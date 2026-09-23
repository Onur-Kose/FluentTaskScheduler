using FluentTaskScheduler.Core;
using FluentTaskScheduler.DSL;
using FluentTaskScheduler.Execution;
using FluentTaskScheduler.Diagnostics;
using FluentTaskScheduler.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
        public static IServiceCollection AddFluentTaskScheduler(this IServiceCollection services, Action<SchedulerOptions>? configure = null)
        {
            services.AddOptions<SchedulerOptions>();
            if (configure is not null) services.Configure(configure);
            services.TryAddSingleton<IScheduledJobRegistry, ScheduledJobRegistry>();
            services.TryAddSingleton<IJobStateStore, MemoryJobStateStore>();
            services.TryAddSingleton<SchedulerDiagnostics>();
            services.TryAddScoped<JobExecutionContext>();
            services.AddHostedService<FlexibleSchedulerService>();

            return services;
        }

        /// <summary>Uses a shared directory for durable state and exclusive execution across processes.</summary>
        public static IServiceCollection AddFileJobStateStore(this IServiceCollection services, string directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            services.Replace(ServiceDescriptor.Singleton<IJobStateStore>(_ => new FileJobStateStore(directory)));
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

        /// <summary>
        /// Registers a custom job configuration class that inherits from FlexibleSchedulerTaskConfig.
        /// Jobs will be automatically configured during application startup.
        /// </summary>
        /// <typeparam name="TConfig">Your custom configuration class.</typeparam>
        /// <param name="services">The IServiceCollection to extend.</param>
        /// <returns>The IServiceCollection for chaining.</returns>
        /// <example>
        /// <code>
        /// services.AddFluentTaskScheduler()
        ///         .AddJobConfiguration&lt;MyScheduledJobs&gt;();
        /// </code>
        /// </example>
        public static IServiceCollection AddJobConfiguration<TConfig>(this IServiceCollection services)
            where TConfig : FlexibleSchedulerTaskConfig, new()
        {
            services.AddHostedService<JobConfigurationHostedService<TConfig>>();
            return services;
        }
    }

    /// <summary>
    /// Internal hosted service that initializes job configurations.
    /// </summary>
    internal class JobConfigurationHostedService<TConfig> : IHostedService
        where TConfig : FlexibleSchedulerTaskConfig, new()
    {
        private readonly IServiceProvider _serviceProvider;

        public JobConfigurationHostedService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var config = new TConfig();
            config.Initialize(_serviceProvider);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

