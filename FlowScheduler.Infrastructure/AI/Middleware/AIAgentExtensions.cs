using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http;
using NRedisStack;

namespace FlowScheduler.Infrastructure.AI.Middleware;

/// <summary>
///  Extension method WithMiddlewares() per AIAgent. Prende tutti gli IAgentMiddleware registrati nel DI e li applica all'agente tramite il builder pattern di Microsoft.Agents.AI. È il
///  collante tra il DI e il pipeline middleware.
/// </summary>
public static class AIAgentExtensions {
    public static AIAgent WithMiddlewares(this AIAgent agent, IEnumerable<IAgentMiddleware> middlewares) {
        var builder = agent.AsBuilder();
        foreach (var mw in middlewares) {
            builder.Use(mw.InvokeAsync);
        }
        return builder.Build();
    }
}