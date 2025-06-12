using FluentTaskScheduler.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentTaskScheduler.Execution
{
    public class FlexibleSchedulerService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<FlexibleSchedulerService> _logger;
        private readonly IScheduledJobRegistry _registry;

        public FlexibleSchedulerService(
            IServiceProvider serviceProvider,
            ILogger<FlexibleSchedulerService> logger)
        {
            _sp = serviceProvider;
            _logger = logger;
            _registry = _sp.GetRequiredService<IScheduledJobRegistry>();
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("FlexibleSchedulerService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                foreach (var job in _registry.GetJobs())
                {
                    if (job.NextRun == default || job.NextRun <= now)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await job.Action(_sp);
                                _logger.LogInformation("Executed job: {Name}", job.Name);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Job failed: {Name}", job.Name);
                            }
                        }, stoppingToken);

                        job.NextRun = CalculateNextRun(now, job);
                    }
                }

                await Task.Delay(1000, stoppingToken);
            }

            _logger.LogInformation("FlexibleSchedulerService stopped.");
        }

        private static DateTime CalculateNextRun(DateTime now, TimedJobConfig job)
        {
            if (job.DailyAt.HasValue)
            {
                var todayTime = now.Date + job.DailyAt.Value;
                return todayTime > now ? todayTime : todayTime.AddDays(1);
            }

            if (job.IntervalStart.HasValue && job.IntervalEnd.HasValue && job.RepeatEvery.HasValue)
            {
                var timeOfDay = now.TimeOfDay;
                if (timeOfDay < job.IntervalStart.Value)
                    return now.Date + job.IntervalStart.Value;

                if (timeOfDay >= job.IntervalEnd.Value)
                    return now.Date + TimeSpan.FromDays(1) + job.IntervalStart.Value;

                return now + job.RepeatEvery.Value;
            }

            if (job.RepeatEvery.HasValue)
            {
                return now + job.RepeatEvery.Value;
            }

            return now.AddDays(1); // fallback
        }
    }
}

