namespace FluentTaskScheduler.Core;

/// <summary>A live job definition. Registered definitions validate and notify on every mutation.</summary>
public class TimedJobConfig
{
    private string _name = string.Empty;
    private string? _key;
    private Func<IServiceProvider, Task>? _func;
    private Func<IServiceProvider, CancellationToken, Task>? _cancellableFunc;
    private JobCollection<TimeSpan> _dailyAtTimes;
    private JobCollection<DayOfWeek>? _excludedDays;
    private TimeSpan? _repeatEvery, _intervalStart, _intervalEnd, _timeout;
    private DateTime _nextRun;
    private bool _manualRunning, _executing, _paused, _registered;
    private RetryPolicy _retry = new();
    private long _revision;

    public TimedJobConfig() => _dailyAtTimes = Attach(new JobCollection<TimeSpan>());

    /// <summary>Raised after an accepted mutation. Registered definitions are validated; observers must not block.</summary>
    public event EventHandler? Changed;
    public string Name { get { lock (this) return _name; } set => Change(() => _name = value); }
    /// <summary>Stable identity used by persistent stores. Set before registration.</summary>
    public string? Key
    {
        get { lock (this) return _key; }
        set
        {
            lock (this)
            {
                if (_registered && value != _key)
                    throw new InvalidOperationException("A registered job's Key cannot change; use a new definition for a new identity.");
                Change(() => _key = value);
            }
        }
    }
    public Func<IServiceProvider, Task> Func
    {
        get { lock (this) return _func!; }
        set => Change(() => { _func = value; _cancellableFunc = null; });
    }
    public Func<IServiceProvider, CancellationToken, Task>? CancellableFunc
    {
        get { lock (this) return _cancellableFunc; }
        set => Change(() => _cancellableFunc = value);
    }
    public JobCollection<TimeSpan> DailyAtTimes
    {
        get { lock (this) return _dailyAtTimes; }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var items = value.ToArray();
            Change(() => _dailyAtTimes.ReplaceSilently(items), true);
        }
    }
    public JobCollection<DayOfWeek>? ExcludedDays
    {
        get { lock (this) return _excludedDays; }
        set
        {
            var items = value?.ToArray();
            Change(() =>
            {
                if (items is null) { _excludedDays?.Detach(); _excludedDays = null; }
                else if (_excludedDays is null) _excludedDays = Attach(new JobCollection<DayOfWeek>(items));
                else _excludedDays.ReplaceSilently(items);
            }, true);
        }
    }
    public TimeSpan? RepeatEvery { get { lock (this) return _repeatEvery; } set => Change(() => _repeatEvery = value, true); }
    public TimeSpan? IntervalStart { get { lock (this) return _intervalStart; } set => Change(() => _intervalStart = value, true); }
    public TimeSpan? IntervalEnd { get { lock (this) return _intervalEnd; } set => Change(() => _intervalEnd = value, true); }
    public DateTime NextRun
    {
        get { lock (this) return _nextRun; }
        set => Change(() => _nextRun = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
    /// <summary>Manual true suspends dispatch. Setting false cannot conceal an actual active execution.</summary>
    public bool IsRunning { get { lock (this) return _executing || _manualRunning; } set => Change(() => _manualRunning = value); }
    public bool IsPaused { get { lock (this) return _paused; } set => Change(() => _paused = value); }
    public TimeSpan? Timeout { get { lock (this) return _timeout; } set => Change(() => _timeout = value); }
    public RetryPolicy Retry { get { lock (this) return _retry; } set => Change(() => _retry = value ?? throw new ArgumentNullException(nameof(value))); }
    internal long Revision { get { lock (this) return _revision; } }

    /// <summary>Applies a group of changes atomically; failure leaves this definition untouched.</summary>
    public void Update(Action<TimedJobConfig> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (this)
        {
            var draft = Snapshot();
            update(draft);
            if (_registered && draft.Key != _key)
                throw new InvalidOperationException("A registered job's Key cannot change.");
            var scheduleChanged = !SameSchedule(draft);
            if (_registered)
            {
                JobValidation.Validate(draft);
                if (scheduleChanged && draft._nextRun == _nextRun)
                    draft._nextRun = JobSchedule.CalculateNextRun(draft, DateTime.UtcNow);
            }
            CopyFrom(draft);
            NotifyChanged();
        }
    }

    private JobCollection<T> Attach<T>(JobCollection<T> collection)
    {
        collection.Attach(this, action => Change(action, true));
        return collection;
    }

    private void Change(Action action, bool schedule = false)
    {
        lock (this)
        {
            if (!_registered) { action(); NotifyChanged(); return; }
            var before = Snapshot();
            var previousDays = _excludedDays;
            try
            {
                action();
                JobValidation.Validate(this);
                if (schedule) _nextRun = JobSchedule.CalculateNextRun(this, DateTime.UtcNow);
            }
            catch
            {
                _excludedDays = previousDays is null ? null : Attach(previousDays);
                CopyFrom(before);
                throw;
            }
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        _revision++;
        // Observers cannot roll back a committed definition or suppress the registry wake-up.
        if (Changed is { } changed)
            foreach (EventHandler handler in changed.GetInvocationList())
                try { handler(this, EventArgs.Empty); } catch { /* observers are isolated */ }
    }

    internal void Register()
    {
        lock (this) { JobValidation.Validate(this); _registered = true; }
    }

    internal TimedJobConfig Snapshot()
    {
        lock (this)
        {
            var snapshot = new TimedJobConfig();
            snapshot.CopyFrom(this);
            snapshot._revision = _revision;
            return snapshot;
        }
    }

    private void CopyFrom(TimedJobConfig other)
    {
        _name = other._name; _key = other._key;
        _func = other._func; _cancellableFunc = other._cancellableFunc;
        _repeatEvery = other._repeatEvery; _intervalStart = other._intervalStart; _intervalEnd = other._intervalEnd;
        _nextRun = other._nextRun; _timeout = other._timeout; _retry = other._retry;
        _manualRunning = other._manualRunning; _paused = other._paused;
        _dailyAtTimes.ReplaceSilently(other._dailyAtTimes.ToArray());
        if (other._excludedDays is null) { _excludedDays?.Detach(); _excludedDays = null; }
        else if (_excludedDays is null) _excludedDays = Attach(new JobCollection<DayOfWeek>(other._excludedDays));
        else _excludedDays.ReplaceSilently(other._excludedDays.ToArray());
    }

    private bool SameSchedule(TimedJobConfig other) =>
        _repeatEvery == other._repeatEvery && _intervalStart == other._intervalStart && _intervalEnd == other._intervalEnd &&
        _dailyAtTimes.SequenceEqual(other._dailyAtTimes) &&
        (_excludedDays ?? []).SequenceEqual(other._excludedDays ?? []);

    internal bool IsExecuting { get { lock (this) return _executing; } }
    internal bool TryBeginExecution(bool allowSuspended = false)
    {
        lock (this)
        {
            if (_executing || (!allowSuspended && (_manualRunning || _paused))) return false;
            _executing = true;
            return true;
        }
    }
    internal void EndExecution() { lock (this) _executing = false; }
    internal void SetNextRun(DateTime nextRun, long expectedRevision)
    {
        lock (this) { if (_revision == expectedRevision) _nextRun = nextRun; }
    }

    internal TimedJobConfig? Restore(Storage.JobState state, long expectedRevision)
    {
        lock (this)
        {
            if (_revision != expectedRevision) return null;
            var draft = Snapshot();
            state.Schedule.Apply(draft);
            draft.NextRun = state.NextRunUtc;
            JobValidation.Validate(draft);
            var changed = !SameSchedule(draft) || _paused != draft._paused || _manualRunning != draft._manualRunning ||
                _timeout != draft._timeout || _retry != draft._retry || _nextRun != draft._nextRun;
            CopyFrom(draft);
            if (changed) NotifyChanged();
            return Snapshot();
        }
    }
}
