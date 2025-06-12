namespace FluentTaskScheduler.Core
{
    public class TimedJobConfig
    {
        public string Name { get; set; } = string.Empty;
        public Func<IServiceProvider, Task> Action { get; set; } = default!;
        public TimeSpan? DailyAt { get; set; }
        public TimeSpan? RepeatEvery { get; set; }
        public TimeSpan? IntervalStart { get; set; }
        public TimeSpan? IntervalEnd { get; set; }
        public DateTime NextRun { get; set; }
    }
}
