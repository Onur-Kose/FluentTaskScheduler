namespace FluentTaskScheduler.Core;

/// <summary>Additional attempts after failure. Retrying a sequence restarts it from its first step.</summary>
public sealed record RetryPolicy
{
    public int MaxRetries { get; init; }
    public TimeSpan Delay { get; init; } = TimeSpan.FromSeconds(5);
    public double BackoffFactor { get; init; } = 2;
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(5);
    internal TimeSpan DelayFor(int attempt) => TimeSpan.FromMilliseconds(
        Math.Min(MaxDelay.TotalMilliseconds, Delay.TotalMilliseconds * Math.Pow(BackoffFactor, attempt - 1)));
}
