using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace FlowScheduler.Infrastructure.AI.McpServerTools;

[McpServerToolType]
public class RagEvalMcpTool {
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    [McpServerTool(Name = "evaluate_rag_query", ReadOnly = true)]
    public async Task<string> EvaluateRagQueryAsync(
        IRagSearchService ragSearchService,
        IChatClient chatClient,
        IRagEvaluator ragEvaluator,
        IEvalInterpretationService interpretationService,
        [Description("The query to evaluate against the RAG pipeline")] string query) {
        var sw = Stopwatch.StartNew();

        var ragResult = await ragSearchService.SearchFreeAsync(query);
        var ragContext = ragResult.FormattedContext;

        var messages = new List<ChatMessage> {
            new(ChatRole.System, "You are a helpful assistant using RAG knowledge"),
            new(ChatRole.User, $"{query}\n\nContext:\n{ragContext}")
        };

        var response = await chatClient.GetResponseAsync(messages);
        var responseText = response.Text ?? "";

        var scores = await ragEvaluator.EvaluateAsync(query, ragContext, responseText);

        string interpretation = scores.Interpretation;
        string[] suggestions = scores.Suggestions;

        if (!scores.EvaluationFailed) {
            (interpretation, suggestions) = await interpretationService.InterpretAsync(
                scores.Relevance, scores.Truth, scores.Completeness,
                scores.Groundedness, scores.ContextRelevance, query);
        }

        sw.Stop();

        var result = new {
            query,
            ragContext = Truncate(ragContext, 500),
            modelResponse = Truncate(responseText, 500),
            scores = new {
                relevance = scores.Relevance,
                truth = scores.Truth,
                completeness = scores.Completeness,
                groundedness = scores.Groundedness,
                contextRelevance = scores.ContextRelevance
            },
            interpretation,
            suggestions,
            evaluationFailed = scores.EvaluationFailed,
            elapsedMs = sw.ElapsedMilliseconds
        };

        return JsonSerializer.Serialize(result, JsonOpts);
    }

    [McpServerTool(Name = "get_eval_history", ReadOnly = true)]
    public async Task<string> GetEvalHistoryAsync(
        IRagEvaluator ragEvaluator,
        [Description("Number of recent results to return (default 10)")] int count = 10) {
        var results = await ragEvaluator.GetRecentResultsAsync(count);
        return JsonSerializer.Serialize(results, JsonOpts);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
