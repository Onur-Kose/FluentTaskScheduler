using FluentTaskScheduler.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentTaskScheduler.Execution
{
    public class FlexibleSchedulerService : BackgroundService
    {
        // Task.Delay's maximum supported duration; longer deadlines use timer chunks.
        private static readonly TimeSpan MaxTimerDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

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
                    using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    try
                    {
                        // Subscribe before reading the snapshot so concurrent additions
                        // are either in the snapshot or complete this wait.
                        var changeTask = _registry.WaitForChangeAsync(waitCancellation.Token);
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
                                // Completion wakes the scheduler; a running job needs no timer.
                                if (job.IsRunning)
                                    continue;

                                if (job.NextRun > now)
                                {
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
                                    // Retain tasks so shutdown can await their scope disposal.
                                    runningTasks.Add(Task.Run(() => ExecuteJobAsync(job, stoppingToken)));
                                    continue;
                                }
                            }

                            if (nextWakeUp is null || jobNextRun < nextWakeUp)
                                nextWakeUp = jobNextRun;
                        }

                        var wakeUpTasks = new List<Task>(runningTasks.Count + 2) { changeTask };
                        wakeUpTasks.AddRange(runningTasks);
                        if (nextWakeUp.HasValue)
                            wakeUpTasks.Add(WaitUntilAsync(nextWakeUp.Value, waitCancellation.Token));

                        var completedTask = await Task.WhenAny(wakeUpTasks);
                        await completedTask;
                    }
                    finally
                    {
                        // WhenAny does not cancel the other task. Release it each iteration,
                        // including when scanning jobs fails or the service is stopping.
                        await waitCancellation.CancelAsync();
                    }
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

        private static async Task WaitUntilAsync(DateTime dueAt, CancellationToken token)
        {
            var remaining = dueAt - DateTime.UtcNow;
            while (remaining > MaxTimerDelay)
            {
                await Task.Delay(MaxTimerDelay, token);
                remaining = dueAt - DateTime.UtcNow;
            }

            await Task.Delay(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, token);
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

