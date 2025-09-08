namespace FluentRunly.Core
{
    public class TimedJobConfig
    {
        public string Name { get; set; } = string.Empty;
        public Func<IServiceProvider, Task> Action { get; set; } = default!;
        public List<TimeSpan> DailyAtTimes { get; set; } = new();
        public TimeSpan? RepeatEvery { get; set; }
        public TimeSpan? IntervalStart { get; set; }
        public TimeSpan? IntervalEnd { get; set; }
        public DateTime NextRun { get; set; }
        public DayOfWeek[]? ExcludedDays { get; set; }
        public bool IsRunning { get; set; } = false;


    }
}
