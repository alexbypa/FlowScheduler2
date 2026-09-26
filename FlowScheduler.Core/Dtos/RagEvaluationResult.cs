namespace FlowScheduler.Core.Dtos;

public record RagEvaluationResult(
    float Relevance,
    float Truth,
    float Completeness,
    float Groundedness,
    float ContextRelevance,
    string Interpretation,
    string[] Suggestions,
    DateTimeOffset Timestamp,
    string Query,
    bool EvaluationFailed
);
