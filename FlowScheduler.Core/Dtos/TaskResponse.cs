namespace FlowScheduler.Core.Dtos;
public record TaskResponse(
    Guid Id,
    string Name,
    string CommandType,
    string CronExpression,
    bool IsEnabled,
    DateTime? LastExecution
);