using FluentRunly.Core;
using FluentRunly.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace FluentRunly.Extensions
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
