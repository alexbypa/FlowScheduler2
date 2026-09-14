namespace FlowScheduler.WebApi.MinimalApi.RAG;

public record RagLibraryUpsertRequest(
    string Title,
    string Markdown,
    string Category,
    string? SubCategory,
    string? DocumentType);

public record RagOpsUpsertRequest(
    string Content,
    string Resolution,
    string Source,
    string Category,
    string? Title,
    string? Severity);

public record RagLibraryDocumentResponse(
    string Id,
    string Title,
    string Source,
    string Category,
    string SubCategory,
    string DocumentType,
    string Context,
    string Content,
    string Resolution,
    string Severity,
    string Markdown,
    DateTime CreatedAt);

public record RagLibraryListResponse(
    IReadOnlyList<RagLibraryDocumentResponse> Items,
    long TotalCount,
    int Page,
    int PageSize);

public record RagLibraryCategoryPairResponse(string Category, string SubCategory, string Context);

public record RagLibrarySaveResponse(string Id);
