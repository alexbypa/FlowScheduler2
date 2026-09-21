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

using FlowScheduler.Infrastructure.Persistence;

namespace FlowScheduler.BackgroundJobs.Extensions;

/// <summary>
/// Main composition root — orchestrates the registration of all subsystems.
/// It doesn't contain its own registration logic; it delegates to extension classes.
/// </summary>
public static class InjectServicesExtension {
    public static IServiceCollection AddWorkerServices(this IServiceCollection services, IConfiguration configuration) {
        // --- Configuration ---
        // Binds appsettings.json values to HangFireOptions for the IOptions pattern
        services.Configure<HangFireOptions>(configuration.GetSection("HangFireOptions"));
        // Exposes HangFireOptions as a Singleton (so you can inject the raw class instead of IOptions<HangFireOptions>)
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HangFireOptions>>().Value);
        // Binds GitHub settings (token, url) to GitHubOptions
        services.Configure<GitHubOptions>(configuration.GetSection("GitHubOptions"));

        // --- Result Processing ---
        // Registers the processor responsible for saving task execution outcomes to the database
        services.AddScoped<IResultProcessor, ResultProcessorFromDatabase>();

        // --- Subsystems ---
        // Initializes the Telegram bot for sending system notifications
        services.AddTelegramServices();
        
        // Registers IConnectionMultiplexer and IDistributedCache on Redis (via our centralized extension)
        services.AddAppRedis(configuration);
        
        // Registers the full 7-layer Agentic AI stack (IChatClient, Tools, Middlewares, and Vector Storage)
        services.AddAiPipeline(configuration);
        
        // Scans the assembly and dynamically registers all IJobCommand implementations (e.g., DatabaseExecuteCommand)
        services.AddJobInfrastructure();
        
        // Starts the MCP client to connect to external servers, discover new tools, and populate IToolRegistry
        services.AddMCPClient(configuration);
        
        // Initializes the DORA/Performance metrics system backed by Redis
        services.AddMetricsStore();
        
        // Configures internal telemetry (e.g., HealthChecks) to monitor Worker health
        services.AddMonitoring(configuration);
        
        // Creates RabbitMQ connections for continuous queue consumption (e.g., crypto.trades)
        services.AddRabbitMqInfrastructure(configuration);

        return services;
    }
}
