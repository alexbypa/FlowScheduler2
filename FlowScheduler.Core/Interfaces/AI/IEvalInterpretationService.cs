namespace FlowScheduler.Core.Interfaces.AI;

public interface IEvalInterpretationService
{
    Task<(string Interpretation, string[] Suggestions)> InterpretAsync(
        float relevance, float truth, float completeness,
        float groundedness, float contextRelevance,
        string query, CancellationToken ct = default);
}
