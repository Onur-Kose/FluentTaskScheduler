using FluentTaskScheduler.Core;

namespace FluentTaskScheduler.Storage;

/// <summary>Execution leases must remain exclusive until disposal, including during SaveAsync.</summary>
public interface IJobStateStore
{
    bool RequiresStableKeys { get; }
    ValueTask<IJobStateLease?> TryAcquireAsync(string key, CancellationToken cancellationToken);
}

public interface IJobStateLease : IAsyncDisposable
{
    JobState? State { get; }
    /// <summary>Durably and atomically replaces state before returning. Does not release the lease.</summary>
    ValueTask SaveAsync(JobState state, CancellationToken cancellationToken);
}

public sealed record JobState
{
    public int FormatVersion { get; init; } = 1;
    public DateTime NextRunUtc { get; init; }
    public DateTime? ScheduledForUtc { get; init; }
    public int RetryAttempt { get; init; }
    public DateTime? LastSuccessUtc { get; init; }
    public string? LastError { get; init; }
    public int ConsecutiveFailures { get; init; }
    public JobScheduleState Schedule { get; init; } = new();

    internal void Validate()
    {
        if (FormatVersion != 1 || Schedule is null || RetryAttempt < 0 || ConsecutiveFailures < 0 ||
            (RetryAttempt > 0 && ScheduledForUtc is null))
            throw new InvalidDataException("Invalid or unsupported stored job state.");
    }
}

/// <summary>Serializable scheduling configuration; executable delegates are registered by application code.</summary>
public sealed record JobScheduleState
{
    public TimeSpan? RepeatEvery { get; init; }
    public TimeSpan[] DailyAtTimes { get; init; } = [];
    public DayOfWeek[]? ExcludedDays { get; init; }
    public TimeSpan? IntervalStart { get; init; }
    public TimeSpan? IntervalEnd { get; init; }
    public TimeSpan? Timeout { get; init; }
    public RetryPolicy Retry { get; init; } = new();
    public bool IsPaused { get; init; }
    public bool IsManuallyRunning { get; init; }

    internal static JobScheduleState From(TimedJobConfig job) => new()
    {
        RepeatEvery = job.RepeatEvery, DailyAtTimes = job.DailyAtTimes.ToArray(),
        ExcludedDays = job.ExcludedDays?.ToArray(), IntervalStart = job.IntervalStart,
        IntervalEnd = job.IntervalEnd, Timeout = job.Timeout, Retry = job.Retry,
        IsPaused = job.IsPaused, IsManuallyRunning = job.IsRunning
    };

    internal void Apply(TimedJobConfig job) => job.Update(draft =>
    {
        draft.RepeatEvery = RepeatEvery; draft.DailyAtTimes = DailyAtTimes;
        draft.ExcludedDays = ExcludedDays is null ? null : new JobCollection<DayOfWeek>(ExcludedDays); draft.IntervalStart = IntervalStart;
        draft.IntervalEnd = IntervalEnd; draft.Timeout = Timeout; draft.Retry = Retry;
        draft.IsPaused = IsPaused; draft.IsRunning = IsManuallyRunning;
    });
}
