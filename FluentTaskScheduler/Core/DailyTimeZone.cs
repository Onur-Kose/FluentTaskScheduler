using System.Globalization;

namespace FluentTaskScheduler.Core;

internal static class DailyTimeZone
{
    internal static TimeZoneInfo Resolve(string id)
    {
        if (id.StartsWith("GMT+", StringComparison.Ordinal) || id.StartsWith("GMT-", StringComparison.Ordinal))
        {
            var sign = id[3] == '+' ? 1 : -1;
            if (TimeSpan.TryParseExact(id[4..], @"hh\:mm", CultureInfo.InvariantCulture, out var offset) &&
                offset <= TimeSpan.FromHours(14))
                return TimeZoneInfo.CreateCustomTimeZone(id, offset * sign, id, id);
            throw new TimeZoneNotFoundException($"Invalid GMT offset: {id}.");
        }
        return TimeZoneInfo.FindSystemTimeZoneById(id);
    }
}
