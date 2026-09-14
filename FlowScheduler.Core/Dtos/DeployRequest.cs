namespace FlowScheduler.Core.Dtos;

public record DeployRequest(
    string ProjectPath,
    string DockerComposeFile,
    string[] HealthCheckUrls,
    int HealthCheckIntervalSeconds,
    int HealthCheckMaxRounds,
    string TelegramChatId
);
