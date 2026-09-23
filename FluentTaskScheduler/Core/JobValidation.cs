namespace FluentTaskScheduler.Core;

internal static class JobValidation
{
    internal static void Validate(TimedJobConfig job)
    {
        if (job.Func is null && job.CancellableFunc is null)
            throw new InvalidOperationException("A job must define Func or CancellableFunc.");
        if ((job.DailyAtTimes.Count > 0) == job.RepeatEvery.HasValue)
            throw new InvalidOperationException("Specify exactly one of Every(...) or DailyAt(...).");
        if (job.RepeatEvery is { } interval && interval < TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(job.RepeatEvery), "Intervals must be at least one second.");
        if (job.DailyAtTimes.Any(time => time < TimeSpan.Zero || time >= TimeSpan.FromDays(1)))
            throw new ArgumentOutOfRangeException(nameof(job.DailyAtTimes));
        if (job.ExcludedDays is { } days && (days.Any(day => !Enum.IsDefined(day)) || days.Distinct().Count() == 7))
            throw new InvalidOperationException("Excluded days must be valid and leave at least one allowed day.");
        if (job.IntervalStart.HasValue != job.IntervalEnd.HasValue ||
            (job.IntervalStart.HasValue && (!job.RepeatEvery.HasValue || job.IntervalStart < TimeSpan.Zero ||
                job.IntervalEnd >= TimeSpan.FromDays(1) || job.IntervalStart >= job.IntervalEnd)))
            throw new InvalidOperationException("A window requires Every(...) and 00:00 <= start < end < 24:00.");
        if (job.Timeout is { } timeout && (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(job.Timeout));
        if (job.Retry.MaxRetries < 0 || job.Retry.Delay <= TimeSpan.Zero || job.Retry.MaxDelay < job.Retry.Delay ||
            !double.IsFinite(job.Retry.BackoffFactor) || job.Retry.BackoffFactor < 1)
            throw new ArgumentException("Invalid retry policy.", nameof(job.Retry));
        if (job.Key is not null && string.IsNullOrWhiteSpace(job.Key))
            throw new ArgumentException("Key cannot be empty.", nameof(job.Key));
    }
}
