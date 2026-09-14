namespace FlowScheduler.Core.Dtos;

public enum DiagnosticSeverity {
    Information,
    Warning,
    Error,
    Fatal
}

public record DiagnosticResult(
    string Message,
    DiagnosticSeverity Severity,
    string CorrelationId,
    string OriginalErrorJson,
    string TaskName,
    bool IsAiGenerated
);
