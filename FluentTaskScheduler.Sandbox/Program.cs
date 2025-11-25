using FluentTaskScheduler.Extensions;
using FluentTaskScheduler.Sandbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        // 1. Register application services
        services.AddSingleton<IMyService, MyService>();
        
        // 2. Register FluentTaskScheduler infrastructure
        services.AddFluentTaskScheduler();
        
        // 3. Register job configuration class
        services.AddJobConfiguration<MyScheduledJobs>();
    })
    .Build();

// All jobs are now configured automatically via MyScheduledJobs class!
// No need for manual SchedulerBuilder calls here.

await host.RunAsync();
