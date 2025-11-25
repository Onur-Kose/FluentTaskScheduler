using FluentTaskScheduler.DSL;
using System.Linq.Expressions;

namespace FluentTaskScheduler.Core
{
    /// <summary>
    /// Base class for defining scheduled jobs in a structured way.
    /// Inherit from this class and override ConfigureJobs() to define your jobs.
    /// </summary>
    public abstract class FlexibleSchedulerTaskConfig
    {
        private IServiceProvider _serviceProvider = default!;
        /// <summary>
        /// Override this method to configure your scheduled jobs.
        /// </summary>
        protected abstract void ConfigureJobs();

        /// <summary>
        /// Initializes the configuration with the service provider and triggers job configuration.
        /// </summary>
        internal void Initialize(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            ConfigureJobs();
        }

        /// <summary>
        /// Creates a fluent scheduler for the specified service type.
        /// </summary>
        /// <typeparam name="T">The service type to schedule.</typeparam>
        /// <param name="method">The method expression to execute.</param>
        /// <param name="name">Optional custom job name.</param>
        /// <returns>A SchedulerBuilder instance for method chaining.</returns>
        protected SchedulerBuilder<T> For<T>(
            Expression<Func<T, Task>> method,
            string? name = null)
            where T : notnull
        {
            return new SchedulerBuilder<T>(_serviceProvider)
                .For(method, name);
        }
    }
}
