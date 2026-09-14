using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.DependencyInjection;

namespace FlowScheduler.Infrastructure.AI.Middleware;

/// <summary>
/// Layer 5: Registrazione middleware della catena agenti.
/// Ogni IAgentMiddleware viene eseguito in ordine prima di ogni tool call.
/// </summary>
public static class AiMiddlewareExtension {
    public static IServiceCollection AddAiMiddleware(this IServiceCollection services) {
        services.AddSingleton<IAgentMiddleware, SqlReadOnlyMiddleware>();
        services.AddSingleton<IAgentMiddleware, ToolCallLoggingMiddleware>();
        return services;
    }
}
