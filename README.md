# FluentTaskScheduler

FluentTaskScheduler is a lightweight and fluent-style job scheduling library for .NET. It allows you to define background jobs in a readable, chainable way and execute them inside your ASP.NET Core or Console host.

---

## MAIN FEATURES

* Fluent, chainable API for defining jobs
* Works with Microsoft Dependency Injection (IServiceCollection)
* Runs as a background hosted service
* Supports interval-based or specific-time execution
* Supports excluded days and time ranges
* Exception-safe execution and logging
* Uses Microsoft.Extensions hosting and dependency injection

---

## INSTALLATION

Install from NuGet:

```
dotnet add package FluentTaskScheduler --version 0.1.0
```

---

## HOW TO USE – STEP BY STEP GUIDE

1. CREATE YOUR SERVICE

---

Define an interface and a class containing the method that you want the scheduler to run.


```c#
public interface IMyService
{
    Task DoWorkAsync();
}

public class MyService : IMyService
{
    public async Task DoWorkAsync()
    {
        Console.WriteLine($"[{DateTime.Now:T}] Job executed.");
        await Task.CompletedTask;
    }
}
```

## 2. REGISTER THE SCHEDULER AND YOUR SERVICE

In Program.cs (targeting .NET 10):


```c#
using FluentTaskScheduler.DSL;
using FluentTaskScheduler.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        // Add the main scheduler service
        services.AddFluentTaskScheduler();

        // Register the scheduler for your interface
        services.AddSchedulerFor<IMyService>();

        // Register your actual service implementation
        services.AddTransient<IMyService, MyService>();
    })
    .Build();
```

Explanation:

* AddFluentTaskScheduler() registers the background runner (FlexibleSchedulerService).
* AddSchedulerFor<T>() prepares a builder that can create jobs for your specific service.
* Your service (IMyService) is resolved from the dependency container at runtime.

3. CREATE AND REGISTER YOUR JOB

---

After the host is built, obtain the scheduler builder and define your job.


```c#
var scheduler = host.Services.GetRequiredService<SchedulerBuilder<IMyService>>();

scheduler
    .For(x => x.DoWorkAsync())        // which method to execute
    .Every(TimeSpan.FromSeconds(30))  // how often to run
    .Do();                            // register it
```

Explanation:

* For() specifies the method to call.
* Every() defines the repetition interval.
* Do() finalizes and registers the job inside the registry.

4. RUN YOUR HOST

---

Finally start the host. The background service will begin running your jobs automatically.


```c#
Console.WriteLine("Scheduler started. Press Ctrl+C to stop.");
await host.RunAsync();
```

When the program runs, you will see console messages every 30 seconds:

```c#
[10:15:00] Job executed.
[10:15:30] Job executed.
```

---

## EXAMPLES OF DIFFERENT SCHEDULE TYPES

Run every 5 minutes:

```c#
scheduler.For(x => x.DoWorkAsync())
         .Every(TimeSpan.FromMinutes(5))
         .Do();
```

Run daily at 08:00 and 18:00:

```c#
scheduler.For(x => x.DoWorkAsync())
         .DailyAt("08:00", "18:00")
         .Do();
```

Run every 10 minutes only between 08:00 and 18:00:

```c#
scheduler.For(x => x.DoWorkAsync())
         .Every(TimeSpan.FromMinutes(10))
         .Between(TimeSpan.FromHours(8), TimeSpan.FromHours(18))
         .Do();
```

Skip weekends:

```c#
scheduler.For(x => x.DoWorkAsync())
         .Every(TimeSpan.FromHours(1))
         .NotRunThisDays(DayOfWeek.Saturday, DayOfWeek.Sunday)
         .Do();
```

---

## API SUMMARY

| Method                                | Description                          |
| ------------------------------------- | ------------------------------------ |
| For(Expression<Func<T, Task>>)        | Specify the method to execute        |
| Every(TimeSpan interval)              | Repeats the job every given interval |
| DailyAt(params string[] times)        | Runs at specific daily times         |
| Between(TimeSpan start, TimeSpan end) | Restrict job to a time window        |
| NotRunThisDays(params DayOfWeek[])    | Exclude specific days                |
| Do()                                  | Registers the job                    |

---

## TECHNICAL NOTES

* The background executor (FlexibleSchedulerService) sleeps until the next job's due time instead of polling on a fixed interval, waking early whenever a job is added. This scales to large numbers of jobs without extra CPU overhead.
* Jobs run as tracked tasks, each with its own dependency injection scope.
* Daily times, time windows, and excluded weekdays use UTC.
* Time windows include the start and exclude the end; overnight windows are not supported.
* An interval that lands on an excluded day moves to the next allowed day, at midnight or the window start.
* Each job keeps track of its own next execution time (NextRun).
* Only one instance of each job runs at a time (no overlapping).
* If both .Every(...) and .DailyAt(...) are used together, an exception is thrown.
* A job must specify either .Every(...) or .DailyAt(...). Intervals must be at least one second.
* .Do() completes a definition; call .For(...) again before configuring another job with the same builder.
* Excluding all seven days also throws an exception to prevent silent never-runs.

---

## ADVANCED TOPICS

* Dependency Injection: Jobs can use any registered service type — transient, scoped, or singleton.
* Error Handling: Exceptions during execution are caught and logged via ILogger<FlexibleSchedulerService>.
* Graceful Shutdown: The scheduler stops dispatching new jobs and waits for active jobs, subject to the host shutdown timeout. Job methods do not receive a cancellation token automatically.
* AOT / Native Compilation: The library targets `IsAotCompatible` and ships with no AOT/trim analyzer warnings. It uses `Expression.Compile()` internally; under Native AOT (no dynamic code generation) this transparently falls back to the built-in expression interpreter instead of failing, so jobs still run correctly — compiled delegates may just be marginally slower to invoke than fully JIT'd ones.

---

## ROADMAP

* Cron expression support
* Persistent job storage (SQLite, Redis)
* Web dashboard for job monitoring
* Parallel and concurrency control

---

## LICENSE

MIT License © Onur Köse
[https://github.com/Onur-Kose](https://github.com/Onur-Kose)
