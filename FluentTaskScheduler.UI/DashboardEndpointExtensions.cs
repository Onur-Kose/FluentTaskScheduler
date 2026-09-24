using FluentTaskScheduler.Core;
using FluentTaskScheduler.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentTaskScheduler.UI;

/// <summary>Maps a read-only scheduler dashboard and its JSON status endpoint.</summary>
public static class DashboardEndpointExtensions
{
    public static IEndpointRouteBuilder MapFluentTaskSchedulerDashboard(
        this IEndpointRouteBuilder endpoints, string path = "/scheduler", bool onlyInDevelopment = true)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.StartsWith('/') || path.Contains('{') || path.Contains('?') || path.Contains('#'))
            throw new ArgumentException("Use an absolute, literal route path such as /scheduler.", nameof(path));

        if (onlyInDevelopment && !endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            return endpoints;

        if (endpoints.ServiceProvider.GetService<SchedulerDiagnostics>() is null ||
            endpoints.ServiceProvider.GetService<IScheduledJobRegistry>() is null)
            throw new InvalidOperationException("Register the scheduler with AddFluentTaskScheduler() before mapping its dashboard.");

        var route = path.TrimEnd('/');
        if (route.Length == 0) route = "/";
        var assembly = typeof(DashboardEndpointExtensions).Assembly;

        endpoints.MapGet(route, () =>
        {
            using var stream = assembly.GetManifestResourceStream("FluentTaskScheduler.UI.wwwroot.index.html")
                ?? throw new InvalidOperationException("Dashboard resource is missing.");
            using var reader = new StreamReader(stream);
            return Results.Content(reader.ReadToEnd(), "text/html; charset=utf-8");
        });

        endpoints.MapGet((route == "/" ? "" : route) + "/api/status", (SchedulerDiagnostics diagnostics, IScheduledJobRegistry registry) =>
        {
            var status = diagnostics.GetStatus();
            var definitions = registry.GetJobs();
            var jobs = status.Jobs.ToDictionary(job => job.Key, StringComparer.Ordinal);
            var rows = new List<DashboardJob>();

            foreach (var definition in definitions)
            {
                // Jobs without a stable key are matched by name only for display purposes.
                var match = definition.Key is { } key
                    ? jobs.GetValueOrDefault(key)
                    : status.Jobs.FirstOrDefault(job => job.Name == definition.Name &&
                        !rows.Any(row => row.Key == job.Key));
                if (match is not null) jobs.Remove(match.Key);
                rows.Add(new DashboardJob(
                    match?.Key ?? definition.Key ?? definition.Name,
                    definition.Name,
                    definition.IsPaused ? "Paused" : definition.IsRunning ? "Running" : match?.Outcome ?? "Scheduled",
                    definition.NextRun,
                    match?.LastStartedUtc,
                    match?.LastSuccessUtc,
                    match?.Error,
                    match?.ConsecutiveFailures ?? 0,
                    definition.IsPaused,
                    definition.RepeatEvery,
                    definition.DailyAtTimes.ToArray())
                {
                    LastCompletedUtc = match?.LastCompletedUtc,
                    LastDuration = match?.LastDuration,
                    ErrorType = match?.ErrorType,
                    ErrorCode = match?.ErrorCode,
                    IntervalStart = definition.IntervalStart,
                    IntervalEnd = definition.IntervalEnd,
                    ExcludedDays = definition.ExcludedDays?.ToArray() ?? []
                });
            }

            foreach (var job in jobs.Values)
                rows.Add(new DashboardJob(job.Key, job.Name, job.Outcome, job.NextRunUtc,
                    job.LastStartedUtc, job.LastSuccessUtc, job.Error, job.ConsecutiveFailures,
                    false, null, [])
                {
                    LastCompletedUtc = job.LastCompletedUtc,
                    LastDuration = job.LastDuration,
                    ErrorType = job.ErrorType,
                    ErrorCode = job.ErrorCode
                });

            return Results.Json(new DashboardSnapshot(status.IsRunning, status.IsHealthy, status.Error,
                DateTime.UtcNow, rows.OrderBy(job => job.NextRunUtc).ToArray(), diagnostics.GetRecentEvents()));
        });

        return endpoints;
    }
}

public sealed record DashboardSnapshot(bool IsRunning, bool IsHealthy, string? Error,
    DateTime TimestampUtc, IReadOnlyList<DashboardJob> Jobs, IReadOnlyList<JobEvent> RecentEvents);

public sealed record DashboardJob(string Key, string Name, string Outcome, DateTime NextRunUtc,
    DateTime? LastStartedUtc, DateTime? LastSuccessUtc, string? Error, int ConsecutiveFailures,
    bool IsPaused, TimeSpan? RepeatEvery, IReadOnlyList<TimeSpan> DailyAtTimes)
{
    public DateTime? LastCompletedUtc { get; init; }
    public TimeSpan? LastDuration { get; init; }
    public string? ErrorType { get; init; }
    public string? ErrorCode { get; init; }
    public TimeSpan? IntervalStart { get; init; }
    public TimeSpan? IntervalEnd { get; init; }
    public IReadOnlyList<DayOfWeek> ExcludedDays { get; init; } = [];
}
