using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text;

namespace FlowScheduler.Infrastructure.AI.Middleware;
/// <summary>
/// Middleware di osservabilità. Logga il nome del tool invocato dall'agente e i suoi argomenti.
/// Utile per il debug per capire quali decisioni prende l'LLM.
/// I log appaiono su Console, Telegram e Dashboard Hangfire tramite Serilog.
/// </summary>
public class ToolCallLoggingMiddleware : IAgentMiddleware {
    private readonly ILogger<ToolCallLoggingMiddleware> _logger;
    private static readonly AsyncLocal<int> _depth = new();

    public ToolCallLoggingMiddleware(ILogger<ToolCallLoggingMiddleware> logger) {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(AIAgent agent, FunctionInvocationContext context, Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken cancellationToken) {
        var depth = _depth.Value;
        var indent = depth > 0 ? new string(' ', depth * 2) + "├─" : "";

        var args = context.Arguments.Count > 0
            ? string.Join(", ", context.Arguments.Select(x => $"[{x.Key} = {x.Value}]"))
            : "nessuno";

        _logger.LogInformation("[TREE depth={Depth}] {Indent}[{AgentName}] → Tool '{ToolName}' invocato (Argomenti: {Args})",
            depth, indent, agent.Name, context.Function.Name, args);

        _depth.Value = depth + 1;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try {
            var result = await next(context, cancellationToken);
            sw.Stop();

            var resultLength = result?.ToString()?.Length ?? 0;
            var indentOut = depth > 0 ? new string(' ', depth * 2) + "└─" : "";
            _logger.LogInformation("{Indent}[{AgentName}] ← Tool '{ToolName}' completato in {ElapsedMs}ms ({ResultLength} chars)",
                indentOut, agent.Name, context.Function.Name, sw.ElapsedMilliseconds, resultLength);

            return result;
        } catch (Exception ex) {
            sw.Stop();
            var indentErr = depth > 0 ? new string(' ', depth * 2) + "└─" : "";
            _logger.LogError(ex, "{Indent}[{AgentName}] ✗ Tool '{ToolName}' FALLITO dopo {ElapsedMs}ms: {Error}",
                indentErr, agent.Name, context.Function.Name, sw.ElapsedMilliseconds, ex.Message);
            throw;
        } finally {
            _depth.Value = depth;
        }
    }
}