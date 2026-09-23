using System.Collections.Concurrent;

namespace FluentTaskScheduler.Core
{
    public class ScheduledJobRegistry : IScheduledJobRegistry
    {
        private readonly ConcurrentQueue<TimedJobConfig> _jobs = new();
        private volatile IReadOnlyList<TimedJobConfig>? _cachedJobs;
        private readonly object _cacheLock = new();
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
        private readonly HashSet<TimedJobConfig> _instances = [];
        private readonly bool _requiresStableKeys;
        public ScheduledJobRegistry(Storage.IJobStateStore? store = null) => _requiresStableKeys = store?.RequiresStableKeys == true;
        private volatile TaskCompletionSource<bool> _changeSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void AddJob(TimedJobConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            lock (config)
            {
                JobValidation.Validate(config);
                var key = config.Key;
                if (_requiresStableKeys && key is null)
                    throw new InvalidOperationException("Persistent jobs require a stable Key. Use WithKey(...).");
                lock (_cacheLock)
                {
                    if (_instances.Contains(config) || (key is not null && _keys.Contains(key)))
                        throw new InvalidOperationException("This job or its Key is already registered.");
                    config.Register();
                    _instances.Add(config);
                    if (key is not null) _keys.Add(key);
                    config.Changed += OnJobChanged;
                    _jobs.Enqueue(config);
                    _cachedJobs = null;
                }
            }
            SignalChange();
        }

        private void OnJobChanged(object? sender, EventArgs args) => SignalChange();

        private void SignalChange()
        {
            TaskCompletionSource<bool> previousSignal;
            lock (_cacheLock)
            {
                previousSignal = _changeSignal;
                _changeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            previousSignal.TrySetResult(true);
        }

        public IReadOnlyList<TimedJobConfig> GetJobs()
        {
            var cachedJobs = _cachedJobs;
            if (cachedJobs is not null)
                return cachedJobs;

            lock (_cacheLock)
            {
                _cachedJobs ??= Array.AsReadOnly(_jobs.ToArray());
                return _cachedJobs;
            }
        }

        public Task WaitForChangeAsync(CancellationToken cancellationToken) =>
            _changeSignal.Task.WaitAsync(cancellationToken);
    }
}
