using FluentTaskScheduler.Core;
using FluentTaskScheduler.DSL;
using FluentTaskScheduler.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace FluentTaskScheduler.Extensions
{
    /// <summary>
    /// Extension methods for registering FluentTaskScheduler components into the IServiceCollection.
    /// </summary>
    public static class SchedulerServiceCollectionExtensions
    {
        /// <summary>
        /// Registers FluentTaskScheduler infrastructure:
        /// - IScheduledJobRegistry (singleton)
        /// - FlexibleSchedulerService (hosted background service)
        /// </summary>
        /// <param name="services">The IServiceCollection to extend.</param>
        public static IServiceCollection AddFluentTaskScheduler(this IServiceCollection services)
        {
            services.AddSingleton<IScheduledJobRegistry, ScheduledJobRegistry>();
            services.AddHostedService<FlexibleSchedulerService>();

            return services;
        }
        /// <summary>
        /// Registers SchedulerBuilder&lt;T&gt; so that jobs can be configured using DI.
        /// Example: services.AddSchedulerFor&lt;MyService&gt;();
        /// </summary>
        public static IServiceCollection AddSchedulerFor<T>(this IServiceCollection services)
            where T : notnull
        {
            services.AddTransient<SchedulerBuilder<T>>();
            return services;
        }
    }
}
