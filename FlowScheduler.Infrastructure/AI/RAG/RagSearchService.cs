using System.Text;
using System.Threading;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace FlowScheduler.Infrastructure.AI.RAG;

/// <summary>
/// Implements the RAG search workflow: query preparation → embedding → vector search → context formatting.
/// </summary>
public class RagSearchService : IRagSearchService {
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IVectorStoreService _vectorStore;
    private readonly ILogger<RagSearchService> _logger;

    private const int MaxQueryLength = 500;
    private const int TopK = 3;
    private const float ScoreThreshold = 0.45f;
    private const float BypassScoreThreshold = 0.2f;

    public RagSearchService(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IVectorStoreService vectorStore,
        ILogger<RagSearchService> logger) {
        _embeddingGenerator = embeddingGenerator;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    public async Task<RagSearchResult> SearchAsync(string markdownErrors, string? category, CancellationToken cancellationToken = default) {
        _logger.LogInformation("[AI] [AI RAG] Ricerca semantica nella knowledge base...");

        string ragQuery = PrepareQuery(markdownErrors, category);
        _logger.LogInformation("[AI] [AI RAG] Query preparata SIZE: [{QuerySize}] : {RagQuery}", ragQuery.Length, ragQuery);

        var queryEmbedding = await _embeddingGenerator.GenerateVectorAsync(ragQuery);
        _logger.LogInformation("[AI] [AI RAG] Embedding generato...");

        var results = await _vectorStore.SearchSimilarAsync(
            queryEmbedding, topK: TopK, scoreThreshold: ScoreThreshold, contextFilter: VectorStoreConstants.ContextOps, cancellationToken: cancellationToken);

        string formattedContext = FormatContext(results);
        _logger.LogInformation("[AI] [AI RAG] Contesto trovato: {RagStatus}", string.IsNullOrEmpty(formattedContext) ? "NESSUNO" : "SI");

        var bestMatch = results.FirstOrDefault(r => r.Score < BypassScoreThreshold);

        return new RagSearchResult(
            FormattedContext: formattedContext,
            HasBypassMatch: bestMatch.Doc != null && !string.IsNullOrEmpty(formattedContext),
            BestMatch: bestMatch.Doc,
            BestMatchScore: bestMatch.Doc != null ? bestMatch.Score : null
        );
    }

    private static string PrepareQuery(string markdownErrors, string? category) {
        string truncated = markdownErrors.Length > MaxQueryLength
            ? markdownErrors[..MaxQueryLength]
            : markdownErrors;

        return !string.IsNullOrEmpty(category)
            ? $"{category}\n{truncated}"
            : truncated;
    }

    private static string FormatContext(List<(RagDocument Doc, float Score)> results) {
        if (results.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var (doc, score) in results) {
            sb.AppendLine($"- [Soluzione {1 - score:P0}] {doc.Content}");
            if (!string.IsNullOrEmpty(doc.Resolution))
                sb.AppendLine($"  Fix: {doc.Resolution}");
        }
        return sb.ToString();
    }

    public async Task<RagSearchResult> SearchFreeAsync(string query, int topK = 3, string? contextFilter = null, CancellationToken cancellationToken = default) {
        _logger.LogInformation("[AI] [AI RAG] Ricerca libera nella knowledge base...");

        topK = Math.Clamp(topK, 1, 10);

        string truncatedQuery = query.Length > MaxQueryLength ? query[..MaxQueryLength] : query;

        var queryEmbedding = await _embeddingGenerator.GenerateVectorAsync(truncatedQuery);
        _logger.LogInformation("[AI] [AI RAG] Embedding generato...");

        var results = await _vectorStore.SearchSimilarAsync(
            queryEmbedding, 
            topK: topK, scoreThreshold: ScoreThreshold, contextFilter: contextFilter, cancellationToken: cancellationToken
            );
        
        if (results == null || results.Count == 0) {
            _logger.LogInformation("[AI] [AI RAG] Nessun risultato trovato.");
            return new RagSearchResult(
                FormattedContext: string.Empty,
                HasBypassMatch: false,
                BestMatch: null,
                BestMatchScore: null
            );
        }

        string formattedContext = FormatContext(results);
        _logger.LogInformation("[AI] [AI RAG] Contesto trovato: {RagStatus}", string.IsNullOrEmpty(formattedContext) ? "NESSUNO" : "SI");


        var best = results.FirstOrDefault();
        return new RagSearchResult(
            FormattedContext: formattedContext,
            HasBypassMatch: results.Any(r => r.Score < BypassScoreThreshold),
            BestMatch: best.Doc,
            BestMatchScore: best.Doc != null ? best.Score : null
        );
    }
}
