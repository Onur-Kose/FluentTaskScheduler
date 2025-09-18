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
                        if (job.IsRunning)
                        {
                            _logger.LogWarning("Job is already running: {Name}", job.Name);
                            continue;
                        }

                        job.IsRunning = true;

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
                            finally
                            {
                                job.IsRunning = false;
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
            DateTime nextRun = now;

            if (job.DailyAtTimes is { Count: > 0 })
            {
                var today = now.Date;

                var upcomingToday = job.DailyAtTimes
                    .Select(t => today + t)
                    .Where(t => t > now)
                    .OrderBy(t => t)
                    .FirstOrDefault();

                if (upcomingToday != default)
                {
                    nextRun = upcomingToday;
                }
                else
                {

                    var tomorrow = now.Date.AddDays(1);
                    nextRun = tomorrow + job.DailyAtTimes.Min();
                }
            }

            else if (job.IntervalStart.HasValue && job.IntervalEnd.HasValue && job.RepeatEvery.HasValue)
            {
                var timeOfDay = now.TimeOfDay;

                if (timeOfDay < job.IntervalStart.Value)
                    nextRun = now.Date + job.IntervalStart.Value;

                else if (timeOfDay >= job.IntervalEnd.Value)
                    nextRun = now.Date + TimeSpan.FromDays(1) + job.IntervalStart.Value;

                else
                {
                    var tentativeNext = now + job.RepeatEvery.Value;
                    if (tentativeNext.TimeOfDay >= job.IntervalEnd.Value)
                    {
                        nextRun = now.Date.AddDays(1) + job.IntervalStart.Value;
                    }
                    else
                    {
                        nextRun = tentativeNext;
                    }
                }
            }

            else if (job.RepeatEvery.HasValue)
            {
                nextRun = now + job.RepeatEvery.Value;
            }
            else
            {
                nextRun = now.AddDays(1);
            }

            while (job.ExcludedDays?.Contains(nextRun.DayOfWeek) == true)
            {
                nextRun = nextRun.AddDays(1);
            }
            return nextRun;
        }
    }
}

