namespace FluentTaskScheduler.Core;

/// <summary>Scoped execution metadata. ExecutionId stays stable through retries and crash recovery.</summary>
public sealed class JobExecutionContext
{
    public string JobKey { get; internal set; } = "";
    public string ExecutionId { get; internal set; } = "";
    public int Attempt { get; internal set; }
    public DateTime ScheduledForUtc { get; internal set; }
    public CancellationToken CancellationToken { get; internal set; }
}
