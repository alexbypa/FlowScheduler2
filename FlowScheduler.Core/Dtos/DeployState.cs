namespace FlowScheduler.Core.Dtos;

public record DeployState(
    string DeployId,
    string BranchName,
    string ProjectPath,
    string TelegramChatId,
    DeployStatus Status,
    int HealthChecksPassed,
    int HealthChecksTotal,
    string? FailureReason,
    DateTime CreatedAt
);

public enum DeployStatus {
    Pending,
    Building,
    HealthChecking,
    WaitingApproval,
    Approved,
    RolledBack,
    Failed
}
