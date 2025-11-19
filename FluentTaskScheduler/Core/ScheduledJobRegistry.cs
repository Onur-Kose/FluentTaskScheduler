using System.Collections.Concurrent;

namespace FluentTaskScheduler.Core
{
    public class ScheduledJobRegistry : IScheduledJobRegistry
    {
        private readonly ConcurrentQueue<TimedJobConfig> _jobs = new();

        public void AddJob(TimedJobConfig config)
        {
            _jobs.Enqueue(config);
        }

        public IReadOnlyList<TimedJobConfig> GetJobs()
        {
            return [.. _jobs];
        }
    }
}
