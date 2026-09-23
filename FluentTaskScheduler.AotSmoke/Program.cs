using FluentTaskScheduler.Core;
using FluentTaskScheduler.Diagnostics;
using FluentTaskScheduler.DSL;
using FluentTaskScheduler.Execution;
using FluentTaskScheduler.Extensions;
using FluentTaskScheduler.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

var mode = args.Length > 0 ? args[0] : "smoke";
var directory = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "FluentTaskScheduler-Smoke-" + Guid.NewGuid().ToString("N"));
if (mode == "hold")
{
    var store = new FileJobStateStore(directory);
    await using var lease = await store.TryAcquireAsync("shared", default)
        ?? throw new InvalidOperationException("Could not acquire test lease.");
    Console.WriteLine("LOCKED");
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return;
}

var services = new ServiceCollection();
services.AddFluentTaskScheduler(options =>
{
    options.InfrastructureRetryDelay = TimeSpan.FromMilliseconds(50);
    options.StoreRefreshInterval = TimeSpan.FromMilliseconds(50);
});
services.AddFileJobStateStore(directory);
services.AddScoped<SmokeJob>();
services.AddSingleton(new SmokeOutput(directory));
await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
var builder = new SchedulerBuilder<SmokeJob>(provider);
builder.For((job, token) => job.Run(token)).ThenFor(job => job.Finish())
    .WithKey("shared").Every(TimeSpan.FromDays(1)).WithTimeout(TimeSpan.FromSeconds(5)).Do();
var config = provider.GetRequiredService<IScheduledJobRegistry>().GetJobs()[0];
config.NextRun = DateTime.UtcNow.AddSeconds(-1);
using var scheduler = new FlexibleSchedulerService(provider, NullLogger<FlexibleSchedulerService>.Instance);
await scheduler.StartAsync(default);
try
{
    if (mode == "worker")
    {
        await Task.Delay(1500);
    }
    else
    {
        await provider.GetRequiredService<SmokeOutput>().Finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        while (config.IsRunning) await Task.Delay(10);
        var status = provider.GetRequiredService<SchedulerDiagnostics>().GetStatus();
        if (!status.IsHealthy) throw new InvalidOperationException("Smoke job did not complete successfully.");
    }
}
finally { await scheduler.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10)); }
Console.WriteLine("OK");

public sealed class SmokeOutput(string directory)
{
    public string Directory { get; } = directory;
    public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
public sealed class SmokeJob(SmokeOutput output, JobExecutionContext context)
{
    public async Task Run(CancellationToken token)
    {
        if (string.IsNullOrEmpty(context.ExecutionId) || !token.CanBeCanceled)
            throw new InvalidOperationException("Missing execution context.");
        await Task.Delay(150, token);
        await File.AppendAllTextAsync(Path.Combine(output.Directory, "executions.txt"), context.ExecutionId + Environment.NewLine, token);
    }
    public Task Finish() { output.Finished.TrySetResult(); return Task.CompletedTask; }
}
