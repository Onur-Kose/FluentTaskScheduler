using FluentTaskScheduler.Diagnostics;

namespace FluentTaskScheduler.Tests;

public class DashboardDiagnosticsTests
{
    [Xunit.Fact]
    public void RecentEventsKeepsNewestHundredCompletedExecutions()
    {
        using var diagnostics = new SchedulerDiagnostics();
        for (var index = 0; index < 105; index++)
            diagnostics.Completed($"job-{index}", $"Job {index}", "Succeeded",
                DateTime.UtcNow.AddMinutes(1), TimeSpan.Zero, null);

        var events = diagnostics.GetRecentEvents();
        Xunit.Assert.Equal(100, events.Count);
        Xunit.Assert.Equal("job-104", events[0].Key);
        Xunit.Assert.Equal("job-5", events[^1].Key);
    }

    [Xunit.Fact]
    public void CompletedExecutionExposesTimingAndErrorDetails()
    {
        using var diagnostics = new SchedulerDiagnostics();
        var nextRun = DateTime.UtcNow.AddMinutes(1);
        diagnostics.Started("job", "Import", DateTime.UtcNow, nextRun, 0);
        diagnostics.Completed("job", "Import", "Failed", nextRun,
            TimeSpan.FromMilliseconds(250), new InvalidOperationException("Import failed"));

        var job = Xunit.Assert.Single(diagnostics.GetStatus().Jobs);
        var execution = Xunit.Assert.Single(diagnostics.GetRecentEvents());
        Xunit.Assert.NotNull(job.LastStartedUtc);
        Xunit.Assert.NotNull(job.LastCompletedUtc);
        Xunit.Assert.Equal(TimeSpan.FromMilliseconds(250), job.LastDuration);
        Xunit.Assert.Equal("System.InvalidOperationException", job.ErrorType);
        Xunit.Assert.Equal($"0x{new InvalidOperationException().HResult:X8}", job.ErrorCode);
        Xunit.Assert.Equal(job.LastStartedUtc, execution.StartedUtc);
        Xunit.Assert.Equal(job.LastDuration, execution.Duration);
        Xunit.Assert.Equal(job.ErrorCode, execution.ErrorCode);
    }
}
