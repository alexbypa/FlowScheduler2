#pragma warning disable AIEVAL001 // Experimental evaluator API
using System.Diagnostics;
using System.Text.Json;
using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FlowScheduler.Infrastructure.AI.Evaluation;

public class RagEvaluationService : IRagEvaluator {
    private readonly IChatClient _chatClient;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RagEvaluationService> _logger;
    private readonly int _maxParallelism;

    private static readonly TimeSpan ResultTtl = TimeSpan.FromDays(7);

    public RagEvaluationService(IChatClient chatClient, IConnectionMultiplexer redis, HangFireOptions options, ILogger<RagEvaluationService> logger) {
        _chatClient = chatClient;
        _redis = redis;
        _logger = logger;
        _maxParallelism = options.OpenAI.EvalMaxParallelism;
    }

    public async Task<RagEvaluationResult> EvaluateAsync(string query, string ragContext, string modelResponse, CancellationToken ct = default) {
        try {
            var sw = Stopwatch.StartNew();
            var chatConfiguration = new ChatConfiguration(_chatClient);

            var messages = new List<ChatMessage> {
                new(ChatRole.User, $"{query}\n\nContext:\n{ragContext}")
            };
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, modelResponse));

            var contextChunks = ragContext.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
            EvaluationResult rtcResult, groundednessResult, retrievalResult;

            if (_maxParallelism >= 3) {
                // Parallel: 3 LLM calls at once (use with high-TPM providers)
                var rtcTask = new RelevanceTruthAndCompletenessEvaluator()
                    .EvaluateAsync(messages, response, chatConfiguration, cancellationToken: ct).AsTask();
                var gTask = new GroundednessEvaluator()
                    .EvaluateAsync(messages, response, chatConfiguration,
                        [new GroundednessEvaluatorContext(ragContext)], ct).AsTask();
                var crTask = new RetrievalEvaluator()
                    .EvaluateAsync(messages, response, chatConfiguration,
                        [new RetrievalEvaluatorContext(contextChunks)], ct).AsTask();

                await Task.WhenAll(rtcTask, gTask, crTask);
                rtcResult = rtcTask.Result;
                groundednessResult = gTask.Result;
                retrievalResult = crTask.Result;
            } else {
                // Sequential: avoids rate limiting on free-tier providers (e.g. Groq 8K TPM)
                rtcResult = await new RelevanceTruthAndCompletenessEvaluator()
                    .EvaluateAsync(messages, response, chatConfiguration, cancellationToken: ct);
                groundednessResult = await new GroundednessEvaluator()
                    .EvaluateAsync(messages, response, chatConfiguration,
                        [new GroundednessEvaluatorContext(ragContext)], ct);
                retrievalResult = await new RetrievalEvaluator()
                    .EvaluateAsync(messages, response, chatConfiguration,
                        [new RetrievalEvaluatorContext(contextChunks)], ct);
            }

            var relevance = rtcResult.Get<NumericMetric>("Relevance (RTC)");
            var truth = rtcResult.Get<NumericMetric>("Truth (RTC)");
            var completeness = rtcResult.Get<NumericMetric>("Completeness (RTC)");
            var groundednessMetric = groundednessResult.Get<NumericMetric>(GroundednessEvaluator.GroundednessMetricName);
            var retrievalMetric = retrievalResult.Get<NumericMetric>(RetrievalEvaluator.RetrievalMetricName);

            sw.Stop();

            float r = (float)(relevance.Value ?? -1);
            float t = (float)(truth.Value ?? -1);
            float c = (float)(completeness.Value ?? -1);
            float g = (float)(groundednessMetric.Value ?? -1);
            float cr = (float)(retrievalMetric.Value ?? -1);
            bool failed = r < 0 || t < 0 || c < 0 || g < 0 || cr < 0;

            _logger.LogInformation(
                "[AI] [EVAL] {Query} → R:{R:F1} T:{T:F1} C:{C:F1} G:{G:F1} CR:{CR:F1} ({ElapsedMs}ms)",
                query, r, t, c, g, cr, sw.ElapsedMilliseconds);

            var evalResult = new RagEvaluationResult(
                Relevance: r,
                Truth: t,
                Completeness: c,
                Groundedness: g,
                ContextRelevance: cr,
                Interpretation: failed ? "evaluation_failed" : string.Empty,
                Suggestions: [],
                Timestamp: DateTimeOffset.UtcNow,
                Query: query,
                EvaluationFailed: failed);

            await PersistResultAsync(evalResult);
            return evalResult;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "[AI] [EVAL] Evaluation failed for query: {Query}", query);

            return new RagEvaluationResult(
                Relevance: -1,
                Truth: -1,
                Completeness: -1,
                Groundedness: -1,
                ContextRelevance: -1,
                Interpretation: "evaluation_failed",
                Suggestions: [],
                Timestamp: DateTimeOffset.UtcNow,
                Query: query,
                EvaluationFailed: true);
        }
    }

    public async Task<List<RagEvaluationResult>> GetRecentResultsAsync(int count = 10) {
        var db = _redis.GetDatabase();
        var server = _redis.GetServers().First();
        var keys = server.Keys(pattern: "eval:result:*").Take(count * 2).ToList();

        var results = new List<(DateTimeOffset Timestamp, RagEvaluationResult Result)>();
        foreach (var key in keys) {
            var json = await db.StringGetAsync(key);
            if (json.HasValue) {
                var result = JsonSerializer.Deserialize<RagEvaluationResult>((string)json!);
                if (result is not null)
                    results.Add((result.Timestamp, result));
            }
        }

        return results
            .OrderByDescending(r => r.Timestamp)
            .Take(count)
            .Select(r => r.Result)
            .ToList();
    }

    private async Task PersistResultAsync(RagEvaluationResult result) {
        try {
            var db = _redis.GetDatabase();
            var key = $"eval:result:{Guid.NewGuid():N}";
            var json = JsonSerializer.Serialize(result);
            await db.StringSetAsync(key, json, ResultTtl);
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "[AI] [EVAL] Failed to persist eval result to Redis");
        }
    }
}
