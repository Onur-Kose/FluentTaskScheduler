using System.Globalization;
using FluentTaskScheduler.Core;
using FluentTaskScheduler.DSL;
using FluentTaskScheduler.Execution;
using FluentTaskScheduler.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentTaskScheduler.Tests;

public class SchedulerTests
{
    private static ServiceProvider CreateProvider() => new ServiceCollection()
        .AddFluentTaskScheduler()
        .AddSingleton<RecordingService>()
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    [Fact]
    public async Task ReusingBuilderPreservesRegisteredStepsAndTheirOrder()
    {
        await using var provider = CreateProvider();
        var builder = new SchedulerBuilder<RecordingService>(provider);
        builder.For(x => x.Record("first")).ThenFor(x => x.Record("second"))
            .Every(TimeSpan.FromSeconds(1)).Do();
        builder.For(x => x.Record("other")).Every(TimeSpan.FromSeconds(1)).Do();
        var jobs = provider.GetRequiredService<IScheduledJobRegistry>().GetJobs();

        await jobs[0].Func(provider);
        await jobs[1].Func(provider);

        Assert.Equal(["first", "second", "other"], provider.GetRequiredService<RecordingService>().Calls);
        Assert.Throws<InvalidOperationException>(() => builder.Do());
        Assert.Throws<InvalidOperationException>(() => builder.ThenFor(x => x.Record("extra")));
        Assert.Equal(2, jobs.Count);
    }

    [Theory]
    [InlineData("24:00")]
    [InlineData("-01:00")]
    [InlineData("1.08:00")]
    [InlineData("09:60")]
    [InlineData("9")]
    [InlineData("")]
    public void InvalidClockTimesAreRejected(string value)
    {
        using var provider = CreateProvider();
        var builder = new SchedulerBuilder<RecordingService>(provider).For(x => x.Record("job"));
        Assert.Throws<ArgumentException>(() => builder.DailyAt(value));
        Assert.Throws<ArgumentException>(() => builder.Between(value, "18:00"));
    }

    [Fact]
    public void GeneratedJobNamePreservesNonPrefixICharacters()
    {
        using var provider = CreateProvider();
        var builder = new SchedulerBuilder<ITaskItemService>(provider);
        builder.For(x => x.Record()).Every(TimeSpan.FromSeconds(1)).Do();
        var jobs = provider.GetRequiredService<IScheduledJobRegistry>().GetJobs();
        Assert.StartsWith("TaskItem_Record_", jobs[0].Name);
        Assert.DoesNotContain("__", jobs[0].Name);
    }

    [Fact]
    public void DocumentedOverloadsRegisterSuccessfully()
    {
        using var provider = CreateProvider();
        var builder = new SchedulerBuilder<RecordingService>(provider);
        builder.For(x => x.Record("daily")).DailyAt("08:00", "18:00:30").Do();
        builder.For(x => x.Record("interval")).Every(TimeSpan.FromMinutes(10))
            .Between(TimeSpan.FromHours(8), TimeSpan.FromHours(18)).Do();
        var jobs = provider.GetRequiredService<IScheduledJobRegistry>().GetJobs();
        Assert.Equal([TimeSpan.FromHours(8), new TimeSpan(18, 0, 30)], jobs[0].DailyAtTimes);
        Assert.Equal(TimeSpan.FromHours(8), jobs[1].IntervalStart);
    }

    [Fact]
    public void ExcludedDaysAreValidatedDeduplicatedAndCopied()
    {
        using var provider = CreateProvider();
        var builder = new SchedulerBuilder<RecordingService>(provider);
        var days = Enumerable.Repeat(DayOfWeek.Sunday, 7).ToArray();
        builder.For(x => x.Record("job")).Every(TimeSpan.FromSeconds(1)).NotRunThisDays(days).Do();
        days[0] = DayOfWeek.Monday;
        Assert.Equal([DayOfWeek.Sunday], provider.GetRequiredService<IScheduledJobRegistry>().GetJobs()[0].ExcludedDays);

        builder.For(x => x.Record("job")).Every(TimeSpan.FromSeconds(1))
            .NotRunThisDays(Enum.GetValues<DayOfWeek>().Append(DayOfWeek.Sunday).ToArray());
        Assert.Throws<InvalidOperationException>(() => builder.Do());
        Assert.Throws<ArgumentException>(() => builder.NotRunThisDays((DayOfWeek)7));
    }

    [Fact]
    public void MissingOrConflictingSchedulesAreRejected()
    {
        using var provider = CreateProvider();
        var builder = new SchedulerBuilder<RecordingService>(provider);
        Assert.Throws<InvalidOperationException>(() => builder.For(x => x.Record("job")).Do());
        Assert.Throws<InvalidOperationException>(() => builder.For(x => x.Record("job"))
            .DailyAt("08:00").Every(TimeSpan.FromSeconds(1)).Do());
        Assert.Throws<InvalidOperationException>(() => builder.For(x => x.Record("job"))
            .DailyAt("08:00").Between("08:00", "18:00").Do());
    }

    [Theory]
    [InlineData("2026-09-25T17:59:00Z", 10, "2026-09-28T08:00:00Z")]
    [InlineData("2026-09-22T17:59:00Z", 10, "2026-09-23T08:00:00Z")]
    [InlineData("2026-09-22T17:00:00Z", 600, "2026-09-23T08:00:00Z")]
    [InlineData("2026-09-22T17:00:00Z", 2880, "2026-09-24T17:00:00Z")]
    [InlineData("2026-09-22T07:00:00Z", 10, "2026-09-22T08:00:00Z")]
    [InlineData("2026-09-22T08:00:00Z", 10, "2026-09-22T08:10:00Z")]
    [InlineData("2026-09-22T18:00:00Z", 10, "2026-09-23T08:00:00Z")]
    public void IntervalsStayWithinAllowedDaysAndWindows(string current, int minutes, string expected)
    {
        var job = new TimedJobConfig
        {
            RepeatEvery = TimeSpan.FromMinutes(minutes),
            IntervalStart = TimeSpan.FromHours(8),
            IntervalEnd = TimeSpan.FromHours(18),
            ExcludedDays = [DayOfWeek.Saturday, DayOfWeek.Sunday]
        };
        Assert.Equal(Utc(expected), JobSchedule.CalculateNextRun(job, Utc(current)));
    }

    [Fact]
    public void DailyScheduleResetsToEarliestTimeAfterExcludedDay()
    {
        var job = new TimedJobConfig
        {
            DailyAtTimes = [TimeSpan.FromHours(18), TimeSpan.FromHours(8)],
            ExcludedDays = [DayOfWeek.Saturday, DayOfWeek.Sunday]
        };
        Assert.Equal(Utc("2026-09-28T08:00:00Z"),
            JobSchedule.CalculateNextRun(job, Utc("2026-09-26T12:00:00Z")));
    }

    [Fact]
    public void WeeklyIntervalOnExcludedWeekdayTerminatesOnAllowedDay()
    {
        var job = new TimedJobConfig
        {
            RepeatEvery = TimeSpan.FromDays(7),
            ExcludedDays = [DayOfWeek.Sunday]
        };
        Assert.Equal(Utc("2026-10-05T00:00:00Z"),
            JobSchedule.CalculateNextRun(job, Utc("2026-09-27T12:00:00Z")));
    }

    [Theory]
    [InlineData("2026-09-22T07:59:59Z", false)]
    [InlineData("2026-09-22T08:00:00Z", true)]
    [InlineData("2026-09-22T17:59:59Z", true)]
    [InlineData("2026-09-22T18:00:00Z", false)]
    [InlineData("2026-09-26T12:00:00Z", false)]
    public void ExecutionRestrictionsRespectWindowBoundaries(string current, bool allowed)
    {
        var job = new TimedJobConfig
        {
            IntervalStart = TimeSpan.FromHours(8),
            IntervalEnd = TimeSpan.FromHours(18),
            ExcludedDays = [DayOfWeek.Saturday]
        };
        Assert.Equal(allowed, JobSchedule.CanRunAt(job, Utc(current)));
    }

    [Fact]
    public async Task RegistryReturnsStableSnapshotsDuringConcurrentRegistration()
    {
        var registry = new ScheduledJobRegistry();
        var snapshot = registry.GetJobs();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                registry.AddJob(new TimedJobConfig());
                Assert.NotNull(registry.GetJobs());
            }
        })));
        Assert.Empty(snapshot);
        Assert.Equal(8000, registry.GetJobs().Count);
    }

    [Fact]
    public void RepeatedRegistrationPreservesExistingRegistry()
    {
        var registry = new ScheduledJobRegistry();
        var services = new ServiceCollection().AddSingleton<IScheduledJobRegistry>(registry);
        services.AddFluentTaskScheduler().AddFluentTaskScheduler();
        using var provider = services.BuildServiceProvider();
        Assert.Same(registry, provider.GetRequiredService<IScheduledJobRegistry>());
        Assert.Single(provider.GetServices<IScheduledJobRegistry>());
    }

    [Fact]
    public async Task ShutdownWaitsForRunningJobAndDisposesAsyncScope()
    {
        var started = new TaskCompletionSource<ScopedJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        await using var provider = new ServiceCollection().AddFluentTaskScheduler()
            .AddScoped(_ =>
            {
                Interlocked.Increment(ref created);
                return new ScopedJob(started, release.Task);
            })
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        new SchedulerBuilder<ScopedJob>(provider).For(x => x.RunAsync()).Every(TimeSpan.FromSeconds(1)).Do();
        var job = provider.GetRequiredService<IScheduledJobRegistry>().GetJobs()[0];
        job.NextRun = DateTime.UtcNow.AddSeconds(-1);
        using var scheduler = new FlexibleSchedulerService(provider, NullLogger<FlexibleSchedulerService>.Instance);
        await scheduler.StartAsync(default);
        Task? stopping = null;
        try
        {
            var instance = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Leave the job active over two polling cycles to verify non-overlap.
            await Task.Delay(2200);
            Assert.Equal(1, instance.ExecutionCount);
            Assert.Equal(1, Volatile.Read(ref created));
            stopping = scheduler.StopAsync(default);
            Assert.False(stopping.IsCompleted);
            Assert.False(instance.Disposed);
            release.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(instance.Disposed);
            Assert.False(job.IsRunning);
        }
        finally
        {
            release.TrySetResult();
            await (stopping ?? scheduler.StopAsync(default)).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task OverdueJobIsNotExecutedOnExcludedDay()
    {
        await using var provider = CreateProvider();
        var ran = false;
        var now = DateTime.UtcNow;
        var job = new TimedJobConfig
        {
            Func = _ => { ran = true; return Task.CompletedTask; },
            RepeatEvery = TimeSpan.FromSeconds(1),
            NextRun = now.AddDays(-1),
            ExcludedDays = [now.DayOfWeek, now.AddDays(1).DayOfWeek]
        };
        provider.GetRequiredService<IScheduledJobRegistry>().AddJob(job);
        using var scheduler = new FlexibleSchedulerService(provider, NullLogger<FlexibleSchedulerService>.Instance);
        await scheduler.StartAsync(default);
        try
        {
            await Task.Delay(1200);
        }
        finally
        {
            await scheduler.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.False(ran);
        Assert.True(job.NextRun > now);
        Assert.DoesNotContain(job.NextRun.DayOfWeek, job.ExcludedDays);
    }

    private static DateTime Utc(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture,
        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    [Fact]
    public async Task FailedJobsCanRunAgainWithFreshScopes()
    {
        var instances = new List<AsyncResource>();
        var repeated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = new ServiceCollection().AddFluentTaskScheduler()
            .AddScoped<AsyncResource>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var job = new TimedJobConfig
        {
            RepeatEvery = TimeSpan.FromSeconds(1),
            Func = sp =>
            {
                instances.Add(sp.GetRequiredService<AsyncResource>());
                if (instances.Count >= 2)
                    repeated.TrySetResult();
                throw new InvalidOperationException("Expected job failure.");
            }
        };
        provider.GetRequiredService<IScheduledJobRegistry>().AddJob(job);
        using var scheduler = new FlexibleSchedulerService(provider, NullLogger<FlexibleSchedulerService>.Instance);
        await scheduler.StartAsync(default);
        try
        {
            await repeated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await scheduler.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.True(instances.Count >= 2);
        Assert.Equal(instances.Count, instances.Distinct().Count());
        Assert.All(instances, instance => Assert.True(instance.Disposed));
        Assert.False(job.IsRunning);
    }

    public sealed class AsyncResource : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    public sealed class RecordingService
    {
        public List<string> Calls { get; } = [];
        public Task Record(string value) { Calls.Add(value); return Task.CompletedTask; }
    }

    public interface ITaskItemService
    {
        Task Record();
    }

    public sealed class ScopedJob(TaskCompletionSource<ScopedJob> started, Task release) : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public int ExecutionCount;
        public async Task RunAsync()
        {
            Interlocked.Increment(ref ExecutionCount);
            started.TrySetResult(this);
            await release;
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
