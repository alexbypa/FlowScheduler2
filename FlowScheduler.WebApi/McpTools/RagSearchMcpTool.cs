using FlowScheduler.Core.Interfaces.AI;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace FlowScheduler.WebApi.McpTools;

/// <summary>
/// This class defines a server tool for performing RAG (Retrieval-Augmented Generation) searches using the IRagSearchService.
/// </summary>
[McpServerToolType]
public class RagSearchMcpTool {
    /// <summary>
    /// Searches for knowledge using the provided query and returns the results formatted as a string.
    /// </summary>
    /// <param name="ragSearchService"></param>
    /// <param name="query"></param>
    /// <param name="topK"></param>
    /// <returns></returns>
    [McpServerTool(Name = "search_knowledge", ReadOnly = true)]
    public async Task<string> SearchKnowledgeAsync(
    IRagSearchService ragSearchService,
    [Description("The search query text")] string query,
    [Description("Maximum number of results (1-10, default 3)")] int topK = 3) {
        topK = Math.Max(1, Math.Min(10, topK));
        var results = await ragSearchService.SearchFreeAsync(query, topK);
        if (string.IsNullOrEmpty(results.FormattedContext)) {
            return $"No results found for query: {query}";
        } else {
            return results.FormattedContext;
        }
    }
}

