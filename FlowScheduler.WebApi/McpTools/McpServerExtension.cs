using FlowScheduler.Infrastructure.AI.McpServerTools;

namespace FlowScheduler.WebApi.McpTools;

public static class McpServerExtension {
    /// <summary>
    /// Registers the necessary services for MCP (Model Context Protocol) in the provided IServiceCollection.
    /// </summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IServiceCollection AddMcpServices(this IServiceCollection services) {
        // 1. Dipendenze dei tool MCP (classi in FlowScheduler.Infrastructure.AI.McpServerTools)
        services.AddMcpServerTools();

        // 2. Registra MCP Server con auto-discovery dei tool via attributi, nell'assembly Infrastructure
        services.AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly(typeof(McpServerToolsExtension).Assembly);

        return services;
    }
    /// <summary>
    /// Maps the MCP endpoints to the provided WebApplication instance, enabling the registered MCP tools to be accessible via HTTP.
    /// </summary>
    /// <param name="app"></param>
    /// <returns></returns>
    public static WebApplication MapMcpEndpoints(this WebApplication app) {
        app.MapMcp("/mcp");
        return app;
    }
}