using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Interfaces.MCP;
using FlowScheduler.Core.Interfaces.Messaging;
using FlowScheduler.Core.Interfaces.Metrics;
using FlowScheduler.Core.Models;
using FlowScheduler.Infrastructure.Monitoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace FlowScheduler.WebApi.Tests.Monitoring;

public class HealthMonitorServiceTests
{
    private readonly IMcpClientService _mcpClient = Substitute.For<IMcpClientService>();
    private readonly IMetricsStore _metricsStore = Substitute.For<IMetricsStore>();
    private readonly ITelegramService _telegram = Substitute.For<ITelegramService>();
    private readonly ILogger<HealthMonitorService> _logger = Substitute.For<ILogger<HealthMonitorService>>();
    private readonly IRagBridgeService _ragBridgeService = Substitute.For<IRagBridgeService>();

    private HealthMonitorService CreateService(HealthMonitorOptions? options = null)
    {
        var opts = options ?? new HealthMonitorOptions
        {
            Enabled = true,
            AlertThreshold = 50.0,
            TelegramChatId = "123456",
            McpServerName = "projectpulse",
            DoraMetricsDays = 30,
            Repositories = [new MonitoredRepo { Owner = "testowner", Repo = "testrepo" }]
        };
        return new HealthMonitorService(
            _mcpClient, _metricsStore, _telegram,
            Options.Create(opts), _logger, _ragBridgeService);
    }

    [Fact]
    public async Task CheckAllAsync_CallsHealthScoreForEachRepo()
    {
        _mcpClient.CallToolAsync("projectpulse", "get_health_score",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{\"score\": 75.0}");
        _mcpClient.CallToolAsync("projectpulse", "get_dora_metrics",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");

        var service = CreateService();
        await service.CheckAllAsync();

        await _mcpClient.Received(1).CallToolAsync("projectpulse", "get_health_score",
            Arg.Is<Dictionary<string, object?>>(d =>
                (string)d["owner"]! == "testowner" && (string)d["repo"]! == "testrepo"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_RecordsHealthMetric()
    {
        _mcpClient.CallToolAsync("projectpulse", "get_health_score",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{\"score\": 72.5}");
        _mcpClient.CallToolAsync("projectpulse", "get_dora_metrics",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");

        var service = CreateService();
        await service.CheckAllAsync();

        await _metricsStore.Received(1).RecordAsync(
            MetricsConstants.CategoryDevops, "health_testrepo", 72.5,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_SendsAlertWhenBelowThreshold()
    {
        _mcpClient.CallToolAsync("projectpulse", "get_health_score",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{\"score\": 35.0}");
        _mcpClient.CallToolAsync("projectpulse", "get_dora_metrics",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");

        var service = CreateService();
        await service.CheckAllAsync();

        await _telegram.Received(1).SendMessageAsync("123456",
            Arg.Is<string>(s => s.Contains("testowner/testrepo") && s.Contains("35")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_NoAlertWhenAboveThreshold()
    {
        // EnableAiTelegramReport=false: isola la sola logica di alert (score < soglia).
        // Il report AI periodico (sempre inviato se il flag e' true) ha un test dedicato separato.
        var options = new HealthMonitorOptions
        {
            Enabled = true,
            AlertThreshold = 50.0,
            TelegramChatId = "123456",
            McpServerName = "projectpulse",
            DoraMetricsDays = 30,
            EnableAiTelegramReport = false,
            Repositories = [new MonitoredRepo { Owner = "testowner", Repo = "testrepo" }]
        };

        _mcpClient.CallToolAsync("projectpulse", "get_health_score",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{\"score\": 85.0}");
        _mcpClient.CallToolAsync("projectpulse", "get_dora_metrics",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");

        var service = CreateService(options);
        await service.CheckAllAsync();

        await _telegram.DidNotReceive().SendMessageAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_RecordsDoraMetrics()
    {
        _mcpClient.CallToolAsync("projectpulse", "get_health_score",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{\"score\": 80.0}");
        _mcpClient.CallToolAsync("projectpulse", "get_dora_metrics",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{\"deployment_frequency\": 2.5, \"lead_time\": 48.0, \"change_failure_rate\": 0.15, \"mttr\": 120.0}");

        var service = CreateService();
        await service.CheckAllAsync();

        await _metricsStore.Received(1).RecordAsync(MetricsConstants.CategoryDevops,
            "dora_deployment_frequency_testrepo", 2.5, Arg.Any<CancellationToken>());
        await _metricsStore.Received(1).RecordAsync(MetricsConstants.CategoryDevops,
            "dora_lead_time_testrepo", 48.0, Arg.Any<CancellationToken>());
        await _metricsStore.Received(1).RecordAsync(MetricsConstants.CategoryDevops,
            "dora_change_failure_rate_testrepo", 0.15, Arg.Any<CancellationToken>());
        await _metricsStore.Received(1).RecordAsync(MetricsConstants.CategoryDevops,
            "dora_mttr_testrepo", 120.0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_SkipsWhenDisabled()
    {
        var service = CreateService(new HealthMonitorOptions { Enabled = false });
        await service.CheckAllAsync();

        await _mcpClient.DidNotReceive().CallToolAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_ContinuesOnMcpFailure()
    {
        var options = new HealthMonitorOptions
        {
            TelegramChatId = "123456",
            McpServerName = "projectpulse",
            Repositories =
            [
                new MonitoredRepo { Owner = "owner1", Repo = "repo1" },
                new MonitoredRepo { Owner = "owner2", Repo = "repo2" }
            ]
        };

        _mcpClient.CallToolAsync("projectpulse", "get_health_score",
            Arg.Is<Dictionary<string, object?>>(d => (string)d["owner"]! == "owner1"),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("MCP connection failed"));

        _mcpClient.CallToolAsync("projectpulse", "get_health_score",
            Arg.Is<Dictionary<string, object?>>(d => (string)d["owner"]! == "owner2"),
            Arg.Any<CancellationToken>())
            .Returns("{\"score\": 90.0}");

        _mcpClient.CallToolAsync("projectpulse", "get_dora_metrics",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");

        var service = CreateService(options);
        await service.CheckAllAsync();

        await _metricsStore.Received(1).RecordAsync(MetricsConstants.CategoryDevops,
            "health_repo2", 90.0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_HandlesInvalidJson()
    {
        _mcpClient.CallToolAsync("projectpulse", "get_health_score",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("not valid json");
        _mcpClient.CallToolAsync("projectpulse", "get_dora_metrics",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("not valid json");

        var service = CreateService();
        await service.CheckAllAsync();

        await _metricsStore.DidNotReceive().RecordAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<double>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_RecordsCiRunsCount_FromRunsArray()
    {
        // Schema reale MCP: { "runs": [...] } — nessun campo "status" a livello root.
        _mcpClient.CallToolAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");
        _mcpClient.CallToolAsync("projectpulse", "check_ci_status",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{\"runs\": [{\"id\": 1}, {\"id\": 2}, {\"id\": 3}]}");

        var service = CreateService();
        await service.CheckAllAsync();

        await _metricsStore.Received(1).RecordAsync(
            MetricsConstants.CategoryDevops, "ci_runs_testrepo", 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_RecordsDepsAlertsCount_FromAlertsArray()
    {
        // Schema reale MCP: { "alerts": [...] } — nessun campo "status" a livello root.
        _mcpClient.CallToolAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");
        _mcpClient.CallToolAsync("projectpulse", "analyze_dependencies",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{\"alerts\": [{\"summary\": \"vuln1\"}, {\"summary\": \"vuln2\"}]}");

        var service = CreateService();
        await service.CheckAllAsync();

        await _metricsStore.Received(1).RecordAsync(
            MetricsConstants.CategoryDevops, "deps_alerts_testrepo", 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_SkipsCodeScanningMetric_WhenResponseIsPlainTextNotJson()
    {
        // Se code scanning non e' abilitato sul repo, il tool ritorna testo semplice, non JSON.
        _mcpClient.CallToolAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");
        _mcpClient.CallToolAsync("projectpulse", "analyze_code_scanning",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("Code scanning is not enabled for this repository.");

        var service = CreateService();
        await service.CheckAllAsync();

        await _metricsStore.DidNotReceive().RecordAsync(
            MetricsConstants.CategoryDevops, "code_scanning_alerts_testrepo", Arg.Any<double>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_CallsAllFiveMcpTools()
    {
        _mcpClient.CallToolAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");

        var service = CreateService();
        await service.CheckAllAsync();

        foreach (var tool in new[] { "get_health_score", "get_dora_metrics", "check_ci_status", "analyze_dependencies", "analyze_code_scanning" })
        {
            await _mcpClient.Received(1).CallToolAsync("projectpulse", tool,
                Arg.Is<Dictionary<string, object?>>(d => (string)d["owner"]! == "testowner" && (string)d["repo"]! == "testrepo"),
                Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task CheckAllAsync_IsolatesSingleToolFailure_OtherToolsStillProcessed()
    {
        // analyze_dependencies fallisce, ma get_health_score deve comunque essere registrato
        // (CollectAllDataAsync non deve scartare tutto il repo per un solo tool rotto).
        _mcpClient.CallToolAsync("projectpulse", "get_health_score",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{\"score\": 90.0}");
        _mcpClient.CallToolAsync("projectpulse", "get_dora_metrics",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");
        _mcpClient.CallToolAsync("projectpulse", "check_ci_status",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");
        _mcpClient.CallToolAsync("projectpulse", "analyze_dependencies",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("MCP tool failed"));
        _mcpClient.CallToolAsync("projectpulse", "analyze_code_scanning",
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");

        var service = CreateService();
        await service.CheckAllAsync();

        await _metricsStore.Received(1).RecordAsync(
            MetricsConstants.CategoryDevops, "health_testrepo", 90.0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_CallsIngestHealthReport_WhenRagIngestionEnabled()
    {
        var options = new HealthMonitorOptions
        {
            Enabled = true,
            TelegramChatId = "123456",
            McpServerName = "projectpulse",
            EnableRagIngestion = true,
            EnableAiTelegramReport = false,
            Repositories = [new MonitoredRepo { Owner = "testowner", Repo = "testrepo" }]
        };
        _mcpClient.CallToolAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");

        var service = CreateService(options);
        await service.CheckAllAsync();

        await _ragBridgeService.Received(1).IngestHealthReportAsync(
            Arg.Any<HealthCheckResult>(), "testowner", "testrepo", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_SkipsIngestHealthReport_WhenRagIngestionDisabled()
    {
        var options = new HealthMonitorOptions
        {
            Enabled = true,
            TelegramChatId = "123456",
            McpServerName = "projectpulse",
            EnableRagIngestion = false,
            EnableAiTelegramReport = false,
            Repositories = [new MonitoredRepo { Owner = "testowner", Repo = "testrepo" }]
        };
        _mcpClient.CallToolAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");

        var service = CreateService(options);
        await service.CheckAllAsync();

        await _ragBridgeService.DidNotReceive().IngestHealthReportAsync(
            Arg.Any<HealthCheckResult>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_SendsAiFormattedReport_WhenTelegramReportEnabled()
    {
        var options = new HealthMonitorOptions
        {
            Enabled = true,
            TelegramChatId = "123456",
            McpServerName = "projectpulse",
            EnableRagIngestion = false,
            EnableAiTelegramReport = true,
            Repositories = [new MonitoredRepo { Owner = "testowner", Repo = "testrepo" }]
        };
        _mcpClient.CallToolAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");
        _ragBridgeService.FormatHealthReportAsync(Arg.Any<HealthCheckResult>(), "testowner", "testrepo", Arg.Any<CancellationToken>())
            .Returns("Report AI");

        var service = CreateService(options);
        await service.CheckAllAsync();

        await _telegram.Received(1).SendMessageAsync("123456", "Report AI", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAllAsync_SkipsAiFormattedReport_WhenTelegramReportDisabled()
    {
        var options = new HealthMonitorOptions
        {
            Enabled = true,
            TelegramChatId = "123456",
            McpServerName = "projectpulse",
            EnableRagIngestion = false,
            EnableAiTelegramReport = false,
            Repositories = [new MonitoredRepo { Owner = "testowner", Repo = "testrepo" }]
        };
        _mcpClient.CallToolAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns("{}");

        var service = CreateService(options);
        await service.CheckAllAsync();

        await _ragBridgeService.DidNotReceive().FormatHealthReportAsync(
            Arg.Any<HealthCheckResult>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _telegram.DidNotReceive().SendMessageAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
