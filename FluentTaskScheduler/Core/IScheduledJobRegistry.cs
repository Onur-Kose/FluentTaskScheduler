namespace FluentTaskScheduler.Core
{
    public interface IScheduledJobRegistry
    {
        void AddJob(TimedJobConfig config);
        IReadOnlyList<TimedJobConfig> GetJobs();

        /// <summary>
        /// Returns a task that completes when the job list changes (e.g. a new job is added).
        /// Lets a scheduler sleeping until the next known NextRun wake up early instead of
        /// oversleeping past a newly registered job. Default implementation never completes
        /// on its own, preserving behavior for existing implementers of this interface.
        /// </summary>
        Task WaitForChangeAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
