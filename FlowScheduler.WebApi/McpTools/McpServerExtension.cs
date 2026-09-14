using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Infrastructure.AI.RAG;

namespace FlowScheduler.WebApi.McpTools;

public static class McpServerExtension {
    /// <summary>
    /// Registers the necessary services for MCP (Model Context Protocol) in the provided IServiceCollection.
    /// </summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IServiceCollection AddMcpServices(this IServiceCollection services) {
        // 1. Registra IRagSearchService (mancante in WebApi, necessario per 2 tool MCP)
        services.AddTransient<IRagSearchService, RagSearchService>();

        // 2. Registra MCP Server con auto-discovery dei tool via attributi
        services.AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly();

        return services;
    }
    /// <summary>
    /// Maps the MCP endpoints to the provided WebApplication instance, enabling the registered MCP tools to be accessible via HTTP.
    /// </summary>
    /// <param name="app"></param>
    /// <returns></returns>
    public static WebApplication MapMcpEndpoints(this WebApplication app) {
        app.MapMcp();
        return app;
    }
}