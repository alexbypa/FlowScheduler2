using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Models;

namespace FlowScheduler.WebApi.MinimalApi.RAG;

public class RagLibraryEndpoints : IEndpointDefinition {
    public void DefineEndpoints(WebApplication app) {
        var group = app.MapGroup("/rag/library").WithTags("RAG Library");

        group.MapGet("/documents", async Task<IResult> (
                string? context,
                string? category,
                string? subCategory,
                string? docType,
                int? page,
                int? pageSize,
                IVectorStoreService vectorStore,
                CancellationToken cancellationToken) => {
            var p = Math.Max(0, page ?? 0);
            var size = pageSize ?? 20;
            var ctx = string.IsNullOrWhiteSpace(context) ? null : VectorStoreConstants.NormalizeContextTag(context);
            var cat = string.IsNullOrWhiteSpace(category) ? null : VectorStoreConstants.NormalizeTag(category, "general");
            var sub = string.IsNullOrWhiteSpace(subCategory) ? null : VectorStoreConstants.NormalizeTag(subCategory, VectorStoreConstants.SubCategoryNoneTag);
            var dtype = string.IsNullOrWhiteSpace(docType) ? null : VectorStoreConstants.NormalizeTag(docType, VectorStoreConstants.DocumentTypeDefault);

            var (items, total) = await vectorStore.SearchLibraryAsync(ctx, cat, sub, dtype, p, size, cancellationToken);
            var dto = items.Select(ToResponse).ToList();
            return Results.Ok(new RagLibraryListResponse(dto, total, p, size));
        })
        .WithName("RagLibraryListDocuments");

        group.MapGet("/documents/{id}", async Task<IResult> (string id, IVectorStoreService vectorStore) => {
            var doc = await vectorStore.GetDocumentAsync(id);
            return doc is null ? Results.NotFound() : Results.Ok(ToResponse(doc));
        })
        .WithName("RagLibraryGetDocument");

        group.MapPost("/documents", async Task<IResult> (RagLibraryUpsertRequest request, IRagIngestionService ingestion) => {
            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Category)) {
                return Results.BadRequest("Title e Category sono obbligatori.");
            }

            try {
                var id = await ingestion.IngestLibraryDocumentAsync(
                    request.Title.Trim(),
                    request.Markdown ?? "",
                    request.Category,
                    request.SubCategory,
                    request.DocumentType);
                return Results.Created($"/rag/library/documents/{id}", new RagLibrarySaveResponse(id));
            } catch (Exception ex) {
                return EmbeddingFailureResult(ex, "Errore durante la creazione del documento");
            }
        })
        .WithName("RagLibraryCreateDocument");

        group.MapPost("/documents/ops", async Task<IResult> (RagOpsUpsertRequest request, IRagIngestionService ingestion) => {
            if (string.IsNullOrWhiteSpace(request.Content) || string.IsNullOrWhiteSpace(request.Category)) {
                return Results.BadRequest("Content e Category sono obbligatori.");
            }

            if (string.IsNullOrWhiteSpace(request.Source)) {
                return Results.BadRequest("Source è obbligatorio.");
            }

            try {
                var id = await ingestion.IngestOpsDocumentAsync(
                    request.Content.Trim(),
                    request.Resolution ?? "",
                    request.Source.Trim(),
                    request.Category,
                    request.Title,
                    request.Severity);
                return Results.Created($"/rag/library/documents/{id}", new RagLibrarySaveResponse(id));
            } catch (Exception ex) {
                return EmbeddingFailureResult(ex, "Errore durante la creazione del documento operativo");
            }
        })
        .WithName("RagLibraryCreateOpsDocument");

        group.MapPut("/documents/{id}", async Task<IResult> (
                string id,
                RagLibraryUpsertRequest request,
                IRagIngestionService ingestion,
                IVectorStoreService vectorStore) => {
            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Category)) {
                return Results.BadRequest("Title e Category sono obbligatori.");
            }

            var existing = await vectorStore.GetDocumentAsync(id);
            if (existing is null) {
                return Results.NotFound();
            }

            if (!string.Equals(existing.Context, VectorStoreConstants.ContextLibrary, StringComparison.Ordinal)) {
                return Results.Conflict("Documento ops: usa PUT /rag/library/documents/{id}/ops");
            }

            try {
                var ok = await ingestion.UpdateLibraryDocumentAsync(
                    id,
                    request.Title.Trim(),
                    request.Markdown ?? "",
                    request.Category,
                    request.SubCategory,
                    request.DocumentType);
                return ok ? Results.Ok(new RagLibrarySaveResponse(id)) : Results.NotFound();
            } catch (Exception ex) {
                return EmbeddingFailureResult(ex, "Errore durante l'aggiornamento del documento");
            }
        })
        .WithName("RagLibraryUpdateDocument");

        group.MapPut("/documents/{id}/ops", async Task<IResult> (
                string id,
                RagOpsUpsertRequest request,
                IRagIngestionService ingestion,
                IVectorStoreService vectorStore) => {
            if (string.IsNullOrWhiteSpace(request.Content) || string.IsNullOrWhiteSpace(request.Category)) {
                return Results.BadRequest("Content e Category sono obbligatori.");
            }

            if (string.IsNullOrWhiteSpace(request.Source)) {
                return Results.BadRequest("Source è obbligatorio.");
            }

            var existing = await vectorStore.GetDocumentAsync(id);
            if (existing is null) {
                return Results.NotFound();
            }

            if (!string.Equals(existing.Context, VectorStoreConstants.ContextOps, StringComparison.Ordinal)) {
                return Results.Conflict("Documento library: usa PUT /rag/library/documents/{id}");
            }

            try {
                var ok = await ingestion.UpdateOpsDocumentAsync(
                    id,
                    request.Content.Trim(),
                    request.Resolution ?? "",
                    request.Source.Trim(),
                    request.Category,
                    request.Title,
                    request.Severity);
                return ok ? Results.Ok(new RagLibrarySaveResponse(id)) : Results.NotFound();
            } catch (Exception ex) {
                return EmbeddingFailureResult(ex, "Errore durante l'aggiornamento del documento operativo");
            }
        })
        .WithName("RagLibraryUpdateOpsDocument");

        group.MapGet("/categories", async Task<IResult> (string? context, int? maxDocs, IVectorStoreService vectorStore, CancellationToken cancellationToken) => {
            var ctx = string.IsNullOrWhiteSpace(context) ? null : VectorStoreConstants.NormalizeContextTag(context);
            var pairs = await vectorStore.ListLibraryCategoryPairsAsync(ctx, maxDocs ?? 2000, cancellationToken);
            var dto = pairs.Select(p => new RagLibraryCategoryPairResponse(p.Category, p.SubCategory, p.Context)).ToList();
            return Results.Ok(dto);
        })
        .WithName("RagLibraryListCategories");
    }

    private static IResult EmbeddingFailureResult(Exception ex, string title) {
        var detail = ex.InnerException?.Message ?? ex.Message;
        if (string.IsNullOrWhiteSpace(detail)) {
            detail = "Errore durante la generazione dell'embedding (API Gemini). Controlla AiSettings e la lunghezza del markdown.";
        }

        return Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status502BadGateway,
            title: title);
    }

    private static RagLibraryDocumentResponse ToResponse(RagDocument d) => new(
        d.Id,
        string.IsNullOrWhiteSpace(d.Title) ? d.Source : d.Title,
        d.Source,
        d.Category,
        d.SubCategory,
        d.DocumentType,
        d.Context,
        d.Content,
        d.Resolution,
        d.Severity,
        d.Markdown,
        d.CreatedAt);
}
