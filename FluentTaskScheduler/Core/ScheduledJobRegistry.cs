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
            ArgumentNullException.ThrowIfNull(config);
            lock (_cacheLock)
            {
                _jobs.Enqueue(config);
                _cachedJobs = null;
            }
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
    }
}
