using System.Threading;
using FlowScheduler.Core.Models;

namespace FlowScheduler.Core.Interfaces.AI;

public interface IVectorStoreService {
    Task StoreDocumentAsync(RagDocument doc, CancellationToken cancellationToken = default);
    Task<RagDocument?> GetDocumentAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> UpdateDocumentFieldsAsync(RagDocument doc, bool updateEmbedding, CancellationToken cancellationToken = default);
    Task<List<(RagDocument Doc, float Score)>> SearchSimilarAsync(
        ReadOnlyMemory<float> queryEmbedding, int topK = 3, float scoreThreshold = 0.40f, string? contextFilter = null, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<RagDocument> Items, long TotalCount)> SearchLibraryAsync(
        string? context, string? category, string? subCategory, string? docType, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(string Category, string SubCategory, string Context)>> ListLibraryCategoryPairsAsync(
        string? context, int maxDocs = 2000, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentAsync(string id, CancellationToken cancellationToken = default);
}