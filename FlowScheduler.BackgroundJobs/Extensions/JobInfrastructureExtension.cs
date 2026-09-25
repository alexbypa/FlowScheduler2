using FlowScheduler.BackgroundJobs.Jobs;
using FlowScheduler.Core.Interfaces.Data;
using FlowScheduler.Core.Interfaces.Jobs;
using FlowScheduler.Core.Interfaces.Metrics;
using FlowScheduler.Infrastructure.Database;
using FlowScheduler.Infrastructure.Metrics;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace FlowScheduler.BackgroundJobs.Extensions;

/// <summary>
/// Registrazione DI per l'infrastruttura dei job: connection factory, resolver,
/// handler, command factory e scan automatico di tutte le implementazioni IJobCommand.
/// </summary>
public static class JobInfrastructureExtension {
    public static IServiceCollection AddJobInfrastructure(this IServiceCollection services) {
        // Both registered; DbConnectionFactoryResolver receives IEnumerable<IDbConnectionFactory>
        services.AddSingleton<IDbConnectionFactory, SqlServerConnectionFactory>();
        services.AddSingleton<IDbConnectionFactory, PostgreSqlConnectionFactory>();
        services.AddSingleton<IDbConnectionFactoryResolver, DbConnectionFactoryResolver>();
        services.AddScoped<IBackgroundJobHandler, BackgroundJobHandler>();
        services.AddSingleton(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase()); // requires AddAppRedis() called first
        services.AddSingleton<IMetricsStore, RedisMetricsStore>();

        // Scan BackgroundJobs assembly for IJobCommand implementations
        // (IJobCommand is now in Core, but implementations stay here)
        var assembly = typeof(JobInfrastructureExtension).Assembly;
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo<IJobCommand>())
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.AddTransient<IInjectCommandFactory, InjectCommandFactory>();
        return services;
    }
}
