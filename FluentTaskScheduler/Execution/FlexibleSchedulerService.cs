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
            var runningTasks = new List<Task>();

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    runningTasks.RemoveAll(task => task.IsCompletedSuccessfully);
                    var now = DateTime.UtcNow;
                    foreach (var job in _registry.GetJobs())
                    {
                        if (stoppingToken.IsCancellationRequested)
                            break;

                        lock (job)
                        {
                            if (job.IsRunning || job.NextRun > now)
                                continue;

                            // A delayed poll must still honor calendar restrictions.
                            if (!JobSchedule.CanRunAt(job, now))
                            {
                                job.NextRun = JobSchedule.CalculateNextRun(job, now);
                                continue;
                            }

                            job.NextRun = JobSchedule.CalculateNextRun(job, now);
                            job.IsRunning = true;
                        }

                        // Retain tasks so shutdown can await their scope disposal.
                        runningTasks.Add(Task.Run(() => ExecuteJobAsync(job, stoppingToken)));
                    }

                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            finally
            {
                await Task.WhenAll(runningTasks);
                _logger.LogInformation("FlexibleSchedulerService stopped.");
            }
        }

        private async Task ExecuteJobAsync(TimedJobConfig job, CancellationToken token)
        {
            try
            {
                if (token.IsCancellationRequested)
                    return;

                await using var scope = _sp.CreateAsyncScope();
                await job.Func(scope.ServiceProvider);
                _logger.LogInformation("Executed job: {Name}", job.Name);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job failed: {Name}", job.Name);
            }
            finally
            {
                lock (job)
                {
                    job.IsRunning = false;
                }
            }
        }
    }
}

