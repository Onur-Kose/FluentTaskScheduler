namespace FluentTaskScheduler.Core;

public sealed class SchedulerOptions
{
    public int MaxConcurrency { get; set; } = 4;
    public TimeSpan? DefaultTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan InfrastructureRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan StoreRefreshInterval { get; set; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        if (MaxConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(MaxConcurrency));
        if (DefaultTimeout is { } timeout && (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(DefaultTimeout));
        if (InfrastructureRetryDelay < TimeSpan.FromMilliseconds(10) || StoreRefreshInterval < TimeSpan.FromMilliseconds(10))
            throw new ArgumentException("Store/recovery intervals must be at least 10ms.");
    }
}
