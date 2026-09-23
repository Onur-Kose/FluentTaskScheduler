using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FluentTaskScheduler.Core;
using FluentTaskScheduler.Storage;
using FluentTaskScheduler.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static FluentTaskScheduler.Tests.ProductionSafetyTests;

namespace FluentTaskScheduler.Tests;

public class PersistenceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    private static TimedJobConfig Job(Func<IServiceProvider, Task> action) => new()
    {
        Key = "shared", RepeatEvery = TimeSpan.FromDays(1),
        NextRun = DateTime.UtcNow.AddSeconds(-1), Func = action
    };

    [Fact]
    public async Task RestartRestoresNextRunAndDoesNotRepeatCompletedOccurrence()
    {
        using var directory = new TemporaryDirectory();
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Job(_ => { ran.TrySetResult(); return Task.CompletedTask; });
        DateTime nextRun;
        await using (var harness = await Harness.Start(first, s => s.AddFileJobStateStore(directory.Path)))
        {
            await ran.Task.WaitAsync(Limit);
            await Until(() => !first.IsRunning);
            nextRun = first.NextRun;
        }
        var count = 0;
        var second = Job(_ => { Interlocked.Increment(ref count); return Task.CompletedTask; });
        await using (var harness = await Harness.Start(second, s => s.AddFileJobStateStore(directory.Path)))
        {
            await Until(() => second.NextRun == nextRun && !second.IsRunning);
            await Task.Delay(100);
            Assert.Equal(0, Volatile.Read(ref count));
        }
    }

    [Fact]
    public async Task InterruptedOccurrenceRetainsExecutionIdentityAndRetryAttempt()
    {
        using var directory = new TemporaryDirectory();
        var due = DateTime.UtcNow.AddMinutes(-10);
        var store = new FileJobStateStore(directory.Path);
        await using (var lease = await store.TryAcquireAsync("shared", default))
        {
            await lease!.SaveAsync(new JobState
            {
                NextRunUtc = DateTime.UtcNow.AddMinutes(-1), ScheduledForUtc = due, RetryAttempt = 1,
                Schedule = new JobScheduleState { RepeatEvery = TimeSpan.FromDays(1), Retry = new RetryPolicy { MaxRetries = 3 } }
            }, default);
        }
        var executed = new TaskCompletionSource<(string Id, int Attempt)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = Job(sp =>
        {
            var context = sp.GetRequiredService<JobExecutionContext>();
            executed.TrySetResult((context.ExecutionId, context.Attempt));
            return Task.CompletedTask;
        });
        await using var harness = await Harness.Start(job, s => s.AddFileJobStateStore(directory.Path));
        var execution = await executed.Task.WaitAsync(Limit);
        Assert.Equal("shared:" + due.Ticks, execution.Id);
        Assert.Equal(2, execution.Attempt);
    }

    [Fact]
    public async Task TwoSchedulerInstancesExecuteSharedOccurrenceOnlyOnce()
    {
        using var directory = new TemporaryDirectory();
        var count = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<IServiceProvider, Task> action = async _ =>
        {
            Interlocked.Increment(ref count);
            started.TrySetResult();
            await Task.Delay(150);
        };
        var first = Job(action);
        var second = Job(action);
        await using var one = await Harness.Start(first, s => s.AddFileJobStateStore(directory.Path));
        await using var two = await Harness.Start(second, s => s.AddFileJobStateStore(directory.Path));
        await started.Task.WaitAsync(Limit);
        await Until(() => first.NextRun > DateTime.UtcNow && second.NextRun > DateTime.UtcNow && !first.IsRunning && !second.IsRunning);
        Assert.Equal(1, Volatile.Read(ref count));
    }

    [Fact]
    public async Task LiveScheduleUpdateIsPersistedAndSeenByAnotherInstance()
    {
        using var directory = new TemporaryDirectory();
        var first = Job(_ => Task.CompletedTask);
        first.NextRun = DateTime.UtcNow.AddDays(1);
        var second = Job(_ => Task.CompletedTask);
        second.NextRun = first.NextRun;
        await using var one = await Harness.Start(first, s => s.AddFileJobStateStore(directory.Path));
        await using var two = await Harness.Start(second, s => s.AddFileJobStateStore(directory.Path));
        await Until(() => Directory.GetFiles(directory.Path, "*.json").Length == 1 && !first.IsRunning && !second.IsRunning);
        first.Update(draft =>
        {
            draft.RepeatEvery = TimeSpan.FromDays(3);
            draft.IsPaused = true;
        });
        await Until(() => second.IsPaused && second.RepeatEvery == TimeSpan.FromDays(3));
        var stored = new FileJobStateStore(directory.Path);
        await using var lease = await Acquire(stored, "shared");
        Assert.True(lease.State!.Schedule.IsPaused);
        Assert.Equal(TimeSpan.FromDays(3), lease.State.Schedule.RepeatEvery);
    }

    [Fact]
    public async Task CompletionCheckpointFailureRetriesTheSameOccurrenceAndIsObservable()
    {
        var store = new FailCompletionOnceStore();
        var ids = new List<string>();
        var outcomes = new List<string>();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = Job(sp =>
        {
            ids.Add(sp.GetRequiredService<JobExecutionContext>().ExecutionId);
            return Task.CompletedTask;
        });
        await using var harness = await Harness.Start(job, services => services.AddSingleton<IJobStateStore>(store),
            diagnostics => diagnostics.JobCompleted += result =>
            {
                outcomes.Add(result.Outcome);
                if (result.Outcome == "Succeeded") finished.TrySetResult();
            });
        await finished.Task.WaitAsync(Limit);
        await Until(() => !job.IsRunning);
        Assert.Equal(["CheckpointFailed", "Succeeded"], outcomes);
        Assert.Equal(2, ids.Count);
        Assert.Single(ids.Distinct());
        Assert.True(harness.Diagnostics.GetStatus().IsHealthy);
    }

    [Fact]
    public async Task FileStoreRegistrationRequiresExplicitStableIdentity()
    {
        using var directory = new TemporaryDirectory();
        var services = new ServiceCollection();
        services.AddFluentTaskScheduler().AddFileJobStateStore(directory.Path);
        await using var provider = services.BuildServiceProvider();
        var job = Job(_ => Task.CompletedTask);
        job.Key = null;
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IScheduledJobRegistry>().AddJob(job));
    }

    private static async Task<IJobStateLease> Acquire(IJobStateStore store, string key)
    {
        using var timeout = new CancellationTokenSource(Limit);
        while (true)
        {
            if (await store.TryAcquireAsync(key, timeout.Token) is { } lease) return lease;
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FailCompletionOnceStore : IJobStateStore
    {
        private readonly MemoryJobStateStore _inner = new();
        private int _failed;
        public bool RequiresStableKeys => false;
        public async ValueTask<IJobStateLease?> TryAcquireAsync(string key, CancellationToken token)
        {
            var lease = await _inner.TryAcquireAsync(key, token);
            return lease is null ? null : new FaultingLease(this, lease);
        }
        private sealed class FaultingLease(FailCompletionOnceStore store, IJobStateLease inner) : IJobStateLease
        {
            public JobState? State => inner.State;
            public ValueTask SaveAsync(JobState state, CancellationToken token)
            {
                if (State?.ScheduledForUtc is not null && state.ScheduledForUtc is null &&
                    Interlocked.Exchange(ref store._failed, 1) == 0)
                    throw new IOException("Simulated completion checkpoint failure.");
                return inner.SaveAsync(state, token);
            }
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    [Fact]
    public async Task CorruptStateDoesNotRunJobOrPreventAnotherJobFromRunning()
    {
        using var directory = new TemporaryDirectory();
        var file = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("shared"))) + ".json";
        await File.WriteAllTextAsync(System.IO.Path.Combine(directory.Path, file), "{broken");
        var badRan = false;
        var goodRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bad = Job(_ => { badRan = true; return Task.CompletedTask; });
        var good = Job(_ => { goodRan.TrySetResult(); return Task.CompletedTask; });
        good.Key = "healthy";
        await using var harness = await Harness.Start([bad, good], s => s.AddFileJobStateStore(directory.Path));
        await goodRan.Task.WaitAsync(Limit);
        await Until(() => harness.Diagnostics.GetStatus().Jobs.Any(job => job.Key == "shared" && job.Outcome == "Unavailable"));
        Assert.False(badRan);
        Assert.False(harness.Diagnostics.GetStatus().IsHealthy);
        Assert.False(harness.Scheduler.ExecuteTask!.IsCompleted);
    }

    [Fact]
    public async Task ProcessCrashReleasesExecutionLease()
    {
        using var directory = new TemporaryDirectory();
        using var child = StartWorker("hold", directory.Path);
        try
        {
            Assert.Equal("LOCKED", await child.StandardOutput.ReadLineAsync().WaitAsync(Limit));
            var store = new FileJobStateStore(directory.Path);
            Assert.Null(await store.TryAcquireAsync("shared", default));
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(Limit);
            await using var lease = await store.TryAcquireAsync("shared", default);
            Assert.NotNull(lease);
        }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); } }
    }

    [Fact]
    public async Task IndependentProcessesDoNotDuplicateSharedOccurrence()
    {
        using var directory = new TemporaryDirectory();
        using var first = StartWorker("worker", directory.Path);
        using var second = StartWorker("worker", directory.Path);
        try
        {
            await Task.WhenAll(first.WaitForExitAsync(), second.WaitForExitAsync()).WaitAsync(Limit);
            var firstError = await first.StandardError.ReadToEndAsync();
            var secondError = await second.StandardError.ReadToEndAsync();
            Assert.True(first.ExitCode == 0, firstError);
            Assert.True(second.ExitCode == 0, secondError);
            Assert.Single(await File.ReadAllLinesAsync(System.IO.Path.Combine(directory.Path, "executions.txt")));
        }
        finally
        {
            foreach (var child in new[] { first, second })
                if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
        }
    }

    private static Process StartWorker(string mode, string directory)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(System.IO.Path.Combine(root.FullName, "FluentRunly.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var dll = System.IO.Path.Combine(root.FullName, "FluentTaskScheduler.AotSmoke", "bin", configuration, "net10.0", "FluentTaskScheduler.AotSmoke.dll");
        Assert.True(File.Exists(dll), "Build the smoke project before process tests.");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(dll); start.ArgumentList.Add(mode); start.ArgumentList.Add(directory);
        return Process.Start(start)!;
    }

    internal sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _root = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        public string Path { get; }
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(_root, "FluentTaskScheduler.Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            var resolved = System.IO.Path.GetFullPath(Path);
            if (!resolved.StartsWith(_root, StringComparison.OrdinalIgnoreCase) ||
                !System.IO.Path.GetFileName(resolved).StartsWith("FluentTaskScheduler.Tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to delete an unexpected test directory.");
            Directory.Delete(resolved, recursive: true);
        }
    }
}
