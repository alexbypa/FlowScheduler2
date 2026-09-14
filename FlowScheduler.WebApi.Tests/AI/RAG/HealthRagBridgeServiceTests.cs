using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Infrastructure.AI.RAG;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace FlowScheduler.WebApi.Tests.AI.RAG;

public class HealthRagBridgeServiceTests
{
    private readonly IRagIngestionService _ragIngestionService = Substitute.For<IRagIngestionService>();
    private readonly IChatClientFactory _chatClientFactory = Substitute.For<IChatClientFactory>();
    private readonly IChatClient _chatClient = Substitute.For<IChatClient>();

    private HealthRagBridgeService CreateService()
    {
        _chatClientFactory.GetClient(Arg.Any<ModelTier>(), Arg.Any<string?>()).Returns(_chatClient);
        return new HealthRagBridgeService(_ragIngestionService, _chatClientFactory);
    }

    private void SetChatResponse(string? text)
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text ?? string.Empty));
        _chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
    }

    // --- IngestHealthReportAsync ---

    [Fact]
    public async Task IngestHealthReportAsync_ConcatenatesAllJsonSections_IntoContent()
    {
        var service = CreateService();
        var data = new HealthCheckResult
        {
            HealthJson = "{\"report\":{\"score\":{\"value\":80}}}",
            DoraJson = "{\"dora\":true}",
            CiJson = "{\"status\":\"success\"}",
            DependenciesJson = "{\"status\":\"success\"}",
            CodeScanningJson = "{\"status\":\"success\"}"
        };

        await service.IngestHealthReportAsync(data, "owner1", "repo1");

        await _ragIngestionService.Received(1).IngestOpsDocumentAsync(
            source: Arg.Is<string>(s => s.Contains("owner1") && s.Contains("repo1")),
            category: Arg.Any<string>(),
            messageTemplate: Arg.Any<string>(),
            resolution: Arg.Any<string>(),
            content: Arg.Is<string>(c =>
                c.Contains(data.HealthJson) && c.Contains(data.DoraJson) &&
                c.Contains(data.CiJson) && c.Contains(data.DependenciesJson) &&
                c.Contains(data.CodeScanningJson)),
            severity: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestHealthReportAsync_SetsSeverityWarning_WhenScoreBelowFifty()
    {
        var service = CreateService();
        var data = new HealthCheckResult { HealthJson = "{\"report\":{\"score\":{\"value\":35}}}" };

        await service.IngestHealthReportAsync(data, "owner1", "repo1");

        await _ragIngestionService.Received(1).IngestOpsDocumentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            "Warning", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestHealthReportAsync_SetsSeverityInfo_WhenScoreAtOrAboveFifty()
    {
        var service = CreateService();
        var data = new HealthCheckResult { HealthJson = "{\"report\":{\"score\":{\"value\":75}}}" };

        await service.IngestHealthReportAsync(data, "owner1", "repo1");

        await _ragIngestionService.Received(1).IngestOpsDocumentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            "Info", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestHealthReportAsync_SetsSeverityInfo_WhenHealthJsonMissingOrUnparseable()
    {
        var service = CreateService();
        var data = new HealthCheckResult { HealthJson = "not valid json" };

        await service.IngestHealthReportAsync(data, "owner1", "repo1");

        await _ragIngestionService.Received(1).IngestOpsDocumentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            "Info", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestHealthReportAsync_ExtractsScore_FromNestedValueObject()
    {
        // Schema reale MCP: report.score e' un oggetto { "value": X }, non un numero diretto.
        var service = CreateService();
        var data = new HealthCheckResult { HealthJson = "{\"report\":{\"score\":{\"value\":10}}}" };

        await service.IngestHealthReportAsync(data, "owner1", "repo1");

        await _ragIngestionService.Received(1).IngestOpsDocumentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            "Warning", Arg.Any<CancellationToken>());
    }

    // --- FormatHealthReportAsync ---

    [Fact]
    public async Task FormatHealthReportAsync_ReturnsChatResponseText()
    {
        var service = CreateService();
        SetChatResponse("Report AI formattato");
        var data = new HealthCheckResult { HealthJson = "{\"report\":{\"score\":{\"value\":80}}}" };

        var result = await service.FormatHealthReportAsync(data, "owner1", "repo1");

        Assert.Equal("Report AI formattato", result);
    }

    [Fact]
    public async Task FormatHealthReportAsync_ReturnsEmptyString_WhenResponseTextIsNull()
    {
        var service = CreateService();
        SetChatResponse(null);
        var data = new HealthCheckResult();

        var result = await service.FormatHealthReportAsync(data, "owner1", "repo1");

        Assert.Equal("", result);
    }

    [Fact]
    public async Task FormatHealthReportAsync_CallsLlm_EvenWithPartialData()
    {
        var service = CreateService();
        SetChatResponse("ok");
        var data = new HealthCheckResult { HealthJson = "{\"report\":{\"score\":{\"value\":80}}}" };
        // DoraJson, CiJson, DependenciesJson, CodeScanningJson volutamente null (dati parziali)

        var result = await service.FormatHealthReportAsync(data, "owner1", "repo1");

        Assert.Equal("ok", result);
        await _chatClient.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>());
    }
}
