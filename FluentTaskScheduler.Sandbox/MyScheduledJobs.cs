using FluentTaskScheduler.Core;

namespace FluentTaskScheduler.Sandbox
{
    /// <summary>
    /// Test job configuration class.
    /// All scheduled jobs are defined here in a clean, organized way.
    /// </summary>
    public class MyScheduledJobs : FlexibleSchedulerTaskConfig
    {
        protected override void ConfigureJobs()
        {
            // Job 1: Sequential task execution every 10 seconds
            For<IMyService>(x => x.StepOneAsync())
                .ThenFor(x => x.StepTwoAsync())
                .Every(TimeSpan.FromSeconds(10))
                .Do();

            // Job 2: Single task every 15 seconds (with custom name)
            For<IMyService>(x => x.StepOneAsync(), "CustomNamedJob")
                .Every(TimeSpan.FromSeconds(15))
                .Do();

            // Job 3: Daily task at specific time
            For<IMyService>(x => x.StepTwoAsync())
                .DailyAt("09:00")
                .Do();
        }
    }
}
