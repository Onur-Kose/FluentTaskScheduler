namespace FluentTaskScheduler.Core;

internal static class JobSchedule
{
    // All calendar restrictions are evaluated in UTC. The end of a window is exclusive.
    internal static bool CanRunAt(TimedJobConfig job, DateTime now) =>
        job.ExcludedDays?.Contains(now.DayOfWeek) != true &&
        (!job.IntervalStart.HasValue || now.TimeOfDay >= job.IntervalStart.Value) &&
        (!job.IntervalEnd.HasValue || now.TimeOfDay < job.IntervalEnd.Value);

    internal static DateTime CalculateNextRun(TimedJobConfig job, DateTime now)
    {
        if (job.ExcludedDays?.Distinct().Count() == 7)
            throw new InvalidOperationException("All days are excluded. The job would never run.");

        if (job.DailyAtTimes.Count > 0)
        {
            // Search whole days so that skipping a day also resets to its earliest time.
            for (var offset = 0; offset <= 7; offset++)
            {
                var day = now.Date.AddDays(offset);
                if (job.ExcludedDays?.Contains(day.DayOfWeek) == true)
                    continue;

                foreach (var time in job.DailyAtTimes.OrderBy(time => time))
                {
                    var candidate = day + time;
                    if (candidate > now)
                        return candidate;
                }
            }

            throw new InvalidOperationException("No valid daily execution time was found.");
        }

        var interval = job.RepeatEvery
            ?? throw new InvalidOperationException("Specify either Every(...) or DailyAt(...).");
        var next = job.IntervalStart.HasValue && now.TimeOfDay < job.IntervalStart.Value
            ? now.Date + job.IntervalStart.Value
            : now + interval;

        // Move by calendar days, not by intervals: a weekly interval on an excluded
        // weekday must not loop forever, and short intervals must not busy-loop.
        while (true)
        {
            if (job.ExcludedDays?.Contains(next.DayOfWeek) == true)
            {
                next = next.Date.AddDays(1);
                continue;
            }

            if (job.IntervalStart.HasValue && job.IntervalEnd.HasValue)
            {
                if (next.TimeOfDay < job.IntervalStart.Value)
                    next = next.Date + job.IntervalStart.Value;
                else if (next.TimeOfDay >= job.IntervalEnd.Value)
                {
                    next = next.Date.AddDays(1) + job.IntervalStart.Value;
                    continue;
                }
            }

            return next;
        }
    }
}
