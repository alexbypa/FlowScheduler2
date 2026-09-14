using System.Threading;
using FlowScheduler.Core.Dtos;

namespace FlowScheduler.Core.Interfaces.AI;

public interface IJobDiagnosticAgent {
    Task<DiagnosticResult> AnalyzeAsync(
        string errorsMarkdown,
        CreateTaskRequest taskRequest,
        string correlationId,
        string? errorCategory = null,
        string? preSummarizeText = null,
        CancellationToken cancellationToken = default);
}
