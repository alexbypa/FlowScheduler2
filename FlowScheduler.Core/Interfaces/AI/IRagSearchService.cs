using System.Threading;
using FlowScheduler.Core.Models;

namespace FlowScheduler.Core.Interfaces.AI;

/// <summary>
/// Encapsulates the RAG (Retrieval-Augmented Generation) search workflow:
/// query preparation, embedding generation, vector search and context formatting.
/// </summary>
public interface IRagSearchService {
    Task<RagSearchResult> SearchAsync(string markdownErrors, string? category, CancellationToken cancellationToken = default);
    Task<RagSearchResult> SearchFreeAsync(string query,int topK = 3,string? contextFilter = null,  CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a RAG knowledge base search.
/// </summary>
/// <param name="FormattedContext">Pre-formatted RAG context ready for prompt injection. Empty if no match.</param>
/// <param name="HasBypassMatch">True if a high-confidence match was found (score &lt; 0.2), allowing AI bypass.</param>
/// <param name="BestMatch">The highest-scoring document, if any.</param>
/// <param name="BestMatchScore">Score of the best match (lower = more similar).</param>
public record RagSearchResult(
    string FormattedContext,
    bool HasBypassMatch,
    RagDocument? BestMatch,
    float? BestMatchScore
);
