using System.Collections.Concurrent;

namespace FluentTaskScheduler.Core
{
    public class ScheduledJobRegistry : IScheduledJobRegistry
    {
        private readonly ConcurrentQueue<TimedJobConfig> _jobs = new();
        private volatile IReadOnlyList<TimedJobConfig>? _cachedJobs;
        private readonly object _cacheLock = new();
        public void AddJob(TimedJobConfig config)
        {
            _jobs.Enqueue(config);
            lock (_cacheLock)
            {
                _cachedJobs = null;
            }
        }

        public IReadOnlyList<TimedJobConfig> GetJobs()
        {
            if (_cachedJobs is not null)
                return _cachedJobs;

            lock (_cacheLock)
            {
                _cachedJobs ??= [.. _jobs];
                return _cachedJobs;
            }
        }
    }
}
