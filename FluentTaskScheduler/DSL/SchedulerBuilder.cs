using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using FluentTaskScheduler.Core;

namespace FluentTaskScheduler.DSL
{
    public class SchedulerBuilder<T> where T : notnull
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IScheduledJobRegistry _registry;
        private readonly TimedJobConfig _config = new();
        private readonly List<Expression<Func<T, Task>>> _steps = [];

        public SchedulerBuilder(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _registry = _serviceProvider.GetRequiredService<IScheduledJobRegistry>();
        }

        public SchedulerBuilder<T> For(Expression<Func<T, Task>> method)
        {
            _steps.Clear(); 
            _steps.Add(method);
            return this;
        }

        public SchedulerBuilder<T> ThenFor(Expression<Func<T, Task>> method)
        {
            if(_steps.Count == 0 )
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


            _config.Action = async sp =>
            {
                var instance = sp.GetRequiredService<T>();
                foreach (var expr in _steps)
                {
                    var func = expr.Compile();
                    await func(instance);
                }
            };

            _registry.AddJob(_config);
        }
    }
}
