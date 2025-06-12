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
        private Expression<Func<T, Task>>? _expression;

        public SchedulerBuilder(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _registry = _serviceProvider.GetRequiredService<IScheduledJobRegistry>();
        }

        public SchedulerBuilder<T> For(Expression<Func<T, Task>> method)
        {
            _expression = method;
            return this;
        }

        public SchedulerBuilder<T> DailyAt(string time)
        {
            _config.DailyAt = TimeSpan.Parse(time);
            return this;
        }

        public SchedulerBuilder<T> Every(TimeSpan interval)
        {
            _config.RepeatEvery = interval;
            return this;
        }

        public SchedulerBuilder<T> Between(string start, string end)
        {
            _config.IntervalStart = TimeSpan.Parse(start);
            _config.IntervalEnd = TimeSpan.Parse(end);
            return this;
        }

        public void Do()
        {
            if (_expression == null)
                throw new InvalidOperationException("No method specified for execution.");

            _config.Action = async sp =>
            {
                var instance = sp.GetRequiredService<T>();
                var func = _expression.Compile();
                await func(instance);
            };

            _registry.AddJob(_config);
        }
    }
}
