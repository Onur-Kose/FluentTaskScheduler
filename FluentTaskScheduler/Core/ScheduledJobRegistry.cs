using System.Collections.Concurrent;

namespace FluentTaskScheduler.Core
{
    public class ScheduledJobRegistry : IScheduledJobRegistry
    {
        private readonly ConcurrentQueue<TimedJobConfig> _jobs = new();
        private volatile IReadOnlyList<TimedJobConfig>? _cachedJobs;
        private readonly object _cacheLock = new();
        private volatile TaskCompletionSource<bool> _changeSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void AddJob(TimedJobConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            TaskCompletionSource<bool> previousSignal;
            lock (_cacheLock)
            {
                _jobs.Enqueue(config);
                _cachedJobs = null;
                previousSignal = _changeSignal;
                _changeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            // Completed outside the lock so continuations can't re-enter and block on it.
            previousSignal.TrySetResult(true);
        }

        public IReadOnlyList<TimedJobConfig> GetJobs()
        {
            var cachedJobs = _cachedJobs;
            if (cachedJobs is not null)
                return cachedJobs;

            lock (_cacheLock)
            {
                _cachedJobs ??= Array.AsReadOnly(_jobs.ToArray());
                return _cachedJobs;
            }
        }

        public Task WaitForChangeAsync(CancellationToken cancellationToken) =>
            _changeSignal.Task.WaitAsync(cancellationToken);
    }
}
