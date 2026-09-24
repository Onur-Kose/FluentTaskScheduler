# Dashboard demo

Run from the repository root:

```powershell
dotnet run --project FluentTaskScheduler.UI.Demo --urls http://localhost:5057
```

Open [http://localhost:5057/scheduler](http://localhost:5057/scheduler). The demo registers a successful task, a task that deliberately fails, and a paused task so every main dashboard state can be inspected. The JSON endpoint is at `/scheduler/api/status`.

The demo keeps all state in memory. Stop it with Ctrl+C.
