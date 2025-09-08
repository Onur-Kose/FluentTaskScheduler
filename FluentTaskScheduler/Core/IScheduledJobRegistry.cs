namespace FluentTaskScheduler.Core
{
    public interface IScheduledJobRegistry
    {
        void AddJob(TimedJobConfig config);
        IReadOnlyList<TimedJobConfig> GetJobs();

    }
}
