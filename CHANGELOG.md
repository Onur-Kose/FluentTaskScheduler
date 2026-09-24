# Changelog

## 0.2.1 (scheduler) / 0.1.0-beta.1 (UI)

- Add the optional `FluentTaskScheduler.UI` NuGet package for ASP.NET Core applications.
- Map the read-only dashboard and JSON status endpoint with one call; routes are Development-only by default.
- Show recent execution start and finish times, duration, exception type, and `HResult` code.
- Package both scheduler and UI in CI and verify the UI package's dependency on the scheduler.

## 0.2.0

- Validate public registrations and live mutations. Reject invalid edits atomically, keeping the previous valid definition.
- Notify the registry and wake the scheduler on property and collection changes. Add atomic Update, pause/resume, and observer events.
- Keep actual execution ownership separate from the publicly writable IsRunning flag, preventing accidental overlapping execution.
- Add cancellation-aware For/ThenFor overloads, cooperative timeouts, bounded concurrency, and retries with exponential backoff.
- Add execution identity and attempt metadata through the scoped JobExecutionContext.
- Add an extensible state-store contract and a file provider with atomic checkpoints and exclusive execution across processes.
- Restore missed/interrupted occurrences, retry state, and live scheduling configuration after restart.
- Isolate job and infrastructure failures, expose health/status, metrics, traces, and completion notifications.
- Add Windows/Linux CI, dependency auditing, package verification, and a Native AOT smoke app.

### Migration from 0.1.x

DailyAtTimes and ExcludedDays now use JobCollection<T>, so Add/Remove/index assignments are observable.
Collection expressions and assignment from arrays/List<T> still work; use IList<T>/IReadOnlyList<T> or ToArray()
where code explicitly expected a concrete List<T>/array. Recompile consumers for this public type change.
The same fluent schedule methods and tokenless delegates remain available.

Registered definitions are validated immediately, including direct AddJob calls. Use Update when multiple
fields must change together (for example switching from Every to DailyAt or changing both window boundaries).
IsRunning = true manually suspends dispatch; IsRunning = false resumes that manual suspension but never
pretends that an active invocation has ended. IsPaused provides an explicit pause control.

Defaults are four concurrent jobs, a cooperative five-minute timeout, and no automatic retries. Set
DefaultTimeout = null to retain unlimited execution duration. File storage is opt-in and requires a stable
WithKey/Key for each job. Persisted configuration takes precedence over startup defaults on restart;
live changes after registration/start are propagated and persisted. See the README for operational semantics.
