using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using FlowScheduler.Core.Interfaces.AI;

namespace FlowScheduler.WebApi.McpTools;

[McpServerToolType]
public class DiagnosticMcpTool {
    [McpServerTool(Name = "run_diagnostic", ReadOnly = true)]
    [Description("Diagnose an error by searching for known solutions in the RAG knowledge base. Returns matching diagnostics with severity and confidence.")]
    public async Task<string> RunDiagnosticAsync(
        IRagSearchService ragSearchService,
        [Description("The error message or description to diagnose")] string errorDescription,
        [Description("Error category for targeted search (e.g. 'sql', 'hangfire', 'redis'). Null for broad search.")] string? category = null) {
        // 1. Chiama ragSearchService.SearchAsync(errorDescription, category)
        var result = await ragSearchService.SearchAsync(errorDescription, category);
        // 2. Se FormattedContext vuoto → return "No known solutions found for this error."
        if (result.FormattedContext == null || result.FormattedContext.Trim().Length == 0) {
            return "No known solutions found for this error.";
        }
        // 3. Costruisci risposta con StringBuilder :
        //    - Se HasBypassMatch → "HIGH CONFIDENCE MATCH (>80%):" + BestMatch.Resolution
        //      + "Severity: {BestMatch.Severity}"
        //    - Altrimenti → "Possible solutions:" + FormattedContext
        var sb = new StringBuilder();
        if (result.HasBypassMatch && result.BestMatch != null) {
            sb.AppendLine($"HIGH CONFIDENCE MATCH (>80%):");
            sb.AppendLine($"Severity: {result.BestMatch.Severity}");
            sb.AppendLine($"Solution: {result.BestMatch.Resolution}");
            sb.AppendLine();
        }
        sb.AppendLine("Possible solutions:");
        sb.AppendLine(result.FormattedContext);

        // 4. Return stringa formattata
        return sb.ToString();
    }
}