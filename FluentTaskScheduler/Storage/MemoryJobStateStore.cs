using System.Collections.Concurrent;

namespace FluentTaskScheduler.Storage;

/// <summary>Coordinates schedulers sharing this store instance. State does not survive process exit.</summary>
public sealed class MemoryJobStateStore : IJobStateStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    public bool RequiresStableKeys => false;
    public ValueTask<IJobStateLease?> TryAcquireAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = _entries.GetOrAdd(key, _ => new Entry());
        return ValueTask.FromResult<IJobStateLease?>(entry.Gate.Wait(0) ? new Lease(entry) : null);
    }
    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public JobState? State;
    }
    private sealed class Lease(Entry entry) : IJobStateLease
    {
        private bool _disposed;
        public JobState? State => entry.State;
        public ValueTask SaveAsync(JobState state, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            entry.State = state;
            return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            if (!_disposed) { _disposed = true; entry.Gate.Release(); }
            return ValueTask.CompletedTask;
        }
    }
}
