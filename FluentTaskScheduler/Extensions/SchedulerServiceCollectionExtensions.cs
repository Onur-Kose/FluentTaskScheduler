using FluentTaskScheduler.Core;
using FluentTaskScheduler.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace FluentTaskScheduler.Extensions
{
    public static class SchedulerServiceCollectionExtensions
    {
        public static IServiceCollection AddFluentTaskScheduler(this IServiceCollection services)
        {
            services.AddSingleton<IScheduledJobRegistry, ScheduledJobRegistry>();
            services.AddHostedService<FlexibleSchedulerService>();

            return services;
        }
    }
}
