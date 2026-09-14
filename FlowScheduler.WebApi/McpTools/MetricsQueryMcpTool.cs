using FlowScheduler.Core.Interfaces.Metrics;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace FlowScheduler.WebApi.McpTools;

[McpServerToolType]
public class MetricsQueryMcpTool {
    [McpServerTool(Name = "query_metrics", ReadOnly = true)]
    [Description("Query performance metrics by category and name within a time range. Returns timestamped values.")]
    public async Task<string> QueryMetricsAsync(
          IMetricsStore metricsStore,
          [Description("Metric category (e.g. 'job')")] string category,
          [Description("Metric name")] string name,
          [Description("Hours to look back from now (default 24)")] int hoursBack = 24) {
        hoursBack = Math.Clamp(hoursBack, 1, 168);
        var range = (from: DateTimeOffset.UtcNow.AddHours(-hoursBack), to: DateTimeOffset.UtcNow);
        var result = await metricsStore.QueryAsync(category, name, range.from, range.to);
        if (result.Count == 0) {
            return $"No metrics found for {category}/{name} in last {hoursBack}h";
        }
        return string.Join(Environment.NewLine, result.Select(entry => $"[{entry.Timestamp}] {entry.Name}: {entry.Value}"));
    }
}
