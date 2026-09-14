using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.AI;
using FlowScheduler.Core.Models;
namespace FlowScheduler.WebApi.MinimalApi.RAG;

public class RagEndpoints : IEndpointDefinition {
    public record RagConfirmRequest(string CorrelationId, string TaskName, string ErrorsJson, string AiSolution);
    public void DefineEndpoints(WebApplication app) {

        app.MapPost("/rag/ingest", async (RagIngestRequest request, IRagIngestionService ingestionService) => {
            var id = await ingestionService.IngestOpsDocumentAsync(request.Source, request.Category, request.MessageTemplate, request.Resolution, request.Content);
            return Results.Created($"/rag/documents/{id}", new { Id = id });
        })
        .WithName("RagIngest")
        .WithTags("RAG");

        app.MapPost("/rag/confirm-solution", async (RagConfirmRequest request, IRagIngestionService ingestionService) => {

            var extractedTemplates = new System.Collections.Generic.HashSet<string>();
            try {
                using var document = System.Text.Json.JsonDocument.Parse(request.ErrorsJson);
                if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array) {
                    foreach (var element in document.RootElement.EnumerateArray()) {
                        if (element.TryGetProperty("MessageTemplate", out var templateProp) && templateProp.ValueKind == System.Text.Json.JsonValueKind.String) {
                            var val = templateProp.GetString();
                            if (!string.IsNullOrWhiteSpace(val))
                                extractedTemplates.Add(val);
                        }
                    }
                } else if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object) {
                    if (document.RootElement.TryGetProperty("MessageTemplate", out var templateProp) && templateProp.ValueKind == System.Text.Json.JsonValueKind.String) {
                        var val = templateProp.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            extractedTemplates.Add(val);
                    }
                }
            } catch { } // Fallback silenzioso se non è un JSON formattato male o generico

            string errorRepresentation = extractedTemplates.Any()
                ? $"Templates: {string.Join(" | ", extractedTemplates)}"
                : $"Errore Originale: {request.ErrorsJson}";

            // Formattiamo il documento col template purificato per massimizzare la precisione dell'Embedding
            string structuredContent = $"""
        Task: {request.TaskName}
        {errorRepresentation}
        """;

            // Riutilizziamo il servizio esistente impostando una categoria specifica
            var id = await ingestionService.IngestAsync(
                structuredContent,
                resolution: request.AiSolution,
                source: $"AI-Validation-{request.CorrelationId}",
                category: "VerifiedSolution"
            );

            return Results.Ok(new { Id = id, Status = "Learned" });
        })
        .WithName("RagConfirmSolution")
        .WithTags("RAG");

        app.MapPost("/rag/ingest/batch", async (RagIngestBatchRequest request, IRagIngestionService ingestionService) => {
            var docs = request.Documents.Select(d => (d.Content, d.Resolution, d.Source, d.Category));
            var ids = await ingestionService.IngestBatchAsync(docs);
            return Results.Created("/rag/documents", new { Ids = ids, Count = ids.Count });
        })
        .WithName("RagIngestBatch")
        .WithTags("RAG");

        app.MapPost("/rag/search", async (RagSearchRequest request, IEmbeddingGenerator<string, Embedding<float>> embeddingService, IVectorStoreService vectorStore, ILogger<RagEndpoints> logger) => {
            try {
                var embedding = await embeddingService.GenerateVectorAsync(request.Query);
                var contextFilter = string.IsNullOrWhiteSpace(request.Context) ? null : VectorStoreConstants.NormalizeContextTag(request.Context);
                var results = await vectorStore.SearchSimilarAsync(embedding, request.TopK, contextFilter: contextFilter);
                return Results.Ok(results.Select(r => new {
                    r.Doc.Id,
                    r.Doc.Content,
                    r.Doc.Source,
                    r.Doc.Category,
                    r.Doc.Context,
                    r.Doc.CreatedAt,
                    Relevance = 1 - r.Score
                }));
            } catch (Exception ex) {
                logger.LogError(ex, "[CRASH SEARCH API] Errore: {Error}", ex.Message);
                return Results.Problem($"Errore durante la ricerca RAG: {ex.Message}");
            }
        })
        .WithName("RagSearch")
        .WithTags("RAG");

        app.MapPost("/rag/debug", async (RagSearchRequest request, IEmbeddingGenerator<string, Embedding<float>> embeddingService, IVectorStoreService vectorStore) => {
            // 1. Generiamo l'embedding della query (come nella ricerca normale)
            var embedding = await embeddingService.GenerateVectorAsync(request.Query);

            // 2. Chiamiamo una versione "speciale" della ricerca che NON ha la soglia (threshold)
            // Dobbiamo assicurarci che SearchSimilarAsync nel servizio non scarti nulla se passiamo soglia 1.0
            var results = await vectorStore.SearchSimilarAsync(embedding, topK: 10, scoreThreshold: 1.0f, contextFilter: request.Context);

            return Results.Ok(results.Select(r => new {
                r.Doc.Id,
                r.Doc.Source,
                r.Doc.Category,
                r.Doc.Context,
                r.Doc.Content,
                RawScore = r.Score,
                Similarity = 1 - r.Score,
                r.Doc.Resolution,
                HasVector = r.Doc.Embedding.Length > 0
            }));
        })
  .WithName("RagDebug")
  .WithTags("RAG");

        app.MapDelete("/rag/documents/{id}", async (string id, IVectorStoreService vectorStore) => {
            var deleted = await vectorStore.DeleteDocumentAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("RagDeleteDocument")
        .WithTags("RAG");
    }
}
