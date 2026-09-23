using FluentTaskScheduler.Core;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Linq.Expressions;

namespace FluentTaskScheduler.DSL
{
    /// <summary>
    /// Fluent DSL builder that allows defining scheduled jobs using chained syntax.
    /// Supports DailyAt, Every, Between, exclusion days, and multi-step sequencing.
    /// </summary>
    public class SchedulerBuilder<T> where T : notnull
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IScheduledJobRegistry _registry;
        private TimedJobConfig _config = null!;
        private readonly List<Expression<Func<T, CancellationToken, Task>>> _steps = [];

        /// <summary>
        /// Creates a new SchedulerBuilder instance.
        /// </summary>
        public SchedulerBuilder(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _registry = _serviceProvider.GetRequiredService<IScheduledJobRegistry>();
        }
        /// <summary>
        /// Defines the first action that will be executed by this job.
        /// Must be called before using ThenFor, DailyAt, Every, Between, or NotRunThisDays.
        /// </summary>
        /// <param name="method">The async method expression to execute.</param>
        /// <param name="name">Optional custom job name.</param>
        public SchedulerBuilder<T> For(Expression<Func<T, Task>> method, string? name = null)
        {
            ArgumentNullException.ThrowIfNull(method);
            return For(WithToken(method), name);
        }

        public SchedulerBuilder<T> For(Expression<Func<T, CancellationToken, Task>> method, string? name = null)
        {
            ArgumentNullException.ThrowIfNull(method);
            _config = new TimedJobConfig();
            _steps.Clear();
            _steps.Add(method);
            _config.Name = GetOrGenerateJobName(method, name);
            return this;
        }
        /// <summary>
        /// Adds an additional method to be executed sequentially after the previous steps.
        /// </summary>
        /// <param name="method">The async method expression to execute.</param>
        public SchedulerBuilder<T> ThenFor(Expression<Func<T, Task>> method)
        {
            ArgumentNullException.ThrowIfNull(method);
            return ThenFor(WithToken(method));
        }

        public SchedulerBuilder<T> ThenFor(Expression<Func<T, CancellationToken, Task>> method)
        {
            EnsureForCalled();
            ArgumentNullException.ThrowIfNull(method);
            _steps.Add(method);
            return this;
        }
        /// <summary>
        /// Schedules the job to run every day at the specified time.
        /// </summary>
        /// <param name="time">Time format must be 'HH:mm' or 'HH:mm:ss'.</param>
        public SchedulerBuilder<T> DailyAt(string time)
        {
            EnsureForCalled();
            _config.DailyAtTimes.Add(ParseTime(time, nameof(time)));
            return this;
        }

        public SchedulerBuilder<T> DailyAt(params string[] times)
        {
            EnsureForCalled();
            ArgumentNullException.ThrowIfNull(times);
            if (times.Length == 0)
                throw new ArgumentException("Specify at least one daily time.", nameof(times));

            var parsedTimes = times.Select(time => ParseTime(time, nameof(times))).ToArray();
            _config.DailyAtTimes.AddRange(parsedTimes);
            return this;
        }
        /// <summary>
        /// Schedules the job to run repeatedly based on the provided interval.
        /// </summary>
        /// <param name="interval">Minimum allowed interval is 1 second.</param>
        public SchedulerBuilder<T> Every(TimeSpan interval)
        {
            if (interval.TotalSeconds < 1)
                throw new ArgumentException("Repeat interval must be at least 1 second. Please change Every parameter");

            if (_steps.Count == 0)
                throw new InvalidOperationException("You must define an action with For(...) before using this method.");


            _config.RepeatEvery = interval;
            return this;
        }
        /// <summary>
        /// Defines a time window (start-end) during which repeated jobs are allowed to run.
        /// Must be used together with Every(...).
        /// </summary>
        /// <param name="start">Start time in 'HH:mm' or 'HH:mm:ss' format.</param>
        /// <param name="end">End time in 'HH:mm' or 'HH:mm:ss' format.</param>
        public SchedulerBuilder<T> Between(string start, string end)
        {
            EnsureForCalled();
            return Between(ParseTime(start, nameof(start)), ParseTime(end, nameof(end)));
        }

        public SchedulerBuilder<T> Between(TimeSpan start, TimeSpan end)
        {
            EnsureForCalled();
            if (start < TimeSpan.Zero || start >= TimeSpan.FromDays(1))
                throw new ArgumentOutOfRangeException(nameof(start));
            if (end < TimeSpan.Zero || end >= TimeSpan.FromDays(1))
                throw new ArgumentOutOfRangeException(nameof(end));
            if (start >= end)
                throw new ArgumentException("Start time must be earlier than end time.");

            _config.IntervalStart = start;
            _config.IntervalEnd = end;
            return this;
        }
        /// <summary>
        /// Prevents the job from running on specified days of the week.
        /// </summary>
        public SchedulerBuilder<T> NotRunThisDays(params DayOfWeek[] days)
        {
            EnsureForCalled();
            ArgumentNullException.ThrowIfNull(days);
            if (days.Any(day => !Enum.IsDefined(day)))
                throw new ArgumentException("Invalid day of the week.", nameof(days));
            _config.ExcludedDays = days.Distinct().ToArray();
            return this;
        }
        /// <summary>
        /// Finalizes the job definition and registers it with the scheduler.
        /// </summary>
        public void Do()
        {
            if (_steps.Count == 0)
                throw new InvalidOperationException("No steps defined for execution.");

            if (_config.DailyAtTimes.Count != 0 && _config.RepeatEvery.HasValue)
                throw new InvalidOperationException("You cannot use both .DailyAt(...) and .Every(...). These options are mutually exclusive.");

            if (_config.DailyAtTimes.Count == 0 && !_config.RepeatEvery.HasValue)
                throw new InvalidOperationException("Specify either Every(...) or DailyAt(...).");

            if (_config.ExcludedDays?.Length == 7)
                throw new InvalidOperationException("All days are excluded. The job would never run. Change NotRunThisDays");

            if ((_config.IntervalStart.HasValue || _config.IntervalEnd.HasValue) && !_config.RepeatEvery.HasValue)
            {
                throw new InvalidOperationException(
                    "When using .Between(...), you must also specify .Every(...)");
            }
            var steps = _steps.Select(expression => expression.Compile()).ToArray();
            async Task ExecuteSteps(IServiceProvider sp, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var instance = sp.GetRequiredService<T>();
                foreach (var step in steps)
                {
                    token.ThrowIfCancellationRequested();
                    await step(instance, token);
                }
                token.ThrowIfCancellationRequested();
            }
            _config.Func = sp => ExecuteSteps(sp, CancellationToken.None);
            _config.CancellableFunc = ExecuteSteps;

            JobValidation.Validate(_config);

            _config.NextRun = JobSchedule.CalculateNextRun(_config, DateTime.UtcNow);

            _registry.AddJob(_config);
            _steps.Clear();
            _config = null!;
        }
        /// <summary>
        /// Ensures For(...) was called before using configuration methods.
        /// </summary>
        private void EnsureForCalled()
        {
            if (_steps.Count == 0)
                throw new InvalidOperationException("You must define an action using For(...) first.");
        }
        /// <summary>
        /// Returns the provided name, or generates one from the method name if empty.
        /// </summary>
        private static string GetOrGenerateJobName(LambdaExpression method, string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                var methodName = ExtractMethodName(method);
                return GenerateJobName(methodName);
            }
            return name + GenerateShortId();
        }

        /// <summary>
        /// Generates a short unique identifier (6 characters).
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
            return $"_{new string(randomCode)}";
        }

        /// <summary>
        /// Extracts the method name from the expression tree.
        /// </summary>
        private static string ExtractMethodName(LambdaExpression expression)
        {
            return expression.Body switch
            {
                MethodCallExpression methodCall => methodCall.Method.Name.Replace("Async", ""),

                MemberExpression memberAccess => memberAccess.Member.Name,

                _ => "UnknownMethod"
            };
        }

        /// <summary>
        /// Builds a unique job name using the service type and method name.
        /// </summary>
        private static string GenerateJobName(string methodName)
        {
            var serviceTypeName = typeof(T).Name;
            if (serviceTypeName.EndsWith("Service", StringComparison.Ordinal))
                serviceTypeName = serviceTypeName[..^"Service".Length];
            if (serviceTypeName.Length > 1 && serviceTypeName[0] == 'I' && char.IsUpper(serviceTypeName[1]))
                serviceTypeName = serviceTypeName[1..];

            return $"{serviceTypeName}_{methodName}{GenerateShortId()}";
        }

        private static TimeSpan ParseTime(string value, string parameterName)
        {
            if (!TimeSpan.TryParseExact(value, [@"hh\:mm", @"hh\:mm\:ss"],
                CultureInfo.InvariantCulture, out var time) ||
                time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
                throw new ArgumentException("Invalid time format. Expected 'HH:mm' or 'HH:mm:ss' within a single day.", parameterName);
            return time;
        }

        public SchedulerBuilder<T> WithKey(string key)
        {
            EnsureForCalled();
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            _config.Key = key;
            return this;
        }

        public SchedulerBuilder<T> WithTimeout(TimeSpan timeout)
        {
            EnsureForCalled();
            _config.Timeout = timeout;
            return this;
        }

        public SchedulerBuilder<T> WithRetry(RetryPolicy policy)
        {
            EnsureForCalled();
            _config.Retry = policy;
            return this;
        }

        private static Expression<Func<T, CancellationToken, Task>> WithToken(Expression<Func<T, Task>> method) =>
            Expression.Lambda<Func<T, CancellationToken, Task>>(method.Body, method.Parameters[0],
                Expression.Parameter(typeof(CancellationToken), "cancellationToken"));
    }
}
