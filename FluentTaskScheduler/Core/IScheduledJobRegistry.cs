namespace FluentTaskScheduler.Core
{
    public interface IScheduledJobRegistry
    {
        void AddJob(TimedJobConfig config);
        IReadOnlyList<TimedJobConfig> GetJobs();

        /// <summary>
        /// Returns a task that completes when the job list changes (e.g. a new job is added).
        /// Lets a scheduler sleeping until the next known NextRun wake up early instead of
        /// oversleeping past a newly registered job. Implementations must signal additions
        /// and honor cancellation to release abandoned waits. Subscribe before GetJobs()
        /// so additions between taking the snapshot and waiting cannot be missed.
        /// </summary>
        Task WaitForChangeAsync(CancellationToken cancellationToken);
    }
}
