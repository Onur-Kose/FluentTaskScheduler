using System.Collections.Concurrent;
using FluentTaskScheduler.Core;
using FluentTaskScheduler.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentTaskScheduler.Tests;

public class SchedulerWakeUpTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task JobAddedBetweenSnapshotAndWaitIsNotMissed()
    {
        var ran = NewSignal();
        var registry = new NotifyingRegistry();
        registry.AfterFirstSnapshot = () => registry.AddJob(NewJob(ran));
        await using var provider = CreateProvider(registry);
        using var scheduler = CreateScheduler(provider);
        await scheduler.StartAsync(default);
        try
        {
            await ran.Task.WaitAsync(TestTimeout);
        }
        finally { await scheduler.StopAsync(default).WaitAsync(TestTimeout); }

        Assert.All(registry.Waits, wait => Assert.True(wait.Token.IsCancellationRequested));
    }

    [Fact]
    public async Task JobAddedWhileIdleRunsAtItsOneSecondDueTime()
    {
        var registry = new NotifyingRegistry();
        var ran = NewSignal();
        DateTime? executedAt = null;
        await using var provider = CreateProvider(registry);
        using var scheduler = CreateScheduler(provider);
        await scheduler.StartAsync(default);
        try
        {
            await registry.FirstSnapshot.Task.WaitAsync(TestTimeout);
            var job = NewJob(ran);
            job.NextRun = DateTime.UtcNow.AddSeconds(1);
            var dueAt = job.NextRun;
            job.Func = _ =>
            {
                executedAt = DateTime.UtcNow;
                ran.TrySetResult();
                return Task.CompletedTask;
            };
            registry.AddJob(job);
            await ran.Task.WaitAsync(TestTimeout);
            Assert.True(executedAt >= dueAt, "The job must not run before its due time.");
        }
        finally { await scheduler.StopAsync(default).WaitAsync(TestTimeout); }
    }

    [Fact]
    public async Task TimerWakeUpsCancelPreviousChangeWaitsAndShutdownCancelsLastWait()
    {
        var registry = new NotifyingRegistry();
        registry.AddJob(NewJob(NewSignal()));
        await using var provider = CreateProvider(registry);
        using var scheduler = CreateScheduler(provider);
        await scheduler.StartAsync(default);
        try
        {
            await registry.FourthWait.Task.WaitAsync(TestTimeout);
            Assert.All(registry.Waits.Take(3), wait =>
            {
                Assert.True(wait.Task.IsCanceled);
                Assert.True(wait.Token.IsCancellationRequested);
            });
        }
        finally { await scheduler.StopAsync(default).WaitAsync(TestTimeout); }

        Assert.All(registry.Waits, wait => Assert.True(wait.Task.IsCanceled));
    }

    [Fact]
    public async Task SnapshotFailureStillCancelsChangeWait()
    {
        var registry = new NotifyingRegistry
        {
            AfterFirstSnapshot = () => throw new InvalidOperationException("Snapshot failed.")
        };
        await using var provider = CreateProvider(registry);
        using var scheduler = CreateScheduler(provider);
        await scheduler.StartAsync(default);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => scheduler.ExecuteTask!.WaitAsync(TestTimeout));
            Assert.Equal("Snapshot failed.", error.Message);
            Assert.True(Assert.Single(registry.Waits).Task.IsCanceled);
        }
        finally { await scheduler.StopAsync(default).WaitAsync(TestTimeout); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdleSchedulerDoesNotPoll(bool hasDistantJob)
    {
        var registry = new NotifyingRegistry();
        if (hasDistantJob)
        {
            var job = NewJob(NewSignal());
            job.NextRun = DateTime.UtcNow.AddDays(90);
            registry.AddJob(job);
        }
        await using var provider = CreateProvider(registry);
        using var scheduler = CreateScheduler(provider);
        await scheduler.StartAsync(default);
        try
        {
            await registry.FirstSnapshot.Task.WaitAsync(TestTimeout);
            await Task.Delay(1200);
            Assert.Equal(1, registry.SnapshotCount);
            Assert.False(Assert.Single(registry.Waits).Task.IsCompleted);
            Assert.False(scheduler.ExecuteTask!.IsCompleted);
        }
        finally { await scheduler.StopAsync(default).WaitAsync(TestTimeout); }
    }

    [Fact]
    public async Task OverrunningJobWakesSchedulerOnCompletionWithoutPolling()
    {
        var registry = new NotifyingRegistry();
        var started = NewSignal();
        var release = NewSignal();
        var repeated = NewSignal();
        var executions = 0;
        var job = NewJob(started);
        job.Func = async _ =>
        {
            if (Interlocked.Increment(ref executions) == 1)
            {
                started.TrySetResult();
                await release.Task;
            }
            else
            {
                repeated.TrySetResult();
            }
        };
        registry.AddJob(job);
        await using var provider = CreateProvider(registry);
        using var scheduler = CreateScheduler(provider);
        await scheduler.StartAsync(default);
        try
        {
            await started.Task.WaitAsync(TestTimeout);
            await Task.Delay(2200);
            Assert.Equal(1, registry.SnapshotCount);
            Assert.Equal(1, Volatile.Read(ref executions));
            release.TrySetResult();
            await repeated.Task.WaitAsync(TestTimeout);
        }
        finally
        {
            release.TrySetResult();
            await scheduler.StopAsync(default).WaitAsync(TestTimeout);
        }
    }

    [Fact]
    public async Task ChangeWaitFailureIsObserved()
    {
        await using var provider = CreateProvider(new FailingRegistry());
        using var scheduler = CreateScheduler(provider);
        await scheduler.StartAsync(default);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => scheduler.ExecuteTask!.WaitAsync(TestTimeout));
            Assert.Equal("Change wait failed.", error.Message);
        }
        finally { await scheduler.StopAsync(default).WaitAsync(TestTimeout); }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TimedJobConfig NewJob(TaskCompletionSource ran) => new()
    {
        RepeatEvery = TimeSpan.FromSeconds(1),
        NextRun = DateTime.UtcNow.AddSeconds(-1),
        Func = _ => { ran.TrySetResult(); return Task.CompletedTask; }
    };

    private static ServiceProvider CreateProvider(IScheduledJobRegistry registry) =>
        new ServiceCollection().AddSingleton(registry).BuildServiceProvider();

    private static FlexibleSchedulerService CreateScheduler(IServiceProvider provider) =>
        new(provider, NullLogger<FlexibleSchedulerService>.Instance);

    private class SnapshotRegistry
    {
        protected readonly ScheduledJobRegistry Inner = new();
        public Action? AfterFirstSnapshot { get; set; }
        public TaskCompletionSource FirstSnapshot { get; } = NewSignal();
        private bool _readFirstSnapshot;
        private int _snapshotCount;
        public int SnapshotCount => Volatile.Read(ref _snapshotCount);

        public void AddJob(TimedJobConfig config) => Inner.AddJob(config);

        public IReadOnlyList<TimedJobConfig> GetJobs()
        {
            Interlocked.Increment(ref _snapshotCount);
            var snapshot = Inner.GetJobs();
            if (!_readFirstSnapshot)
            {
                _readFirstSnapshot = true;
                AfterFirstSnapshot?.Invoke();
                FirstSnapshot.TrySetResult();
            }
            return snapshot;
        }
    }

    private sealed class NotifyingRegistry : SnapshotRegistry, IScheduledJobRegistry
    {
        public ConcurrentQueue<(Task Task, CancellationToken Token)> Waits { get; } = new();
        public TaskCompletionSource FourthWait { get; } = NewSignal();

        public Task WaitForChangeAsync(CancellationToken cancellationToken)
        {
            var task = Inner.WaitForChangeAsync(cancellationToken);
            Waits.Enqueue((task, cancellationToken));
            if (Waits.Count == 4)
                FourthWait.TrySetResult();
            return task;
        }
    }

    private sealed class FailingRegistry : SnapshotRegistry, IScheduledJobRegistry
    {
        public Task WaitForChangeAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("Change wait failed."));
    }
}
