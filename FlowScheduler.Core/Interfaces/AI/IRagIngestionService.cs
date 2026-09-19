using System.Threading;

namespace FlowScheduler.Core.Interfaces.AI;

/// <summary>
/// Contratto per l'ingestione di documenti nel Vector Store RAG.
/// Supporta documenti operativi (ops), libreria e batch.
/// </summary>
public interface IRagIngestionService {
    Task<string> IngestAsync(string content, string resolution, string source, string category, CancellationToken cancellationToken = default);

    Task<List<string>> IngestBatchAsync(IEnumerable<(string Content, string Resolution, string Source, string Category)> documents, CancellationToken cancellationToken = default);

    Task<string> IngestLibraryDocumentAsync(string title, string markdown, string category, string? subCategory, string? documentType, CancellationToken cancellationToken = default);

    Task<bool> UpdateLibraryDocumentAsync(string id, string title, string markdown, string category, string? subCategory, string? documentType, CancellationToken cancellationToken = default);

    Task<string> IngestOpsDocumentAsync(string source, string category, string messageTemplate, string resolution, string content, string? severity = null, CancellationToken cancellationToken = default);

    Task<bool> UpdateOpsDocumentAsync(string id, string content, string resolution, string source, string category, string? title, string? severity = null, CancellationToken cancellationToken = default);
    Task<string> IngestMetricsDocumentAsync(string source, string category, string content, string? severity = null, CancellationToken cancellationToken = default);
}
