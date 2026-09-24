# FluentTaskScheduler.UI

Optional, read-only dashboard for ASP.NET Core applications running `FluentTaskScheduler`. The package contains its HTML, CSS, and JavaScript; there is no frontend build, static-file configuration, or separate server.

**Beta:** `0.1.0-beta.1`. The dashboard API may change before the first stable release.

## Requirements

- .NET 10 ASP.NET Core application.
- `AddFluentTaskScheduler()` in the application's service registration.
- The dashboard reads the scheduler in the **same process**. A separate worker process cannot be monitored by this package yet.

## Install

After publication to NuGet:

```powershell
dotnet add package FluentTaskScheduler.UI --version 0.1.0-beta.1
```

NuGet installs stable `FluentTaskScheduler` 0.2.1 as a dependency. If your application already references the main package, it is resolved once. Keep the main package as a direct reference when it must remain installed after removing UI:

```powershell
dotnet add package FluentTaskScheduler --version 0.2.1
dotnet add package FluentTaskScheduler.UI --version 0.1.0-beta.1
```

To install packages built from this repository before publication, pack both projects into `artifacts/packages` and use that folder as a NuGet source.

## Use

```csharp
using FluentTaskScheduler.Extensions;
using FluentTaskScheduler.UI;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFluentTaskScheduler();
// Register your application services and jobs as usual.

var app = builder.Build();
app.MapFluentTaskSchedulerDashboard();
app.Run();
```

The single `MapFluentTaskSchedulerDashboard()` call maps both `/scheduler` and `/scheduler/api/status`. No `AddFluentTaskSchedulerUI()`, `UseStaticFiles()`, or asset registration is needed. **By default, the routes exist only in the Development environment.** In Production and other environments, the call maps nothing.

Change the path with `app.MapFluentTaskSchedulerDashboard("/jobs")` (the JSON endpoint becomes `/jobs/api/status`).

## Production access

If you want the dashboard in Production, explicitly enable it and require authorization for both routes:

```csharp
app.MapGroup("/internal")
   .RequireAuthorization()
   .MapFluentTaskSchedulerDashboard(onlyInDevelopment: false);
// Dashboard: /internal/scheduler
// JSON:      /internal/scheduler/api/status
```

Configure authentication and authorization in the host application. The page and JSON API expose job names, schedules, error messages, exception types, and `Exception.HResult` values.

## What the dashboard shows

- Registered, running, failed, and paused jobs; next run and last start.
- Search and status filters.
- Per-job schedule, last start and finish, duration, last success, and consecutive failures.
- Error message, exception type, and `HResult` code.
- Up to 100 recent completed executions, kept in memory and lost on restart.

The page polls every five seconds and does not modify jobs. For a durable history or a dashboard in a separate process, connect the scheduler's diagnostics to an external monitoring store.

## Preview

From the repository root:

```powershell
dotnet run --project FluentTaskScheduler.UI.Demo --urls http://localhost:5057
```

Open `http://localhost:5057/scheduler`. The demo includes successful, failed, and paused jobs.
