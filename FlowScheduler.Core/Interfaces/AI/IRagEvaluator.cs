using FlowScheduler.Core.Dtos;

namespace FlowScheduler.Core.Interfaces.AI;

public interface IRagEvaluator {
    Task<RagEvaluationResult> EvaluateAsync(string query, string ragContext, string modelResponse, CancellationToken ct = default);
    Task<List<RagEvaluationResult>> GetRecentResultsAsync(int count = 10);
}
