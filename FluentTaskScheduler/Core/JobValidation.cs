namespace FluentTaskScheduler.Core;

internal static class JobValidation
{
    internal static void Validate(TimedJobConfig job)
    {
        if (job.Func is null && job.CancellableFunc is null)
            throw new InvalidOperationException("A job must define Func or CancellableFunc.");
        var scheduleCount = (job.DailyAtTimes.Count > 0 ? 1 : 0) +
            (job.RepeatEvery.HasValue ? 1 : 0) + (job.RunOnceAtUtc.HasValue ? 1 : 0);
        if (scheduleCount != 1)
            throw new InvalidOperationException("Specify exactly one of Every(...), a daily schedule, or RunOnceAtUtc(...).");
        if (job.RunOnceAtUtc is { Kind: not DateTimeKind.Utc })
            throw new ArgumentException("RunOnceAtUtc requires a DateTime with Kind.Utc.", nameof(job.RunOnceAtUtc));
        if (job.RunOnceAtUtc.HasValue && job.ExcludedDays is { Count: > 0 })
            throw new InvalidOperationException("Excluded days cannot be combined with RunOnceAtUtc(...).");
        if (job.RepeatEvery is { } interval && interval < TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(job.RepeatEvery), "Intervals must be at least one second.");
        if (job.DailyAtTimes.Any(time => time < TimeSpan.Zero || time >= TimeSpan.FromDays(1)))
            throw new ArgumentOutOfRangeException(nameof(job.DailyAtTimes));
        if (job.DailyTimeZoneId is { } zoneId)
        {
            if (job.DailyAtTimes.Count == 0 || string.IsNullOrWhiteSpace(zoneId))
                throw new InvalidOperationException("A daily time zone requires daily times.");
            _ = DailyTimeZone.Resolve(zoneId);
        }
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
