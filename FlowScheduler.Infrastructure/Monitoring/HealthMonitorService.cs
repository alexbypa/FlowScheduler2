using System.Text.Json;
using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Interfaces.MCP;
using FlowScheduler.Core.Interfaces.Messaging;
using FlowScheduler.Core.Interfaces.Metrics;
using FlowScheduler.Core.Interfaces.Monitoring;
using FlowScheduler.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowScheduler.Infrastructure.Monitoring;

public sealed class HealthMonitorService : IHealthMonitorService {
    private readonly IMcpClientService _mcpClient;
    private readonly IMetricsStore _metricsStore;
    private readonly ITelegramService _telegram;
    private readonly HealthMonitorOptions _options;
    private readonly ILogger<HealthMonitorService> _logger;
    private readonly IRagBridgeService _ragBridgeService;

    /// <summary>
    /// Maps MCP response property name → sub-field containing the numeric value.
    /// </summary>
    private static readonly Dictionary<string, string> DoraMetricFields = new() {
        ["deployment_frequency"] = "per_week",
        ["lead_time"] = "median_hours",
        ["change_failure_rate"] = "rate_percent",
        ["mttr"] = "median_hours"
    };

    public HealthMonitorService(
        IMcpClientService mcpClient,
        IMetricsStore metricsStore,
        ITelegramService telegram,
        IOptions<HealthMonitorOptions> options,
        ILogger<HealthMonitorService> logger,
        IRagBridgeService ragBridgeService
        ) {
        _mcpClient = mcpClient;
        _metricsStore = metricsStore;
        _telegram = telegram;
        _options = options.Value;
        _logger = logger;
        _ragBridgeService = ragBridgeService;
    }

    public async Task CheckAllAsync(CancellationToken ct = default) {
        if (!_options.Enabled) {
            _logger.LogInformation("Health monitoring disabled, skipping");
            return;
        }

        _logger.LogInformation("Starting health check for {Count} repositories",
            _options.Repositories.Count);

        var tasks = _options.Repositories.Select(repo => CheckRepoAsync(repo, ct));
        await Task.WhenAll(tasks);
    }
    /// <summary>
    /// Chiama un tool MCP con error isolation — un fallimento ritorna null invece di propagare,
    /// cosi' gli altri tool raccolti in parallelo da CollectAllDataAsync non vengono scartati.
    /// </summary>
    private async Task<string?> FetchToolProjectpulseJsonAsync(MonitoredRepo repo, string toolName, CancellationToken ct) {
        try {
            return await _mcpClient.CallToolAsync(_options.McpServerName, toolName,
                new Dictionary<string, object?> { ["owner"] = repo.Owner, ["repo"] = repo.Repo }, ct);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "[Collect] Tool '{Tool}' failed for {Owner}/{Repo}, continuing", toolName, repo.Owner, repo.Repo);
            return null;
        }
    }
    private async Task<HealthCheckResult> CollectAllDataAsync(MonitoredRepo repo, CancellationToken ct) {
        var healthTask = FetchToolProjectpulseJsonAsync(repo, "get_health_score", ct);
        var doraTask = FetchToolProjectpulseJsonAsync(repo, "get_dora_metrics", ct);
        var ciTask = FetchToolProjectpulseJsonAsync(repo, "check_ci_status", ct);
        var depsTask = FetchToolProjectpulseJsonAsync(repo, "analyze_dependencies", ct);
        var scanTask = FetchToolProjectpulseJsonAsync(repo, "analyze_code_scanning", ct);

        await Task.WhenAll(healthTask, doraTask, ciTask, depsTask, scanTask);

        return new HealthCheckResult {
            HealthJson = await healthTask,
            DoraJson = await doraTask,
            CiJson = await ciTask,
            DependenciesJson = await depsTask,
            CodeScanningJson = await scanTask
        };
    }

    private async Task CheckRepoAsync(MonitoredRepo repo, CancellationToken ct) {
        try {
            var healthData = await CollectAllDataAsync(repo, ct);

            await ParseAndStoreMetricsAsync(healthData, repo, ct);

            if (_options.EnableRagIngestion) {
                await _ragBridgeService.IngestHealthReportAsync(healthData, repo.Owner, repo.Repo, ct);
            }

            if (_options.EnableAiTelegramReport) {
                var responseForTelegram = await _ragBridgeService.FormatHealthReportAsync(healthData, repo.Owner, repo.Repo, ct);
                await _telegram.SendMessageAsync(_options.TelegramChatId, responseForTelegram, ct);
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "Health check failed for {Owner}/{Repo}, continuing. Error: {Message}",
                repo.Owner, repo.Repo, ex.Message);
        }
    }

    private async Task ParseAndStoreMetricsAsync(HealthCheckResult data, MonitoredRepo repo, CancellationToken ct) {
        await Task.WhenAll(
            ParseHealthScoreAsync(data.HealthJson, repo, ct),
            ParseDoraMetricsAsync(data.DoraJson, repo, ct),
            ParseArrayCountMetricAsync("[CI]", "ci_runs", "runs", data.CiJson, repo, ct),
            ParseArrayCountMetricAsync("[Deps]", "deps_alerts", "alerts", data.DependenciesJson, repo, ct),
            ParseArrayCountMetricAsync("[CodeScanning]", "code_scanning_alerts", "alerts", data.CodeScanningJson, repo, ct)
        );
    }

    /// <summary>
    /// Estrae la lunghezza di un array JSON (es. "runs", "alerts") e la registra come metrica.
    /// Schema reale MCP: nessuno di questi tool ritorna un campo "status" — solo array di elementi.
    /// </summary>
    private async Task ParseArrayCountMetricAsync(string logTag, string metricName, string arrayProperty, string? json, MonitoredRepo repo, CancellationToken ct) {
        if (!TryParseJson(json, out var root))
            return;

        _logger.LogInformation("{Tag} Parsed JSON root kind: {Kind}", logTag, root.ValueKind);

        if (root.TryGetProperty(arrayProperty, out var arrayProp) &&
            arrayProp.ValueKind == JsonValueKind.Array) {
            var count = arrayProp.GetArrayLength();
            await _metricsStore.RecordAsync(MetricsConstants.CategoryDevops,
                $"{metricName}_{repo.Repo}", count, ct);
            _logger.LogInformation("{Tag} {ArrayProperty} count {Owner}/{Repo}: {Count}", logTag, arrayProperty, repo.Owner, repo.Repo, count);
        }
    }

    private async Task ParseHealthScoreAsync(string? json, MonitoredRepo repo, CancellationToken ct) {
        if (!TryParseJson(json, out var root))
            return;

        _logger.LogInformation("[HealthScore] Parsed JSON root kind: {Kind}", root.ValueKind);

        // MCP response structure: { "report": { "score": { "value": 37 }, ... } }
        var scoreRoot = root.TryGetProperty("report", out var report) ? report : root;

        if (scoreRoot.TryGetProperty("score", out var scoreProp) &&
            TryExtractDouble(scoreProp, "value", out var score)) {
            await _metricsStore.RecordAsync(MetricsConstants.CategoryDevops,
                $"health_{repo.Repo}", score, ct);

            _logger.LogInformation("Health score {Owner}/{Repo}: {Score:F1}",
                repo.Owner, repo.Repo, score);

            if (score < _options.AlertThreshold) {
                var message = $"⚠️ Health alert: {repo.Owner}/{repo.Repo} " +
                              $"score {score:F1} (threshold: {_options.AlertThreshold})";
                await _telegram.SendMessageAsync(_options.TelegramChatId, message, ct);

                _logger.LogWarning(
                    "Alert sent for {Owner}/{Repo}: score {Score:F1} below threshold {Threshold}",
                    repo.Owner, repo.Repo, score, _options.AlertThreshold);
            }
        }
    }

    private async Task ParseDoraMetricsAsync(string? json, MonitoredRepo repo, CancellationToken ct) {
        if (!TryParseJson(json, out var root))
            return;

        _logger.LogInformation("[DORA] Parsed JSON root kind: {Kind}, properties: {Props}",
            root.ValueKind, root.ValueKind == JsonValueKind.Object
                ? string.Join(", ", root.EnumerateObject().Select(p => p.Name))
                : "N/A");

        foreach (var (metric, valueField) in DoraMetricFields) {
            if (!root.TryGetProperty(metric, out var metricObj)) {
                _logger.LogWarning("[DORA] Metric '{Metric}' not found in response", metric);
                continue;
            }

            if (TryExtractDouble(metricObj, valueField, out var value)) {
                await _metricsStore.RecordAsync(MetricsConstants.CategoryDevops,
                    $"dora_{metric}_{repo.Repo}", value, ct);
                _logger.LogInformation("[DORA] Recorded {Metric}.{Field}={Value:F2} for {Repo}",
                    metric, valueField, value, repo.Repo);
            } else {
                _logger.LogInformation("[DORA] Metric '{Metric}.{Field}' is null or missing — no data for this period",
                    metric, valueField);
            }
        }
    }

    /// <summary>
    /// Extracts a double from a JsonElement: direct number, or object with named sub-field.
    /// Returns false for null/missing values (normal when no data exists for the period).
    /// </summary>
    private static bool TryExtractDouble(JsonElement element, string subField, out double value) {
        value = 0;
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetDouble(out value);

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(subField, out var inner) &&
            inner.ValueKind == JsonValueKind.Number)
            return inner.TryGetDouble(out value);

        return false;
    }

    private bool TryParseJson(string? json, out JsonElement root) {
        root = default;
        if (string.IsNullOrWhiteSpace(json)) {
            _logger.LogError("[Parse] MCP response is null or empty — nothing to parse");
            return false;
        }

        _logger.LogDebug("[Parse] Attempting JSON parse of: {Preview}",
            json.Length > 500 ? json[..500] + "..." : json);

        try {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
            _logger.LogInformation("[Parse] JSON parsed OK — ValueKind: {Kind}", root.ValueKind);
            return true;
        } catch (JsonException ex) {
            _logger.LogError(ex, "[Parse] Failed to parse MCP response as JSON. First 200 chars: {Preview}",
                json.Length > 200 ? json[..200] : json);
            return false;
        }
    }
}
