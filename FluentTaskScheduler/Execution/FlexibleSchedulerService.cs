using System.Diagnostics;
using System.Globalization;
using FluentTaskScheduler.Core;
using FluentTaskScheduler.Diagnostics;
using FluentTaskScheduler.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentTaskScheduler.Execution;

public class FlexibleSchedulerService : BackgroundService
{
    private static readonly TimeSpan MaxTimerDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    private readonly IServiceProvider _sp;
    private readonly ILogger<FlexibleSchedulerService> _logger;
    private readonly IScheduledJobRegistry _registry;
    private readonly IJobStateStore _store;
    private readonly SchedulerOptions _options;
    private readonly SchedulerDiagnostics _diagnostics;
    private readonly bool _ownsDiagnostics;

    public FlexibleSchedulerService(IServiceProvider serviceProvider, ILogger<FlexibleSchedulerService> logger)
    {
        _sp = serviceProvider;
        _logger = logger;
        _registry = _sp.GetRequiredService<IScheduledJobRegistry>();
        _store = _sp.GetService<IJobStateStore>() ?? new MemoryJobStateStore();
        _options = _sp.GetService<IOptions<SchedulerOptions>>()?.Value ?? new SchedulerOptions();
        _options.Validate();
        _diagnostics = _sp.GetService<SchedulerDiagnostics>() ?? new SchedulerDiagnostics();
        _ownsDiagnostics = _sp.GetService<SchedulerDiagnostics>() is null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runningTasks = new List<Task>();
        var runtimes = new Dictionary<TimedJobConfig, Runtime>();
        _diagnostics.SetRunning(true);
        _logger.LogInformation("FlexibleSchedulerService started with concurrency {Limit}.", _options.MaxConcurrency);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var waits = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                try
                {
                    var change = _registry.WaitForChangeAsync(waits.Token);
                    runningTasks.RemoveAll(task => task.IsCompleted);
                    var now = DateTime.UtcNow;
                    DateTime? wakeAt = null;
                    // Due order prevents a frequently running job from starving older pending jobs.
                    foreach (var job in _registry.GetJobs().OrderBy(job => job.NextRun))
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        lock (job)
                        {
                            if (job.IsExecuting) continue;
                            if (!runtimes.TryGetValue(job, out var runtime))
                            {
                                runtime = new Runtime(job.Key ?? Guid.NewGuid().ToString("N"), job.Revision);
                                runtimes.Add(job, runtime);
                                _diagnostics.Waiting(runtime.Key, job.Name, job.NextRun, job.IsPaused || job.IsRunning);
                            }
                            if (runtime.BlockedUntil > now && runtime.BlockedRevision == job.Revision)
                            {
                                Earlier(ref wakeAt, runtime.BlockedUntil);
                                continue;
                            }
                            var sync = _store.RequiresStableKeys &&
                                (!runtime.Initialized || runtime.SeenRevision != job.Revision || now >= runtime.RefreshAt);
                            if ((job.IsPaused || job.IsRunning) && !sync) continue;
                            if (!sync && job.NextRun > now)
                            {
                                Earlier(ref wakeAt, job.NextRun);
                                if (_store.RequiresStableKeys) Earlier(ref wakeAt, runtime.RefreshAt);
                                continue;
                            }
                            // The registry itself is the pending queue. Never create unbounded waiting tasks.
                            if (runningTasks.Count >= _options.MaxConcurrency) continue;
                            if (!job.TryBeginExecution(allowSuspended: sync)) continue;
                            var snapshot = job.Snapshot();
                            runningTasks.Add(Task.Run(() => ProcessJobAsync(job, snapshot, runtime, stoppingToken)));
                        }
                    }

                    var wakeUps = new List<Task>(runningTasks.Count + 2) { change };
                    wakeUps.AddRange(runningTasks);
                    if (wakeAt.HasValue) wakeUps.Add(WaitUntilAsync(wakeAt.Value, waits.Token));
                    _diagnostics.InfrastructureError(null);
                    var completed = await Task.WhenAny(wakeUps);
                    await completed;
                    _diagnostics.InfrastructureError(null);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    // Registry/store infrastructure failures are recoverable and visible through health/telemetry.
                    _diagnostics.InfrastructureError(ex);
                    _logger.LogError(ex, "Scheduler infrastructure failed; retrying.");
                    await waits.CancelAsync();
                    await Task.Delay(_options.InfrastructureRetryDelay, stoppingToken);
                }
                finally { await waits.CancelAsync(); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            await Task.WhenAll(runningTasks);
            _diagnostics.SetRunning(false);
            _logger.LogInformation("FlexibleSchedulerService stopped.");
        }
    }

    private async Task ProcessJobAsync(TimedJobConfig job, TimedJobConfig snapshot, Runtime runtime, CancellationToken stoppingToken)
    {
        try
        {
            JobValidation.Validate(snapshot);
            if (_store.RequiresStableKeys && snapshot.Key is null)
                throw new InvalidOperationException("Persistent jobs require a stable Key. Use WithKey(...).");
            await using var lease = await _store.TryAcquireAsync(runtime.Key, stoppingToken);
            if (lease is null)
            {
                Block(runtime, snapshot.Revision);
                return;
            }

            snapshot = job.Snapshot();
            var state = lease.State;
            state?.Validate();
            var localChange = runtime.SeenRevision != snapshot.Revision;
            if (state is null || localChange)
            {
                state = new JobState
                {
                    NextRunUtc = snapshot.NextRun,
                    Schedule = JobScheduleState.From(snapshot),
                    LastSuccessUtc = state?.LastSuccessUtc,
                    LastError = state?.LastError,
                    ConsecutiveFailures = state?.ConsecutiveFailures ?? 0
                };
                await lease.SaveAsync(state, stoppingToken);
            }
            else
            {
                var restored = job.Restore(state, snapshot.Revision);
                if (restored is null) return;
                snapshot = restored;
            }

            runtime.Initialized = true;
            runtime.SeenRevision = snapshot.Revision;
            runtime.RefreshAt = DateTime.UtcNow + _options.StoreRefreshInterval;
            var now = DateTime.UtcNow;
            if (snapshot.IsPaused || snapshot.IsRunning || state.NextRunUtc > now)
            {
                job.SetNextRun(state.NextRunUtc, snapshot.Revision);
                _diagnostics.Waiting(runtime.Key, snapshot.Name, state.NextRunUtc, snapshot.IsPaused || snapshot.IsRunning,
                    state.LastSuccessUtc, state.LastError, state.ConsecutiveFailures);
                return;
            }
            if (!JobSchedule.CanRunAt(snapshot, now))
            {
                state = state with { NextRunUtc = JobSchedule.CalculateNextRun(snapshot, now) };
                await lease.SaveAsync(state, stoppingToken);
                job.SetNextRun(state.NextRunUtc, snapshot.Revision);
                return;
            }

            var nextRegularRun = JobSchedule.CalculateNextRun(snapshot, now);
            state = state with { ScheduledForUtc = state.ScheduledForUtc ?? state.NextRunUtc };
            // Checkpoint the occurrence BEFORE entering user code. A crash retries the same execution identity.
            await lease.SaveAsync(state, stoppingToken);
            var scheduledFor = state.ScheduledForUtc.Value;
            job.SetNextRun(nextRegularRun, snapshot.Revision);
            var executionId = runtime.Key + ":" + scheduledFor.Ticks.ToString(CultureInfo.InvariantCulture);
            var stopwatch = Stopwatch.StartNew();
            _diagnostics.Started(runtime.Key, snapshot.Name, scheduledFor, nextRegularRun, state.RetryAttempt);
            using var activity = _diagnostics.Activities.StartActivity("scheduler.execute");
            activity?.SetTag("job.key", runtime.Key);
            activity?.SetTag("job.execution_id", executionId);
            activity?.SetTag("job.attempt", state.RetryAttempt + 1);
            Exception? failure = null;
            var outcome = "Succeeded";
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            if ((snapshot.Timeout ?? _options.DefaultTimeout) is { } timeout)
                cancellation.CancelAfter(timeout);
            using var timeoutObservation = cancellation.Token.Register(() =>
            {
                if (!stoppingToken.IsCancellationRequested)
                    _diagnostics.Record(runtime.Key, snapshot.Name, "TimeoutCancellationRequested", nextRegularRun,
                        new TimeoutException("Timeout elapsed; awaiting cooperative cancellation and scope disposal."));
            });

            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                await using (var scope = _sp.CreateAsyncScope())
                {
                    if (scope.ServiceProvider.GetService<JobExecutionContext>() is { } context)
                    {
                        context.JobKey = runtime.Key;
                        context.ExecutionId = executionId;
                        context.Attempt = state.RetryAttempt + 1;
                        context.ScheduledForUtc = scheduledFor;
                        context.CancellationToken = cancellation.Token;
                    }
                    if (snapshot.CancellableFunc is { } cancellable)
                        await cancellable(scope.ServiceProvider, cancellation.Token);
                    else
                        await snapshot.Func(scope.ServiceProvider);
                    cancellation.Token.ThrowIfCancellationRequested();
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                outcome = stoppingToken.IsCancellationRequested ? "Cancelled" : "TimedOut";
                if (outcome == "TimedOut") failure = new TimeoutException("Job exceeded its execution timeout.");
            }
            catch (Exception ex) { failure = ex; outcome = "Failed"; }

            cancellation.CancelAfter(Timeout.InfiniteTimeSpan);
            timeoutObservation.Dispose();

            try
            {
                if (outcome == "Cancelled")
                {
                    // Preserve occurrence/attempt for recovery after shutdown.
                }
                else if (failure is not null && state.RetryAttempt < snapshot.Retry.MaxRetries)
                {
                    var attempt = state.RetryAttempt + 1;
                    var retryAt = DateTime.UtcNow + snapshot.Retry.DelayFor(attempt);
                    if (!JobSchedule.CanRunAt(snapshot, retryAt))
                        retryAt = JobSchedule.CalculateNextRun(snapshot, retryAt);
                    state = state with
                    {
                        NextRunUtc = retryAt, RetryAttempt = attempt, LastError = failure.Message,
                        ConsecutiveFailures = state.ConsecutiveFailures + 1
                    };
                }
                else
                {
                    state = state with
                    {
                        NextRunUtc = nextRegularRun, ScheduledForUtc = null, RetryAttempt = 0,
                        LastSuccessUtc = failure is null ? DateTime.UtcNow : state.LastSuccessUtc,
                        LastError = failure?.Message,
                        ConsecutiveFailures = failure is null ? 0 : state.ConsecutiveFailures + 1
                    };
                }
                // An external update during execution wins over the old run's schedule/retry bookkeeping.
                var latest = job.Snapshot();
                if (latest.Revision != snapshot.Revision)
                {
                    state = state with
                    {
                        NextRunUtc = latest.NextRun, Schedule = JobScheduleState.From(latest),
                        ScheduledForUtc = null, RetryAttempt = 0
                    };
                    snapshot = latest;
                }
                using var commitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await lease.SaveAsync(state, commitTimeout.Token);
                runtime.SeenRevision = snapshot.Revision;
                job.SetNextRun(state.NextRunUtc, snapshot.Revision);
            }
            catch (Exception ex)
            {
                failure = ex;
                outcome = "CheckpointFailed";
                job.SetNextRun(lease.State?.NextRunUtc ?? snapshot.NextRun, snapshot.Revision);
                Block(runtime, snapshot.Revision);
            }
            finally
            {
                stopwatch.Stop();
                _diagnostics.Completed(runtime.Key, snapshot.Name, outcome, job.NextRun, stopwatch.Elapsed, failure);
            }
            if (failure is null) _logger.LogInformation("Job {Key}: {Outcome}.", runtime.Key, outcome);
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, failure.Message);
                _logger.LogError(failure, "Job {Key}: {Outcome}.", runtime.Key, outcome);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Block(runtime, snapshot.Revision);
            _diagnostics.Record(runtime.Key, snapshot.Name, "Unavailable", job.NextRun, ex);
            _logger.LogError(ex, "Job {Key} is unavailable; other jobs continue.", runtime.Key);
        }
        finally { job.EndExecution(); }
    }

    private void Block(Runtime runtime, long revision)
    {
        runtime.BlockedRevision = revision;
        runtime.BlockedUntil = DateTime.UtcNow + _options.InfrastructureRetryDelay;
    }
    private static void Earlier(ref DateTime? current, DateTime candidate)
    {
        if (!current.HasValue || candidate < current) current = candidate;
    }
    private static async Task WaitUntilAsync(DateTime dueAt, CancellationToken token)
    {
        var remaining = dueAt - DateTime.UtcNow;
        while (remaining > MaxTimerDelay)
        {
            await Task.Delay(MaxTimerDelay, token);
            remaining = dueAt - DateTime.UtcNow;
        }
        await Task.Delay(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, token);
    }
    public override void Dispose()
    {
        base.Dispose();
        if (_ownsDiagnostics) _diagnostics.Dispose();
    }
    private sealed class Runtime(string key, long revision)
    {
        public string Key { get; } = key;
        public long SeenRevision = revision;
        public bool Initialized;
        public DateTime RefreshAt;
        public DateTime BlockedUntil;
        public long BlockedRevision;
    }
}
