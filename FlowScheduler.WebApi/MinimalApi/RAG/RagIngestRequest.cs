namespace FlowScheduler.WebApi.MinimalApi.RAG;

public record RagIngestRequest(string Content, string Resolution, string Source, string Category, string MessageTemplate);
public record RagIngestBatchRequest(List<RagIngestRequest> Documents);
public record RagSearchRequest(string Query, int TopK = 3, string? Context = null);
