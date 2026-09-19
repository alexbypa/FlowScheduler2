using System.Threading;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace FlowScheduler.Infrastructure.AI.RAG;

public class RagIngestionService : IRagIngestionService {
    /// <summary>Allineato al limite ~2048 token di gemini-embedding-001.</summary>
    private const int MaxEmbeddingSourceChars = 8_000;

    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IVectorStoreService _vectorStore;
    private readonly ILogger<RagIngestionService> _logger;

    public RagIngestionService(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, IVectorStoreService vectorStore, ILogger<RagIngestionService> logger) {
        _embeddingGenerator = embeddingGenerator;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    public async Task<string> IngestAsync(string content, string resolution, string source, string category, CancellationToken cancellationToken = default) {
        var id = Guid.NewGuid().ToString("N");
        var embedding = await _embeddingGenerator.GenerateVectorAsync(content);

        var doc = new RagDocument {
            Id = id,
            Context = VectorStoreConstants.ContextOps,
            Content = content,
            Resolution = resolution,
            Source = source,
            Title = "",
            Category = VectorStoreConstants.NormalizeTag(category, "general"),
            SubCategory = VectorStoreConstants.SubCategoryNoneTag,
            DocumentType = VectorStoreConstants.DocumentTypeDefault,
            Markdown = "",
            CreatedAt = DateTime.UtcNow,
            Embedding = embedding
        };

        await _vectorStore.StoreDocumentAsync(doc, cancellationToken);
        _logger.LogInformation("[RAG] Documento ingerito: {Id} | Fonte: {Source} | Categoria: {Category}", id, source, category);
        return id;
    }

    public async Task<List<string>> IngestBatchAsync(IEnumerable<(string Content, string Resolution, string Source, string Category)> documents, CancellationToken cancellationToken = default) {
        var docList = documents.ToList();
        var texts = docList.Select(d => d.Content).ToArray();
        var embeddings = await _embeddingGenerator.GenerateAsync(texts);

        var ids = new List<string>();
        for (int i = 0; i < docList.Count; i++) {
            var id = Guid.NewGuid().ToString("N");
            var doc = new RagDocument {
                Id = id,
                Context = VectorStoreConstants.ContextOps,
                Content = docList[i].Content,
                Resolution = docList[i].Resolution,
                Source = docList[i].Source,
                Title = "",
                Category = VectorStoreConstants.NormalizeTag(docList[i].Category, "general"),
                SubCategory = VectorStoreConstants.SubCategoryNoneTag,
                DocumentType = VectorStoreConstants.DocumentTypeDefault,
                Markdown = "",
                CreatedAt = DateTime.UtcNow,
                Embedding = embeddings[i].Vector
            };
            await _vectorStore.StoreDocumentAsync(doc, cancellationToken);
            ids.Add(id);
        }

        _logger.LogInformation("[RAG] Batch ingerito: {Count} documenti", ids.Count);
        return ids;
    }

    /// <summary>Crea un documento libreria: markdown completo, embedding da titolo + estratto markdown.</summary>
    public async Task<string> IngestLibraryDocumentAsync(
        string title,
        string markdown,
        string category,
        string? subCategory,
        string? documentType,
        CancellationToken cancellationToken = default) {
        var id = Guid.NewGuid().ToString("N");
        var cat = VectorStoreConstants.NormalizeTag(category, "general");
        var sub = string.IsNullOrWhiteSpace(subCategory)
            ? ""
            : VectorStoreConstants.NormalizeTag(subCategory, VectorStoreConstants.SubCategoryNoneTag);
        var dtype = VectorStoreConstants.NormalizeTag(documentType, VectorStoreConstants.DocumentTypeDefault);
        var sourceTag = VectorStoreConstants.NormalizeTag(title, "untitled");

        var embedText = BuildEmbeddingSourceText(title, markdown);
        var embedding = await _embeddingGenerator.GenerateVectorAsync(embedText);

        var doc = new RagDocument {
            Id = id,
            Context = VectorStoreConstants.ContextLibrary,
            Content = embedText,
            Resolution = "",
            Source = sourceTag,
            Title = title.Trim(),
            Category = cat,
            SubCategory = sub,
            DocumentType = dtype,
            Markdown = markdown ?? "",
            CreatedAt = DateTime.UtcNow,
            Embedding = embedding
        };

        await _vectorStore.StoreDocumentAsync(doc, cancellationToken);
        _logger.LogInformation("[RAG] Libreria: documento {Id} | {Title} | {Category}/{SubCategory}/{DocType}", id, title, cat, sub, dtype);
        return id;
    }

    public async Task<bool> UpdateLibraryDocumentAsync(
        string id,
        string title,
        string markdown,
        string category,
        string? subCategory,
        string? documentType,
        CancellationToken cancellationToken = default) {
        var existing = await _vectorStore.GetDocumentAsync(id, cancellationToken);
        if (existing is null) return false;
        if (!string.Equals(existing.Context, VectorStoreConstants.ContextLibrary, StringComparison.Ordinal)) {
            return false;
        }

        var cat = VectorStoreConstants.NormalizeTag(category, "general");
        var sub = string.IsNullOrWhiteSpace(subCategory)
            ? ""
            : VectorStoreConstants.NormalizeTag(subCategory, VectorStoreConstants.SubCategoryNoneTag);
        var dtype = VectorStoreConstants.NormalizeTag(documentType, VectorStoreConstants.DocumentTypeDefault);
        var sourceTag = VectorStoreConstants.NormalizeTag(title, "untitled");

        var embedText = BuildEmbeddingSourceText(title, markdown);
        var embeddingChanged = !string.Equals(existing.Content, embedText, StringComparison.Ordinal);

        existing.Content = embedText;
        existing.Resolution = "";
        existing.Source = sourceTag;
        existing.Title = title.Trim();
        existing.Category = cat;
        existing.SubCategory = sub;
        existing.DocumentType = dtype;
        existing.Markdown = markdown ?? "";

        if (embeddingChanged) {
            existing.Embedding = await _embeddingGenerator.GenerateVectorAsync(embedText);
        }

        return await _vectorStore.UpdateDocumentFieldsAsync(existing, updateEmbedding: embeddingChanged, cancellationToken);
    }

    public async Task<string> IngestOpsDocumentAsync(
        string source,
        string category,
        string messageTemplate,
        string resolution,
        string content,
        string? severity = null,
        CancellationToken cancellationToken = default) {
        var signature = BuildOpsEmbeddingText(source, category, messageTemplate);
        _logger.LogDebug("[INGEST DEBUG] Firma per embedding: '{Signature}'", signature);

        var embedContent = TruncateForEmbedding(signature);
        var embedding = await _embeddingGenerator.GenerateVectorAsync(embedContent);
        var id = Guid.NewGuid().ToString("N");

        _logger.LogDebug("[INGEST DEBUG] ID Generato: {Id} | Dimensione Vettore: {VectorSize}", id, embedding.Length);

        var doc = new RagDocument {
            Id = id,
            Context = VectorStoreConstants.ContextOps,
            Content = content,
            Resolution = resolution ?? "",
            Source = source.Trim(),
            Category = VectorStoreConstants.NormalizeTag(category, "general"),
            SubCategory = VectorStoreConstants.SubCategoryNoneTag,
            Severity = string.IsNullOrWhiteSpace(severity) ? "Error" : severity,
            CreatedAt = DateTime.UtcNow,
            Embedding = embedding
        };

        _logger.LogInformation("[RAG] Ops (UI): documento {Id} | {Source} | {Category} | Severity: {Severity} | {MessageTemplate}", id, source, category, doc.Severity, messageTemplate);
        
        await _vectorStore.StoreDocumentAsync(doc, cancellationToken);
        return id;
    }
    private string BuildOpsEmbeddingText(string source, string category, string messageTemplate) {
        return $"[OPS] SOURCE: {source.ToUpper()} | CATEGORY: {category.ToUpper()} | MSG: {messageTemplate}";
    }

    public async Task<bool> UpdateOpsDocumentAsync(
        string id,
        string content,
        string resolution,
        string source,
        string category,
        string? title,
        string? severity = null,
        CancellationToken cancellationToken = default) {
        var existing = await _vectorStore.GetDocumentAsync(id, cancellationToken);
        if (existing is null) return false;
        if (!string.Equals(existing.Context, VectorStoreConstants.ContextOps, StringComparison.Ordinal)) {
            return false;
        }

        var embedContent = TruncateForEmbedding(content);
        var embeddingChanged = !string.Equals(existing.Content, content, StringComparison.Ordinal);

        existing.Content = content;
        existing.Resolution = resolution ?? "";
        existing.Source = source.Trim();
        existing.Category = VectorStoreConstants.NormalizeTag(category, "general");
        existing.Severity = string.IsNullOrWhiteSpace(severity) ? existing.Severity : severity;
        if (title is not null) {
            existing.Title = title.Trim();
        }

        if (embeddingChanged) {
            existing.Embedding = await _embeddingGenerator.GenerateVectorAsync(embedContent);
        }

        return await _vectorStore.UpdateDocumentFieldsAsync(existing, updateEmbedding: embeddingChanged, cancellationToken);
    }

    private static string TruncateForEmbedding(string text) {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxEmbeddingSourceChars) {
            return text;
        }

        return text[..MaxEmbeddingSourceChars];
    }

    private static string BuildEmbeddingSourceText(string title, string markdown) {
        var body = markdown ?? "";
        if (body.Length > MaxEmbeddingSourceChars) {
            body = body[..MaxEmbeddingSourceChars];
        }

        return $"{title.Trim()}\n\n{body}";
    }
    public async Task<string> IngestMetricsDocumentAsync(string source, string category, string content, string? severity = null, CancellationToken cancellationToken = default) {
        var id = Guid.NewGuid().ToString("N");
        var embedding = await _embeddingGenerator.GenerateVectorAsync(content);

        var doc = new RagDocument {
            Id = id,
            // IMPORTANT: We use ContextMetrics instead of ContextOps to prevent massive ProjectPulse JSONs 
            // from flooding the vector search results of the JobDiagnosticAgent. 
            // This ensures ops search stays clean and avoids 429 Rate Limit errors.
            Context = VectorStoreConstants.ContextMetrics,
            Content = content,
            Resolution = "",
            Source = source,
            Title = "",
            Category = VectorStoreConstants.NormalizeTag(category, "general"),
            SubCategory = VectorStoreConstants.SubCategoryNoneTag,
            DocumentType = VectorStoreConstants.DocumentTypeDefault,
            Markdown = "",
            CreatedAt = DateTime.UtcNow,
            Embedding = embedding
        };

        await _vectorStore.StoreDocumentAsync(doc, cancellationToken);
        _logger.LogInformation("[RAG] Documento metric ingerito: {Id} | Fonte: {Source} | Categoria: {Category}", id, source, category);
        return id;
    }
}