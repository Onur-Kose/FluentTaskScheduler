using FluentTaskScheduler.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentTaskScheduler.Execution
{
    public class FlexibleSchedulerService : BackgroundService
    {
        // Floor: prevents a busy-loop when a job overruns its own interval while
        // still IsRunning (its NextRun is in the past, but it can't be re-dispatched yet).
        private static readonly TimeSpan MinWait = TimeSpan.FromMilliseconds(200);

        // Ceiling: used when no jobs are registered, and as a defensive periodic
        // re-check even though job changes are also signaled explicitly.
        private static readonly TimeSpan MaxIdleWait = TimeSpan.FromMinutes(5);

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
                    runningTasks.RemoveAll(task => task.IsCompleted);
                    var now = DateTime.UtcNow;
                    DateTime? nextWakeUp = null;

                    foreach (var job in _registry.GetJobs())
                    {
                        if (stoppingToken.IsCancellationRequested)
                            break;

                        DateTime jobNextRun;
                        lock (job)
                        {
                            if (job.IsRunning || job.NextRun > now)
                            {
                                // Include running jobs too: their NextRun was already advanced
                                // to the next future occurrence before they started, so skipping
                                // them here would cause the scheduler to oversleep past it.
                                jobNextRun = job.NextRun;
                            }
                            else if (!JobSchedule.CanRunAt(job, now))
                            {
                                // A delayed wake-up must still honor calendar restrictions.
                                jobNextRun = job.NextRun = JobSchedule.CalculateNextRun(job, now);
                            }
                            else
                            {
                                job.NextRun = JobSchedule.CalculateNextRun(job, now);
                                job.IsRunning = true;
                                jobNextRun = job.NextRun;

                                // Retain tasks so shutdown can await their scope disposal.
                                runningTasks.Add(Task.Run(() => ExecuteJobAsync(job, stoppingToken)));
                            }
                        }

                        if (nextWakeUp is null || jobNextRun < nextWakeUp)
                            nextWakeUp = jobNextRun;
                    }

                    var delay = nextWakeUp.HasValue ? nextWakeUp.Value - DateTime.UtcNow : MaxIdleWait;
                    if (delay < MinWait)
                        delay = MinWait;
                    else if (delay > MaxIdleWait)
                        delay = MaxIdleWait;

                    var delayTask = Task.Delay(delay, stoppingToken);
                    var changeTask = _registry.WaitForChangeAsync(stoppingToken);
                    await Task.WhenAny(delayTask, changeTask);
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

