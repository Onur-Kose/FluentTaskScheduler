using FluentTaskScheduler.Core;
using FluentTaskScheduler.Extensions;
using FluentTaskScheduler.UI;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddFluentTaskScheduler();

var app = builder.Build();
var jobs = app.Services.GetRequiredService<IScheduledJobRegistry>();

jobs.AddJob(new TimedJobConfig
{
    Key = "demo.heartbeat",
    Name = "Sistem kontrolü",
    RepeatEvery = TimeSpan.FromSeconds(10),
    NextRun = DateTime.UtcNow.AddSeconds(2),
    Func = async _ => await Task.Delay(600)
});

jobs.AddJob(new TimedJobConfig
{
    Key = "demo.failure",
    Name = "Hata örneği",
    RepeatEvery = TimeSpan.FromSeconds(17),
    NextRun = DateTime.UtcNow.AddSeconds(5),
    Func = _ => throw new InvalidOperationException("Demo hata kaydı: örnek görev tamamlanamadı.")
});

jobs.AddJob(new TimedJobConfig
{
    Key = "demo.paused",
    Name = "Duraklatılmış görev",
    RepeatEvery = TimeSpan.FromMinutes(1),
    NextRun = DateTime.UtcNow.AddMinutes(1),
    IsPaused = true,
    Func = _ => Task.CompletedTask
});

// Demo explicitly enables the dashboard outside Development so it works with dotnet run.
app.MapFluentTaskSchedulerDashboard(onlyInDevelopment: false);
app.MapGet("/", () => Results.Redirect("/scheduler"));
app.Run();
