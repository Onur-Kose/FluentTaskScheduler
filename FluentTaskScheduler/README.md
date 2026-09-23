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
dotnet add package FluentTaskScheduler --version 0.2.0
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

* The in-memory executor wakes for due times, completions, or registry changes. Persistent storage additionally refreshes shared state at a configurable interval. Every registry must implement `WaitForChangeAsync` for additions/live edits and honor cancellation.
* Jobs run as tracked tasks, each with its own dependency injection scope.
* Daily times, time windows, and excluded weekdays use UTC.
* Time windows include the start and exclude the end; overnight windows are not supported.
* An interval that lands on an excluded day moves to the next allowed day, at midnight or the window start.
* Each job keeps track of its own next execution time (NextRun).
* Actual execution ownership prevents overlap; file storage additionally coordinates instances sharing the same directory and key.
* If both .Every(...) and .DailyAt(...) are used together, an exception is thrown.
* A job must specify either .Every(...) or .DailyAt(...). Intervals must be at least one second.
* .Do() completes a definition; call .For(...) again before configuring another job with the same builder.
* Excluding all seven days also throws an exception to prevent silent never-runs.

---

## ADVANCED TOPICS

* Dependency Injection: Jobs can use any registered service type — transient, scoped, or singleton.
* Error Handling: Job failures are isolated and logged. Optional retries use bounded backoff; infrastructure failures are retried independently.
* Graceful Shutdown: The scheduler stops dispatching, cancels token-aware jobs, checks cancellation between sequence steps, and awaits scope disposal subject to the host shutdown timeout. Legacy tokenless methods remain supported.
* AOT / Native Compilation: The library targets `IsAotCompatible` and ships with no AOT/trim analyzer warnings. It uses `Expression.Compile()` internally; under Native AOT (no dynamic code generation) this transparently falls back to the built-in expression interpreter instead of failing, so jobs still run correctly — compiled delegates may just be marginally slower to invoke than fully JIT'd ones.

---

## PRODUCTION CONFIGURATION (0.2.0)

The existing fluent methods and tokenless jobs remain supported. Production controls are additive:

```csharp
services.AddFluentTaskScheduler(options =>
{
    options.MaxConcurrency = 4;
    options.DefaultTimeout = TimeSpan.FromMinutes(5); // null disables the default timeout
    options.InfrastructureRetryDelay = TimeSpan.FromSeconds(5);
    options.StoreRefreshInterval = TimeSpan.FromSeconds(5);
});
services.AddFileJobStateStore("/var/lib/my-app/scheduler"); // Windows: a dedicated writable directory
services.AddScoped<IReportService, ReportService>();
services.AddSchedulerFor<IReportService>();

// After building the host, before RunAsync:
var builder = host.Services.GetRequiredService<SchedulerBuilder<IReportService>>();
builder.For((service, token) => service.GenerateAsync(token))
    .WithKey("reports.daily") // identical on every instance and after every restart
    .DailyAt("08:00")
    .WithTimeout(TimeSpan.FromMinutes(2))
    .WithRetry(new RetryPolicy
    {
        MaxRetries = 3,
        Delay = TimeSpan.FromSeconds(5),
        BackoffFactor = 2,
        MaxDelay = TimeSpan.FromMinutes(1)
    })
    .Do();
```

Import `FluentTaskScheduler.Core` for `RetryPolicy`. A token-aware job uses a method such as
`Task GenerateAsync(CancellationToken cancellationToken)`. Token-aware and legacy steps can be mixed
in a `ThenFor(...)` sequence. Each attempt resolves a fresh DI scope and service instance.

### Live external intervention

Property setters, collection mutations, and atomic updates notify the registry immediately.
Applications can continue retrieving and modifying the live definitions:

```csharp
var registry = host.Services.GetRequiredService<IScheduledJobRegistry>();
var job = registry.GetJobs().Single(job => job.Key == "reports.daily");
job.Changed += (_, _) => Console.WriteLine("Job definition changed.");
job.NextRun = DateTime.UtcNow; // wake a sleeping scheduler
job.IsPaused = true;
job.DailyAtTimes.Add(TimeSpan.FromHours(18)); // Add, Remove, Clear and index writes are observable
job.IsPaused = false;

job.Update(draft =>
{
    draft.DailyAtTimes.Clear();
    draft.RepeatEvery = TimeSpan.FromMinutes(10);
    draft.IntervalStart = TimeSpan.FromHours(8);
    draft.IntervalEnd = TimeSpan.FromHours(18);
    draft.ExcludedDays = [DayOfWeek.Saturday, DayOfWeek.Sunday];
});
```

Registered definitions reject invalid changes and preserve their previous valid state. Use `Update`
when intermediate assignments would be invalid. Schedule edits calculate a new next execution time;
an explicit `NextRun` change within the same update takes precedence. Changes during an execution affect
subsequent runs; the active invocation retains its delegate/scope. A live change also wins over the
old invocation's completion/retry schedule. Notification handlers should return promptly; their exceptions
are isolated so they cannot suppress scheduler notifications.

`DailyAtTimes` and `ExcludedDays` are now `JobCollection<T>` instead of concrete `List<T>`/arrays.
They support collection expressions, assignment from lists/arrays, indexed access, Add/Remove/AddRange,
Sort/Reverse, and snapshot enumeration. Assigned input is copied; mutate the collection obtained from
the job to change its live definition. Use `IList<T>`, `IReadOnlyList<T>`, or `ToArray()` where an
explicit list/array type was previously required. Consumers must recompile for version 0.2.0.

`IsRunning = true` still manually suspends dispatch. Setting it to false resumes that manual suspension,
but its getter remains true while user code or scope disposal is actually active. This prevents an
external flag write from accidentally allowing overlap. `IsPaused` is the explicit pause/resume control.
A registered `Key` is its permanent identity; use a new definition for a different identity.

### Cancellation, timeouts, concurrency, and retries

- At most `MaxConcurrency` invocations are in flight per scheduler. Pending jobs remain in the registry,
  ordered by due time; the executor does not create an unbounded collection of waiting tasks.
- Shutdown and timeout tokens reach token-aware jobs, and are checked before every sequence step.
  Legacy methods still run, but cannot be forcibly interrupted. A timed-out method that ignores its token
  retains its execution slot, scope, and storage lock until it actually ends. Its status shows
  `TimeoutCancellationRequested`. The host shutdown timeout still bounds how long the host waits.
- Retry is opt-in (`MaxRetries = 0` by default). Retry delays use capped exponential backoff and retain
  calendar restrictions. Retrying a sequence starts from its first step, so completed steps may repeat.
- Inject the scoped `JobExecutionContext` to obtain `JobKey`, `ExecutionId`, `Attempt`,
  `ScheduledForUtc`, and `CancellationToken`. The execution ID survives retries and crash recovery.
  Use that identity in your own transaction/unique constraint to prevent duplicated business effects.

### File persistence and multiple processes

`AddFileJobStateStore(directory)` enables persistence and exclusive execution between processes.
All instances must use the same persistent directory and stable job keys. Each key gets a separate
OS-managed lock file; state is written to a temporary file, flushed, and atomically replaced.
The lock covers user code, scope disposal, and completion checkpoints. A process crash releases its
lock automatically; do not delete lock files while processes are running.

Every host re-registers the executable handlers with the same keys. Delegates/services are not serialized.
Stored schedules and next-run times take precedence over startup defaults after restart. Runtime schedule,
pause, and next-run changes are saved and become visible to other instances within `StoreRefreshInterval`
when the job lock is available. Changes made during a running invocation are checkpointed when it ends;
an abrupt crash before that checkpoint can lose those uncommitted edits. Concurrent edits on different
instances use the last successfully committed update.

Before user code starts, the occurrence is checkpointed. Interrupted work is retried with the same
execution identity. Successful completion advances its schedule. Missed occurrences are coalesced into
one catch-up execution, respecting allowed days/windows; the scheduler does not replay every missed tick.
A crash after an external side effect but before its completion checkpoint can repeat that effect.
This is at-least-once recovery, not an exactly-once transaction across your application and filesystem.

Local filesystems and shared disks must support exclusive file opens and atomic replacement.
Validate those semantics on the actual shared mount; separate container-local directories do not coordinate.
Keep the directory on persistent storage with application-appropriate permissions. Corrupt/unreadable
state marks that job unavailable and is never silently reset; other jobs continue.

Without this opt-in, the default `MemoryJobStateStore` retains the lightweight in-process behavior.
A custom `IJobStateStore` can provide another storage backend using the same exclusive-lease contract.

### Monitoring

Resolve `SchedulerDiagnostics` (namespace `FluentTaskScheduler.Diagnostics`) and call `GetStatus()`
from an application health endpoint. The result includes running/healthy state and per-job next run,
last success, last start, error, outcome, and consecutive failures. Subscribe to `JobCompleted`
for completion notifications. Observer exceptions cannot stop execution.

The `FluentTaskScheduler` meter exposes execution, failure, retry, timeout, active-job, duration, and
lateness instruments. An ActivitySource with the same name supplies traces with job identity and attempt.
Connect them to your application's OpenTelemetry/metrics exporter. The library does not start an HTTP
server or send telemetry externally.

Registry infrastructure failures are logged and retried with backoff. Invalid jobs from a custom registry
are isolated. Custom registries must signal both additions and live definition changes, and honor
cancellation in `WaitForChangeAsync`; delegating to `ScheduledJobRegistry` provides these behaviors.

### Validation and release

The CI workflow builds and tests on Windows/Linux, audits direct/transitive dependencies, verifies the
NuGet package, and publishes/runs the Native AOT smoke application. Cross-process tests exercise lock
contention, completed-work deduplication, and lock release after a process is killed.

```sh
dotnet test FluentRunly.sln -c Release
dotnet pack FluentTaskScheduler/FluentTaskScheduler.csproj -c Release -o artifacts/packages
dotnet publish FluentTaskScheduler.AotSmoke/FluentTaskScheduler.AotSmoke.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/aot-smoke
```

Use the appropriate RID for the target machine. Package creation does not publish a package or deploy
an application.

---

## ROADMAP

* Cron expression support
* Additional storage providers (SQLite, Redis)
* Web dashboard for job monitoring
* Additional scheduling policies

---

## LICENSE

MIT License © Onur Köse
[https://github.com/Onur-Kose](https://github.com/Onur-Kose)
