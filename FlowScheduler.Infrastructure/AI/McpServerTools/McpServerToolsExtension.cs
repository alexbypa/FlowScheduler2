using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Infrastructure.AI.RAG;
using Microsoft.Extensions.DependencyInjection;

namespace FlowScheduler.Infrastructure.AI.McpServerTools;

public static class McpServerToolsExtension {
    /// <summary>
    /// Registers the dependencies needed by the MCP server tools that are not already
    /// registered elsewhere (IVectorStoreService: AiStorageExtension, IMetricsStore: MetricsExtension).
    /// </summary>
    public static IServiceCollection AddMcpServerTools(this IServiceCollection services) {
        services.AddTransient<IRagSearchService, RagSearchService>();
        return services;
    }
}
