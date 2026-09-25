using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Interfaces.Metrics;
using FlowScheduler.Core.Models;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;

namespace FlowScheduler.Infrastructure.AI.McpServerTools;

[McpServerToolType]
public class HealthReportMcpTool {
    private static readonly string[] MetricNames = [
        "health",
        "dora_deployment_frequency",
        "dora_lead_time",
        "dora_change_failure_rate",
        "dora_mttr",
        "ci_runs",
        "deps_alerts",
        "code_scanning_alerts"
    ];

    [McpServerTool(Name = "view_health_report", ReadOnly = true)]
    [Description("View stored health report for a GitHub repository from historical metrics data. Requires the health monitor job to have run at least once.")]
    public async Task<string> ViewHealthReportAsync(
        IMetricsStore metricsStore,
        IRagBridgeService ragBridgeService,
        [Description("GitHub repository owner")] string owner,
        [Description("GitHub repository name")] string repo,
        [Description("Hours to look back (default 24)")] int hoursBack = 24) {

        hoursBack = Math.Clamp(hoursBack, 1, 168);
        var from = DateTimeOffset.UtcNow.AddHours(-hoursBack);
        var to = DateTimeOffset.UtcNow;

        var sb = new StringBuilder();
        var hasData = false;

        foreach (var metric in MetricNames) {
            var key = $"{metric}_{repo}";
            var entries = await metricsStore.QueryAsync(MetricsConstants.CategoryDevops, key, from, to);
            if (entries.Count > 0) {
                hasData = true;
                var latest = entries[^1];
                sb.AppendLine($"{metric}: {latest.Value:F2} (at {latest.Timestamp:u})");
            }
        }

        if (!hasData) {
            return $"No health data found for {owner}/{repo} in the last {hoursBack}h. Run the health monitor job first.";
        }

        var data = new Core.Dtos.HealthCheckResult {
            HealthJson = sb.ToString()
        };
        return await ragBridgeService.FormatHealthReportAsync(data, owner, repo);
    }
}
