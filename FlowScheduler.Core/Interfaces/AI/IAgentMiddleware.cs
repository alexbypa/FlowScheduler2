using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace FlowScheduler.Core.Interfaces.AI;

/// <summary>
/// Interfaccia contratto per i middleware degli agenti. Definisce il metodo InvokeAsync con il pattern chain-of-responsibility: ogni middleware può ispezionare/bloccare/loggare una chiamata
///  a tool prima di passarla al next.
/// </summary>
public interface IAgentMiddleware {
    ValueTask<object?> InvokeAsync(AIAgent agent, FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken);
}
