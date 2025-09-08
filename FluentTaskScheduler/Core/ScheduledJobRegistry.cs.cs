using System.Collections.Concurrent;

namespace FluentRunly.Core
{
    public class ScheduledJobRegistry : IScheduledJobRegistry
    {
        private readonly ConcurrentBag<TimedJobConfig> _jobs = [];

        public void AddJob(TimedJobConfig config)
        {
            _jobs.Add(config);
        }

        public IReadOnlyList<TimedJobConfig> GetJobs()
        {
            return [.. _jobs];
        }
    }
}
