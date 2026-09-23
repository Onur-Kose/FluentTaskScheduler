using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FluentTaskScheduler.Diagnostics;

public sealed record JobStatus(string Key, string Name, string Outcome, DateTime? LastSuccessUtc,
    DateTime? LastStartedUtc, DateTime NextRunUtc, string? Error, int ConsecutiveFailures);
public sealed record SchedulerStatus(bool IsRunning, bool IsHealthy, string? Error, IReadOnlyList<JobStatus> Jobs);
public sealed record JobEvent(string Key, string Name, string Outcome, DateTime TimestampUtc, string? Error);

/// <summary>Subscribe to the FluentTaskScheduler meter/activity source, or inspect GetStatus from a health endpoint.</summary>
public sealed class SchedulerDiagnostics : IDisposable
{
    public const string InstrumentationName = "FluentTaskScheduler";
    private readonly Meter _meter = new(InstrumentationName);
    private readonly ConcurrentDictionary<string, JobStatus> _jobs = new(StringComparer.Ordinal);
    private readonly Counter<long> _executions, _failures, _retries, _timeouts;
    private readonly UpDownCounter<long> _active;
    private readonly Histogram<double> _duration, _lateness;
    private volatile bool _running;
    private volatile string? _error;
    public ActivitySource Activities { get; } = new(InstrumentationName);
    public event Action<JobEvent>? JobCompleted;

    public SchedulerDiagnostics()
    {
        _executions = _meter.CreateCounter<long>("scheduler.job.executions");
        _failures = _meter.CreateCounter<long>("scheduler.job.failures");
        _retries = _meter.CreateCounter<long>("scheduler.job.retries");
        _timeouts = _meter.CreateCounter<long>("scheduler.job.timeouts");
        _active = _meter.CreateUpDownCounter<long>("scheduler.job.active");
        _duration = _meter.CreateHistogram<double>("scheduler.job.duration", "s");
        _lateness = _meter.CreateHistogram<double>("scheduler.job.lateness", "s");
    }
    public SchedulerStatus GetStatus()
    {
        var jobs = _jobs.Values.OrderBy(job => job.Key).ToArray();
        return new(_running, _running && _error is null && jobs.All(job => job.Error is null), _error, jobs);
    }
    internal void SetRunning(bool running) => _running = running;
    internal void InfrastructureError(Exception? error) => _error = error?.Message;
    internal void Waiting(string key, string name, DateTime nextRun, bool paused,
        DateTime? lastSuccess = null, string? error = null, int failures = 0)
    {
        var outcome = paused ? "Paused" : error is null ? "Scheduled" : "Failed";
        _jobs.AddOrUpdate(key, new JobStatus(key, name, outcome, lastSuccess, null, nextRun, error, failures),
            (_, previous) => previous with
            {
                Name = name, Outcome = outcome, NextRunUtc = nextRun,
                LastSuccessUtc = lastSuccess ?? previous.LastSuccessUtc,
                Error = error, ConsecutiveFailures = failures
            });
    }
    internal void Started(string key, string name, DateTime scheduledFor, DateTime nextRun, int attempt)
    {
        _active.Add(1); _executions.Add(1);
        if (attempt > 0) _retries.Add(1);
        _lateness.Record(Math.Max(0, (DateTime.UtcNow - scheduledFor).TotalSeconds));
        _jobs.AddOrUpdate(key, new JobStatus(key, name, "Running", null, DateTime.UtcNow, nextRun, null, 0),
            (_, previous) => previous with { Name = name, Outcome = "Running", LastStartedUtc = DateTime.UtcNow, NextRunUtc = nextRun });
    }
    internal void Completed(string key, string name, string outcome, DateTime nextRun, TimeSpan duration, Exception? error)
    {
        _active.Add(-1); _duration.Record(duration.TotalSeconds);
        if (error is not null) _failures.Add(1);
        if (outcome == "TimedOut") _timeouts.Add(1);
        Record(key, name, outcome, nextRun, error);
        if (JobCompleted is { } completed)
            foreach (Action<JobEvent> observer in completed.GetInvocationList())
                try { observer(new(key, name, outcome, DateTime.UtcNow, error?.Message)); } catch { /* isolate telemetry observers */ }
    }
    internal void Record(string key, string name, string outcome, DateTime nextRun, Exception? error)
    {
        _jobs.AddOrUpdate(key, new JobStatus(key, name, outcome, outcome == "Succeeded" ? DateTime.UtcNow : null,
            null, nextRun, error?.Message, error is null ? 0 : 1),
            (_, previous) => previous with
            {
                Name = name, Outcome = outcome, NextRunUtc = nextRun, Error = error?.Message,
                LastSuccessUtc = outcome == "Succeeded" ? DateTime.UtcNow : previous.LastSuccessUtc,
                ConsecutiveFailures = error is null ? 0 : previous.ConsecutiveFailures + 1
            });
    }
    public void Dispose() { Activities.Dispose(); _meter.Dispose(); }
}
