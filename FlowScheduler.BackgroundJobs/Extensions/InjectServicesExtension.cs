using FlowScheduler.Infrastructure.Processing;
using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.MCP;
using FlowScheduler.Core.Interfaces.Monitoring;
using FlowScheduler.Core.Interfaces.Processing;
using FlowScheduler.Infrastructure.AI;
using FlowScheduler.Infrastructure.MCP;
using FlowScheduler.Infrastructure.Messaging.RabbitMq;
using FlowScheduler.Infrastructure.Monitoring;
using FlowScheduler.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlowScheduler.BackgroundJobs.Extensions;

/// <summary>
/// Main composition root — orchestrates the registration of all subsystems.
/// Delegates to feature extensions where available; trivial registrations are inlined.
/// </summary>
public static class InjectServicesExtension {
    public static IServiceCollection AddWorkerServices(this IServiceCollection services, IConfiguration configuration) {
        // --- Configuration ---
        // Binds appsettings.json values to HangFireOptions for the IOptions pattern
        services.Configure<HangFireOptions>(configuration.GetSection("HangFireOptions"));
        // Singleton bridge: resolves from IOptions<> so raw HangFireOptions injection works via single source of truth
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
        
        // MCP client: connects to external servers, discovers new tools, populates IToolRegistry
        services.Configure<McpServerOptions>(configuration.GetSection("McpServerOptions"));
        services.AddSingleton<IMcpClientService, McpClientService>();

        // Internal telemetry (HealthChecks) to monitor Worker health
        services.Configure<HealthMonitorOptions>(configuration.GetSection("HealthMonitorOptions"));
        services.AddScoped<IHealthMonitorService, HealthMonitorService>();
        
        // Creates RabbitMQ connections for continuous queue consumption (e.g., crypto.trades)
        services.AddRabbitMqInfrastructure(configuration);

        return services;
    }
}
