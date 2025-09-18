using FluentTaskScheduler.Core;
using FluentTaskScheduler.DSL;
using FluentTaskScheduler.Execution;
using FluentTaskScheduler.Sandbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton<IMyService, MyService>();
        services.AddSingleton<IScheduledJobRegistry, ScheduledJobRegistry>();
        services.AddHostedService<FlexibleSchedulerService>();
    })
    .Build();

//Test Place ==> 
new SchedulerBuilder<IMyService>(host.Services)
    .For(x => x.StepOneAsync())
    .ThenFor(x => x.StepTwoAsync())
    .Every(TimeSpan.FromSeconds(10))
    .Between("14:00", "14:36")
    .Do();


//<== Test Place

await host.RunAsync();
