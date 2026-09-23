using System.Collections.Concurrent;
using FluentTaskScheduler.Core;
using FluentTaskScheduler.Diagnostics;
using FluentTaskScheduler.DSL;
using FluentTaskScheduler.Execution;
using FluentTaskScheduler.Extensions;
using FluentTaskScheduler.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentTaskScheduler.Tests;

public class ProductionSafetyTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TimedJobConfig Job(Func<IServiceProvider, Task>? action = null) => new()
    {
        RepeatEvery = TimeSpan.FromDays(1), NextRun = DateTime.UtcNow.AddSeconds(-1),
        Func = action ?? (_ => Task.CompletedTask)
    };

    [Fact]
    public void InvalidRegistrationIsRejectedWithoutPoisoningRegistry()
    {
        var registry = new ScheduledJobRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.AddJob(new TimedJobConfig()));
        var zeroInterval = Job();
        zeroInterval.RepeatEvery = TimeSpan.Zero;
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.AddJob(zeroInterval));
        registry.AddJob(Job());
        Assert.Single(registry.GetJobs());
    }

    [Fact]
    public async Task InvalidEditsRollBackAndValidEditsNotifyIncludingCollections()
    {
        var registry = new ScheduledJobRegistry();
        var job = Job();
        job.ExcludedDays = [DayOfWeek.Sunday];
        registry.AddJob(job);
        var days = job.ExcludedDays;
        using var deadline = new CancellationTokenSource(Limit);
        var changed = registry.WaitForChangeAsync(deadline.Token);
        Assert.Throws<InvalidOperationException>(() => job.IntervalStart = TimeSpan.FromHours(9));
        Assert.Null(job.IntervalStart);
        Assert.False(changed.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => days!.AddRange(Enum.GetValues<DayOfWeek>()));
        Assert.Equal([DayOfWeek.Sunday], days);
        Assert.Same(days, job.ExcludedDays);
        Assert.False(changed.IsCompleted);
        days![0] = DayOfWeek.Saturday;
        await changed.WaitAsync(Limit);
        var nextChange = registry.WaitForChangeAsync(deadline.Token);
        days.Clear();
        await nextChange.WaitAsync(Limit);
    }

    [Fact]
    public async Task AtomicUpdatesCanSwitchScheduleAndNotifyOnce()
    {
        var registry = new ScheduledJobRegistry();
        var job = Job();
        registry.AddJob(job);
        var notifications = 0;
        job.Changed += (_, _) => notifications++;
        job.Update(draft =>
        {
            draft.RepeatEvery = null;
            draft.DailyAtTimes.Add(TimeSpan.FromHours(8));
        });
        Assert.Null(job.RepeatEvery);
        Assert.Single(job.DailyAtTimes);
        Assert.Equal(1, notifications);
        var before = job.NextRun;
        Assert.Throws<InvalidOperationException>(() => job.Update(draft => draft.DailyAtTimes.Clear()));
        Assert.Equal(before, job.NextRun);
        Assert.Single(job.DailyAtTimes);
        Assert.Equal(1, notifications);
        using var deadline = new CancellationTokenSource(Limit);
        var change = registry.WaitForChangeAsync(deadline.Token);
        job.DailyAtTimes.Add(TimeSpan.FromHours(18));
        await change.WaitAsync(Limit);
    }

    [Fact]
    public async Task ObserverFailureCannotSuppressRegistryNotification()
    {
        var job = Job();
        job.Changed += (_, _) => throw new Exception("An observer failed.");
        var registry = new ScheduledJobRegistry();
        registry.AddJob(job);
        using var deadline = new CancellationTokenSource(Limit);
        var changed = registry.WaitForChangeAsync(deadline.Token);
        job.NextRun = DateTime.UtcNow;
        await changed.WaitAsync(Limit);
    }

    [Fact]
    public async Task ExternalNextRunChangeWakesDistantJob()
    {
        var ran = Signal();
        var job = Job(_ => { ran.TrySetResult(); return Task.CompletedTask; });
        job.NextRun = DateTime.UtcNow.AddDays(90);
        await using var harness = await Harness.Start(job);
        await harness.RegistryRead.Task.WaitAsync(Limit);
        job.NextRun = DateTime.UtcNow.AddSeconds(-1);
        await ran.Task.WaitAsync(Limit);
    }

    [Fact]
    public async Task PauseAndResumeAreLiveAndDoNotDiscardTheJob()
    {
        var ran = Signal();
        var job = Job(_ => { ran.TrySetResult(); return Task.CompletedTask; });
        job.IsPaused = true;
        await using var harness = await Harness.Start(job);
        await harness.RegistryRead.Task.WaitAsync(Limit);
        await Task.Delay(100);
        Assert.False(ran.Task.IsCompleted);
        job.IsPaused = false;
        await ran.Task.WaitAsync(Limit);
    }

    [Fact]
    public async Task ShutdownPassesCancellationAndSkipsLaterSequenceSteps()
    {
        var service = new Sequence();
        await using var provider = Services().AddSingleton(service).BuildServiceProvider();
        new SchedulerBuilder<Sequence>(provider).For((s, token) => s.First(token))
            .ThenFor(s => s.Second()).Every(TimeSpan.FromSeconds(1)).Do();
        var job = provider.GetRequiredService<IScheduledJobRegistry>().GetJobs()[0];
        job.NextRun = DateTime.UtcNow.AddSeconds(-1);
        using var scheduler = Scheduler(provider);
        await scheduler.StartAsync(default);
        try
        {
            await service.Started.Task.WaitAsync(Limit);
            await scheduler.StopAsync(default).WaitAsync(Limit);
            Assert.True(service.ReceivedToken.IsCancellationRequested);
            Assert.False(service.SecondRan);
            Assert.False(job.IsRunning);
        }
        finally { await scheduler.StopAsync(default).WaitAsync(Limit); }
    }

    [Fact]
    public async Task ShutdownAlsoSkipsNextStepOfLegacySequence()
    {
        var service = new Sequence();
        await using var provider = Services().AddSingleton(service).BuildServiceProvider();
        new SchedulerBuilder<Sequence>(provider).For(s => s.LegacyFirst())
            .ThenFor(s => s.Second()).Every(TimeSpan.FromSeconds(1)).Do();
        provider.GetRequiredService<IScheduledJobRegistry>().GetJobs()[0].NextRun = DateTime.UtcNow.AddSeconds(-1);
        using var scheduler = Scheduler(provider);
        await scheduler.StartAsync(default);
        try
        {
            await service.Started.Task.WaitAsync(Limit);
            var stopping = scheduler.StopAsync(default);
            service.Release.TrySetResult();
            await stopping.WaitAsync(Limit);
            Assert.False(service.SecondRan);
        }
        finally { service.Release.TrySetResult(); await scheduler.StopAsync(default).WaitAsync(Limit); }
    }

    [Fact]
    public async Task TimeoutCancelsCooperativeJobAndDisposesItsScope()
    {
        var completed = new TaskCompletionSource<JobEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resource = new Resource();
        var job = Job();
        job.Timeout = TimeSpan.FromMilliseconds(75);
        job.CancellableFunc = async (sp, token) =>
        {
            sp.GetRequiredService<Resource>();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        await using var harness = await Harness.Start(job, services => services.AddScoped(_ => resource),
            diagnostics => diagnostics.JobCompleted += e => completed.TrySetResult(e));
        var result = await completed.Task.WaitAsync(Limit);
        Assert.Equal("TimedOut", result.Outcome);
        Assert.True(resource.Disposed);
        Assert.False(harness.Diagnostics.GetStatus().IsHealthy);
    }

    [Fact]
    public async Task NonCooperativeTimeoutKeepsSlotAndManualFlagCannotCauseOverlap()
    {
        var release = Signal();
        var started = Signal();
        var secondStarted = Signal();
        var count = 0;
        var first = Job(async _ =>
        {
            Interlocked.Increment(ref count);
            started.TrySetResult();
            await release.Task;
        });
        first.Timeout = TimeSpan.FromMilliseconds(50);
        first.NextRun = DateTime.UtcNow.AddMinutes(-2);
        var second = Job(_ => { secondStarted.TrySetResult(); return Task.CompletedTask; });
        await using var harness = await Harness.Start([first, second], configure: options => options.MaxConcurrency = 1);
        try
        {
            await started.Task.WaitAsync(Limit);
            await Until(() => harness.Diagnostics.GetStatus().Jobs.Any(j => j.Outcome == "TimeoutCancellationRequested"));
            first.IsRunning = false;
            Assert.True(first.IsRunning);
            Assert.False(secondStarted.Task.IsCompleted);
            Assert.Equal(1, Volatile.Read(ref count));
            release.TrySetResult();
            await secondStarted.Task.WaitAsync(Limit);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task ConcurrencyIsBoundedAndAllPendingJobsEventuallyRun()
    {
        var release = Signal();
        var full = Signal();
        var all = Signal();
        var active = 0;
        var maximum = 0;
        var total = 0;
        var jobs = Enumerable.Range(0, 12).Select(_ => Job(async sp =>
        {
            var current = Interlocked.Increment(ref active);
            int previous;
            do { previous = maximum; } while (current > previous && Interlocked.CompareExchange(ref maximum, current, previous) != previous);
            if (current == 2) full.TrySetResult();
            await release.Task;
            Interlocked.Decrement(ref active);
            if (Interlocked.Increment(ref total) == 12) all.TrySetResult();
        })).ToArray();
        await using var harness = await Harness.Start(jobs, configure: options => options.MaxConcurrency = 2);
        try
        {
            await full.Task.WaitAsync(Limit);
            await Task.Delay(100);
            Assert.Equal(2, Volatile.Read(ref maximum));
            release.TrySetResult();
            await all.Task.WaitAsync(Limit);
            Assert.Equal(2, maximum);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task RetriesUseFreshScopesAndStableExecutionIdentity()
    {
        var contexts = new ConcurrentQueue<(string Id, int Attempt, Resource Resource)>();
        var succeeded = Signal();
        var job = Job(sp =>
        {
            var context = sp.GetRequiredService<JobExecutionContext>();
            contexts.Enqueue((context.ExecutionId, context.Attempt, sp.GetRequiredService<Resource>()));
            if (context.Attempt < 3) throw new InvalidOperationException("Transient");
            succeeded.TrySetResult();
            return Task.CompletedTask;
        });
        job.Retry = new RetryPolicy { MaxRetries = 2, Delay = TimeSpan.FromMilliseconds(30), MaxDelay = TimeSpan.FromMilliseconds(60) };
        await using (var harness = await Harness.Start(job, services => services.AddScoped<Resource>()))
        {
            await succeeded.Task.WaitAsync(Limit);
            await Until(() => !job.IsRunning);
            Assert.True(harness.Diagnostics.GetStatus().IsHealthy);
        }
        Assert.Equal([1, 2, 3], contexts.Select(c => c.Attempt));
        Assert.Single(contexts.Select(c => c.Id).Distinct());
        Assert.Equal(3, contexts.Select(c => c.Resource).Distinct().Count());
        Assert.All(contexts, c => Assert.True(c.Resource.Disposed));
    }

    [Fact]
    public async Task ExternalUpdateDuringRunWinsOverCompletionSchedule()
    {
        var started = Signal();
        var release = Signal();
        var job = Job(async _ => { started.TrySetResult(); await release.Task; });
        await using var harness = await Harness.Start(job);
        try
        {
            await started.Task.WaitAsync(Limit);
            var requested = DateTime.UtcNow.AddDays(7);
            job.NextRun = requested;
            release.TrySetResult();
            await Until(() => !job.IsRunning);
            Assert.Equal(requested, job.NextRun);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task InvalidCustomRegistryJobCannotStopHealthyJobs()
    {
        var ran = Signal();
        var registry = new RawRegistry([new TimedJobConfig(), Job(_ => { ran.TrySetResult(); return Task.CompletedTask; })]);
        await using var provider = Services().AddSingleton<IScheduledJobRegistry>(registry).BuildServiceProvider();
        using var scheduler = Scheduler(provider);
        await scheduler.StartAsync(default);
        try
        {
            await ran.Task.WaitAsync(Limit);
            await Until(() => provider.GetRequiredService<SchedulerDiagnostics>().GetStatus().Jobs.Any(j => j.Outcome == "Unavailable"));
            Assert.False(scheduler.ExecuteTask!.IsCompleted);
        }
        finally { await scheduler.StopAsync(default).WaitAsync(Limit); }
    }

    [Fact]
    public void DuplicateKeysAreRejectedButJobNamesCanRepeat()
    {
        var registry = new ScheduledJobRegistry();
        var first = Job(); first.Key = "daily-report"; first.Name = "Report";
        var second = Job(); second.Key = first.Key; second.Name = first.Name;
        registry.AddJob(first);
        Assert.Throws<InvalidOperationException>(() => registry.AddJob(second));
        second.Key = "another-report";
        registry.AddJob(second);
        Assert.Equal(2, registry.GetJobs().Count);
    }

    internal static async Task Until(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(Limit);
        while (!condition()) await Task.Delay(10, deadline.Token);
    }
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddFluentTaskScheduler(options =>
        {
            options.InfrastructureRetryDelay = TimeSpan.FromMilliseconds(50);
            options.StoreRefreshInterval = TimeSpan.FromMilliseconds(50);
        });
        return services;
    }
    private static FlexibleSchedulerService Scheduler(ServiceProvider provider) =>
        new(provider, NullLogger<FlexibleSchedulerService>.Instance);

    public sealed class Resource : IAsyncDisposable
    {
        public bool Disposed;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    public sealed class Sequence
    {
        public TaskCompletionSource Started { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
        public CancellationToken ReceivedToken;
        public bool SecondRan;
        public async Task First(CancellationToken token) { ReceivedToken = token; Started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
        public async Task LegacyFirst() { Started.TrySetResult(); await Release.Task; }
        public Task Second() { SecondRan = true; return Task.CompletedTask; }
    }
    private sealed class RawRegistry(IReadOnlyList<TimedJobConfig> jobs) : IScheduledJobRegistry
    {
        public void AddJob(TimedJobConfig job) => throw new NotSupportedException();
        public IReadOnlyList<TimedJobConfig> GetJobs() => jobs;
        public Task WaitForChangeAsync(CancellationToken token) => Task.Delay(Timeout.InfiniteTimeSpan, token);
    }
    internal sealed class Harness : IAsyncDisposable
    {
        public ServiceProvider Provider { get; }
        public FlexibleSchedulerService Scheduler { get; }
        public SchedulerDiagnostics Diagnostics => Provider.GetRequiredService<SchedulerDiagnostics>();
        public TaskCompletionSource RegistryRead { get; } = Signal();
        private Harness(ServiceProvider provider)
        {
            Provider = provider;
            Scheduler = ProductionSafetyTests.Scheduler(provider);
        }
        public static Task<Harness> Start(TimedJobConfig job, Action<ServiceCollection>? services = null,
            Action<SchedulerDiagnostics>? diagnostics = null, Action<SchedulerOptions>? configure = null) =>
            Start([job], services, diagnostics, configure);
        public static async Task<Harness> Start(TimedJobConfig[] jobs, Action<ServiceCollection>? services = null,
            Action<SchedulerDiagnostics>? diagnostics = null, Action<SchedulerOptions>? configure = null)
        {
            var collection = Services();
            if (configure is not null) collection.Configure(configure);
            services?.Invoke(collection);
            var inner = new ScheduledJobRegistry();
            foreach (var job in jobs) inner.AddJob(job);
            var observed = new ObservedRegistry(inner);
            collection.AddSingleton<IScheduledJobRegistry>(observed);
            var harness = new Harness(collection.BuildServiceProvider());
            observed.Read = harness.RegistryRead;
            diagnostics?.Invoke(harness.Diagnostics);
            await harness.Scheduler.StartAsync(default);
            return harness;
        }
        public async ValueTask DisposeAsync()
        {
            await Scheduler.StopAsync(default).WaitAsync(Limit);
            Scheduler.Dispose();
            await Provider.DisposeAsync();
        }
        private sealed class ObservedRegistry(ScheduledJobRegistry inner) : IScheduledJobRegistry
        {
            public TaskCompletionSource Read { get; set; } = null!;
            public void AddJob(TimedJobConfig config) => inner.AddJob(config);
            public IReadOnlyList<TimedJobConfig> GetJobs() { var jobs = inner.GetJobs(); Read.TrySetResult(); return jobs; }
            public Task WaitForChangeAsync(CancellationToken token) => inner.WaitForChangeAsync(token);
        }
    }
}
