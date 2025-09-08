namespace FluentRunly.Core
{
    public interface IScheduledJobRegistry
    {
        void AddJob(TimedJobConfig config);
        IReadOnlyList<TimedJobConfig> GetJobs();

    }
}
