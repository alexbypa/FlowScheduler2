using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Infrastructure.AI.Agents;
using FlowScheduler.Infrastructure.AI.RAG;
using Microsoft.Extensions.DependencyInjection;

namespace FlowScheduler.Infrastructure.AI;

/// <summary>
/// Layer 7: Consumer — servizi che usano gli agenti AI.
/// </summary>
public static class AiConsumersExtension {
    public static IServiceCollection AddAiConsumers(this IServiceCollection services) {
        services.AddTransient<IRagIngestionService, RagIngestionService>();
        services.AddTransient<IRagSearchService, RagSearchService>();
        services.AddTransient<IRagBridgeService, HealthRagBridgeService>();
        services.AddTransient<IJobDiagnosticAgent, JobDiagnosticAgent>();
        return services;
    }
}