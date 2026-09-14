namespace FlowScheduler.Core.Configuration;

public sealed class HealthMonitorOptions {
    public bool Enabled { get; set; } = true;
    public string CronExpression { get; set; } = "0 */6 * * *";
    public double AlertThreshold { get; set; } = 50.0;
    public string TelegramChatId { get; set; } = "";
    public string McpServerName { get; set; } = "projectpulse";
    public int DoraMetricsDays { get; set; } = 30;
    public IList<MonitoredRepo> Repositories { get; set; } = [];
    public bool EnableRagIngestion { get; set; } = true;
    public bool EnableAiTelegramReport { get; set; } = true;
}

public sealed class MonitoredRepo {
    public string Owner { get; set; }
    public string Repo { get; set; }
}