using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace FlowScheduler.Infrastructure.AI.RAG;

public class HealthRagBridgeService : IRagBridgeService {
    private readonly IRagIngestionService _ragIngestionService;
    private readonly IChatClientFactory _chatClientFactory;
    public HealthRagBridgeService(IRagIngestionService ragIngestionService, IChatClientFactory chatClientFactory) {
        _ragIngestionService = ragIngestionService;
        _chatClientFactory = chatClientFactory;
    }
    public async Task<string> FormatHealthReportAsync(HealthCheckResult data, string owner, string repo, CancellationToken ct = default) {
        var messages = new List<ChatMessage> {
            new(ChatRole.System, "Sei un assistente DevOps senior. Produci report Telegram max 15 righe con emoji e ben formattato interpretando i dati JSON forniti di seguito"),
            new(ChatRole.User, $"Repo: {owner}/{repo}\nHealth: {data.HealthJson}\nDORA: {data.DoraJson}\nCI: {data.CiJson}\nDeps: {data.DependenciesJson}\nScanning: {data.CodeScanningJson}")
        };
        var client = _chatClientFactory.GetClient(ModelTier.Primary);
        var response = await client.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text ?? "";
    }

    public async Task IngestHealthReportAsync(HealthCheckResult data, string owner, string repo, CancellationToken ct = default) {
        var healthJson = data.HealthJson;

        var score = ExtractHealthScore(data);   // sync, no LLM, no await
        var severity = score is null ? "Info" : score < 50 ? "Warning" : "Info";

        await _ragIngestionService.IngestOpsDocumentAsync(
              source: $"Health Report for {owner}/{repo}",
              category: "devops",
              messageTemplate : $"Repo: {owner}/{repo}\nHealth: {data.HealthJson}",
              resolution: "", //Per Claude : Qui potremmo mettere la risposta AI
              content: $"Repo: {owner}/{repo}\nHealth: {data.HealthJson}\nDORA: {data.DoraJson}\nCI: {data.CiJson}\nDeps: {data.DependenciesJson}\nScanning: {data.CodeScanningJson}",
              severity: severity
        , ct);
    }

    private static double? ExtractHealthScore(HealthCheckResult data) {
        if (string.IsNullOrWhiteSpace(data.HealthJson)) return null;

        try {
            using var doc = JsonDocument.Parse(data.HealthJson);
            if (!doc.RootElement.TryGetProperty("report", out var report) ||
                !report.TryGetProperty("score", out var scoreEl)) {
                return null;
            }

            // MCP response structure: score e' un oggetto { "value": X }, non un numero diretto
            if (scoreEl.ValueKind == JsonValueKind.Number && scoreEl.TryGetDouble(out var direct))
                return direct;

            if (scoreEl.ValueKind == JsonValueKind.Object &&
                scoreEl.TryGetProperty("value", out var valueEl) &&
                valueEl.TryGetDouble(out var nested))
                return nested;
        } catch (JsonException) {
            return null;
        }

        return null;
    }
}