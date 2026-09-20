using FlowScheduler.Infrastructure.Processing;
using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.Processing;
using FlowScheduler.Infrastructure.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FlowScheduler.Infrastructure.MCP;
using FlowScheduler.Infrastructure.Metrics;
using FlowScheduler.Infrastructure.Messaging.RabbitMq;
using FlowScheduler.Infrastructure.Monitoring;

namespace FlowScheduler.BackgroundJobs.Extensions;

/// <summary>
/// Compositore principale — orchestra la registrazione di tutti i sottosistemi.
/// Non contiene logica di registrazione propria: delega a ogni extension class.
/// </summary>
public static class InjectServicesExtension {
    public static IServiceCollection AddWorkerServices(this IServiceCollection services, IConfiguration configuration) {
        // --- Configurazione ---
        services.Configure<HangFireOptions>(configuration.GetSection("HangFireOptions"));
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HangFireOptions>>().Value);
        services.Configure<GitHubOptions>(configuration.GetSection("GitHubOptions"));

        // --- Result Processing ---
        services.AddScoped<IResultProcessor, ResultProcessorFromDatabase>();

        // --- Sottosistemi ---
        services.AddTelegramServices();
        services.AddRedisCache(configuration);
        services.AddAiPipeline(configuration);
        services.AddJobInfrastructure();
        services.AddMCPClient(configuration);
        services.AddMetricsStore();
        services.AddMonitoring(configuration);
        services.AddRabbitMqInfrastructure(configuration);

        return services;
    }
}
