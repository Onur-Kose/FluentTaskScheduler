using FluentTaskScheduler.Core;
using Microsoft.Extensions.DependencyInjection;
using System.Linq.Expressions;

namespace FluentTaskScheduler.DSL
{
    public class SchedulerBuilder<T> where T : notnull
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IScheduledJobRegistry _registry;
        private TimedJobConfig _config = null!;
        private readonly List<Expression<Func<T, Task>>> _steps = [];

        public SchedulerBuilder(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _registry = _serviceProvider.GetRequiredService<IScheduledJobRegistry>();
        }

        public SchedulerBuilder<T> For(Expression<Func<T, Task>> method, string? name = null)
        {
            _config = new TimedJobConfig();
            _steps.Clear();
            _steps.Add(method);
            _config.Name = GetOrGenerateJobName(method, name);
            return this;
        }

        public SchedulerBuilder<T> ThenFor(Expression<Func<T, Task>> method)
        {
            if (_steps.Count == 0)
                throw new InvalidOperationException("Cannot use ThenFor(...) before calling For(...). Define the initial step first.");
            _steps.Add(method);
            return this;
        }

        public SchedulerBuilder<T> DailyAt(string time)
        {
            if (!TimeSpan.TryParse(time, out var parsedTime))
                throw new ArgumentException("Invalid time format. Expected format: 'HH:mm' or 'HH:mm:ss'.");

            if (_steps.Count == 0)
                throw new InvalidOperationException("You must define an action with For(...) before using this method.");

            _config.DailyAtTimes.Add(parsedTime);
            return this;
        }


        public SchedulerBuilder<T> Every(TimeSpan interval)
        {
            if (interval.TotalSeconds < 1)
                throw new ArgumentException("Repeat interval must be at least 1 second. Plase change Every parameter");

            if (_steps.Count == 0)
                throw new InvalidOperationException("You must define an action with For(...) before using this method.");


            _config.RepeatEvery = interval;
            return this;
        }

        public SchedulerBuilder<T> Between(string start, string end)
        {
            if (_steps.Count == 0)
                throw new InvalidOperationException("You must define an action with For(...) before using this method.");


            if (!TimeSpan.TryParse(start, out var startTime))
                throw new ArgumentException("Invalid start time format. Expected format: 'HH:mm' or 'HH:mm:ss'.");

            if (!TimeSpan.TryParse(end, out var endTime))
                throw new ArgumentException("Invalid end time format. Expected format: 'HH:mm' or 'HH:mm:ss'.");


            if (startTime >= endTime)
                throw new ArgumentException("Start time must be earlier than end time.");

            _config.IntervalStart = startTime;
            _config.IntervalEnd = endTime;
            return this;
        }

        public SchedulerBuilder<T> NotRunThisDays(params DayOfWeek[] days)
        {
            if (_steps.Count == 0)
                throw new InvalidOperationException("You must define an action with For(...) before using this method.");


            _config.ExcludedDays = days;
            return this;
        }

        public void Do()
        {
            if (_steps.Count == 0)
                throw new InvalidOperationException("No steps defined for execution.");

            if (_config.DailyAtTimes.Count != 0 && _config.RepeatEvery.HasValue)
                throw new InvalidOperationException("You cannot use both .DailyAt(...) and .Every(...). These options are mutually exclusive.");

            if (_config.ExcludedDays?.Length == 7)
                throw new InvalidOperationException("All days are excluded. The job would never run. Change NotRunThisDays");

            if ((_config.IntervalStart.HasValue || _config.IntervalEnd.HasValue) && !_config.RepeatEvery.HasValue)
            {
                throw new InvalidOperationException(
                    "When using .Between(...), you must also specify .Every(...)");
            }
            _config.Action = async sp =>
            {
                var instance = sp.GetRequiredService<T>();
                foreach (var expr in _steps)
                {
                    var func = expr.Compile();
                    await func(instance);
                }
            };

            _config.NextRun = CalculateInitialNextRun();

            _registry.AddJob(_config);
        }

        /// <summary>
        /// Verilen ismi döndürür, eğer boş ise metod adından job ismi oluşturur
        /// </summary>
        private static string GetOrGenerateJobName(Expression<Func<T, Task>> method, string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                var methodName = ExtractMethodName(method);
                return GenerateJobName(methodName);
            }
            return name + GenerateShortId();
        }

        /// <summary>
        /// Kısa, unique ID oluşturur (6 karakter)
        /// </summary>
        private static string GenerateShortId()
        {
            const string chars = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
            var bytes = new byte[4];

            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);

            var randomCode = new char[6];
            var value = BitConverter.ToUInt32(bytes, 0);

            for (int i = 0; i < 6; i++)
            {
                randomCode[i] = chars[(int)(value % (uint)chars.Length)];
                value /= (uint)chars.Length;
            }
            var result = "_" + new string(randomCode);
            return new string(result);
        }

        /// <summary>
        /// Expression'dan metod adını çıkarır
        /// </summary>
        private static string ExtractMethodName(Expression<Func<T, Task>> expression)
        {
            return expression.Body switch
            {
                MethodCallExpression methodCall => methodCall.Method.Name.Replace("Async", ""),

                MemberExpression memberAccess => memberAccess.Member.Name,

                _ => "UnknownMethod"
            };
        }

        /// <summary>
        /// Metod adından unique job adı oluşturur
        /// </summary>
        private static string GenerateJobName(string methodName)
        {
            var serviceTypeName = typeof(T).Name
                .Replace("Service", "")
                .Replace("I", "");

            var shortId = GenerateShortId();

            return $"{serviceTypeName}_{methodName}_{shortId}";
        }

        /// <summary>
        /// Başlangıç için ilk çalıştırma zamanını hesapla
        /// </summary>
        private DateTime CalculateInitialNextRun()
        {
            var now = DateTime.Now;
            DateTime nextRun;

            if (_config.DailyAtTimes.Count > 0)
            {
                var today = now.Date;
                var upcoming = _config.DailyAtTimes
                    .Select(t => today + t)
                    .Where(t => t > now)
                    .OrderBy(t => t)
                    .FirstOrDefault();

                nextRun = upcoming != default
                    ? upcoming
                    : now.Date.AddDays(1) + _config.DailyAtTimes.Min();
            }
            else if (_config.IntervalStart.HasValue && _config.RepeatEvery.HasValue)
            {
                var timeOfDay = now.TimeOfDay;

                if (timeOfDay < _config.IntervalStart.Value)
                    nextRun = now.Date + _config.IntervalStart.Value;
                else if (timeOfDay >= _config.IntervalEnd!.Value)
                    nextRun = now.Date.AddDays(1) + _config.IntervalStart.Value;
                else
                    nextRun = now + _config.RepeatEvery.Value;
            }
            else if (_config.RepeatEvery.HasValue)
            {
                nextRun = now.Add(_config.RepeatEvery.Value);
            }
            else
            {
                nextRun = now.AddSeconds(5);
            }

            while (_config.ExcludedDays?.Contains(nextRun.DayOfWeek) == true)
            {
                if (_config.DailyAtTimes.Count > 0)
                {
                    nextRun = nextRun.AddDays(1);
                }
                else if (_config.RepeatEvery.HasValue)
                {
                    nextRun = nextRun.Add(_config.RepeatEvery.Value);
                }
                else
                {
                    nextRun = nextRun.AddDays(1);
                }
            }

            return nextRun;
        }
    }
}
