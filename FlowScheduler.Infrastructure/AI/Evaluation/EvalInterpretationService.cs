using System.Text.Json;
using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace FlowScheduler.Infrastructure.AI.Evaluation;

public sealed class EvalInterpretationService(
    IChatClient chatClient,
    ILogger<EvalInterpretationService> logger) : IEvalInterpretationService
{
    private const float Threshold = 4.0f;

    private static readonly string SystemPrompt =
        "You are a RAG quality analyst. Given these scores (1-5) for a RAG query " +
        "(Relevance, Truth, Completeness measure LLM response quality; " +
        "Groundedness measures if the response is based on the provided context; " +
        "Context Relevance measures if retrieved documents are pertinent to the query), produce: " +
        "1) A brief diagnosis (max 2 sentences) of the main issue. " +
        "2) Max 3 actionable suggestions. " +
        "Respond in JSON: {\"interpretation\": \"...\", \"suggestions\": [\"...\"]}";

    public async Task<(string Interpretation, string[] Suggestions)> InterpretAsync(
        float relevance, float truth, float completeness,
        float groundedness, float contextRelevance,
        string query, CancellationToken ct = default)
    {
        if (relevance >= Threshold && truth >= Threshold && completeness >= Threshold
            && groundedness >= Threshold && contextRelevance >= Threshold)
        {
            logger.LogDebug("All scores >= {Threshold}, skipping LLM call", Threshold);
            return ("Quality OK, no issues detected.", Array.Empty<string>());
        }

        var userMessage = $"Query: {query}\nRelevance: {relevance:F1}\nTruth: {truth:F1}\nCompleteness: {completeness:F1}\nGroundedness: {groundedness:F1}\nContext Relevance: {contextRelevance:F1}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, userMessage)
        };

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
        var raw = response.Text ?? string.Empty;

        try
        {
            var parsed = JsonSerializer.Deserialize<InterpretationResult>(raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed is not null)
            {
                logger.LogInformation("RAG interpretation: {Interpretation}", parsed.Interpretation);
                return (parsed.Interpretation ?? raw, parsed.Suggestions ?? Array.Empty<string>());
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse LLM interpretation JSON, using raw text");
        }

        return (raw, Array.Empty<string>());
    }

    private sealed record InterpretationResult(string? Interpretation, string[]? Suggestions);
}
