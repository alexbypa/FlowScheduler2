using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace FlowScheduler.Infrastructure.AI.Agents;

public class JobDiagnosticAgent : IJobDiagnosticAgent {
    private readonly AIAgent _agent;
    private readonly IRagSearchService _ragSearch;
    private readonly IContentStore _contentStore;
    private readonly ILogger<JobDiagnosticAgent> _logger;
    private readonly IRagEvaluator _evaluator;

    public JobDiagnosticAgent(
        [FromKeyedServices("MainAgent")] AIAgent agent,
        IRagSearchService ragSearch,
        IContentStore contentStore,
        ILogger<JobDiagnosticAgent> logger,
        IRagEvaluator evaluator) {
        _agent = agent;
        _ragSearch = ragSearch;
        _contentStore = contentStore;
        _logger = logger;
        _evaluator = evaluator;
    }

    public async Task<DiagnosticResult> AnalyzeAsync(
            string errorsMarkdown,
            CreateTaskRequest taskRequest,
            string correlationId,
            string? errorCategory = null,
            string? preSummarizeText = null,
            CancellationToken cancellationToken = default) {

        _logger.LogInformation("[AI] [ORCHESTRATOR] Avvio analisi Multi-Agente per task: {TaskName}", taskRequest.Name);
        _logger.LogInformation("[AI] [CONTENT STORE] Salvataggio dettagli in Redis...");
        string contentId = await _contentStore.SetAsync(errorsMarkdown, cancellationToken: cancellationToken);
        _logger.LogInformation("[AI] [CONTENT STORE] Salvati con ID: {ContentId} ({Size} chars)", contentId, errorsMarkdown.Length);

        var textForPrompt = !string.IsNullOrEmpty(preSummarizeText)
                ? preSummarizeText
                : errorsMarkdown.Length > 3000 ? errorsMarkdown[..3000] : errorsMarkdown;

        try {
            // STEP 1: RAG search (delegato a RagSearchService)
            var ragResult = await _ragSearch.SearchAsync(errorsMarkdown, errorCategory, cancellationToken);

            // BYPASS AI se la soluzione è altamente pertinente
            if (ragResult.HasBypassMatch && ragResult.BestMatch != null) {
                var docSeverity = ParseSeverityFromString(ragResult.BestMatch.Severity);
                _logger.LogInformation("[AI] [AI BYPASS] Match eccellente trovato nel RAG. Severity dal documento: {Severity}", docSeverity);
                return new DiagnosticResult(
                    Message: "Ho applicato una soluzione nota e validata dalla Knowledge Base.\n\n" + ragResult.FormattedContext,
                    Severity: docSeverity,
                    CorrelationId: Guid.NewGuid().ToString("N"),
                    OriginalErrorJson: errorsMarkdown,
                    TaskName: taskRequest.Name,
                    IsAiGenerated: false
                );
            }

            // STEP 2: Costruzione prompt con RAG context già incluso
            bool contentTruncated = textForPrompt.Length < errorsMarkdown.Length;
            var prompt = $"Analizza l'errore del Job '{taskRequest.Name}' (Categoria: {errorCategory}).\n" +
                         $"Repository: {taskRequest.GitHubOptionName}\n" +
                         (contentTruncated
                             ? $"I log sono stati troncati. Dettagli completi nel ContentStore (ID): {contentId}\n"
                             : "") +
                         $"Sintesi tecnica: {textForPrompt}";

            if (!string.IsNullOrEmpty(ragResult.FormattedContext)) {
                prompt += $"\n\nRisultati Knowledge Base (già cercati, NON richiamare rag-agent):\n{ragResult.FormattedContext}";
            }

            _logger.LogInformation("[AI] [PROMPT] Lunghezza totale: {PromptLength} chars, RAG context: {RagContextLength} chars", prompt.Length, ragResult.FormattedContext?.Length ?? 0);
            _logger.LogInformation("[AI] [PROMPT] Contenuto completo:\n{Prompt}", prompt);

            // STEP 3: Invocazione orchestratore AI
            var response = await _agent.RunAsync(prompt);
            var rawResponse = response.Text ?? "Nessuna risposta dall'orchestratore.";

            string provider = "Unknown";
            if (response.AdditionalProperties != null && response.AdditionalProperties.TryGetValue("AiProvider", out var p)) {
                provider = p?.ToString() ?? "Unknown";
            }

            _logger.LogInformation("[AI] [ORCHESTRATOR] Risposta ricevuta da {Provider} : {Response}", provider, rawResponse);

            var (severity, message) = ParseSeverity(rawResponse);

            // Fire-and-forget: evaluate RAG quality without blocking diagnostic flow
            _ = Task.Run(async () => {
                try {
                    var evalResult = await _evaluator.EvaluateAsync(prompt, ragResult.FormattedContext ?? "", rawResponse);
                    if (evalResult.Relevance < 2.0f || evalResult.Truth < 2.0f || evalResult.Completeness < 2.0f)
                        _logger.LogWarning("[AI] [EVAL] RAG quality below threshold for task {TaskName}: R:{Relevance:F1} T:{Truth:F1} C:{Completeness:F1}",
                            taskRequest.Name, evalResult.Relevance, evalResult.Truth, evalResult.Completeness);
                }
                catch (Exception ex) {
                    _logger.LogWarning(ex, "[AI] [EVAL] Fire-and-forget evaluation failed");
                }
            }, cancellationToken);

            return new DiagnosticResult(
                Message: message,
                Severity: severity,
                CorrelationId: correlationId,
                OriginalErrorJson: errorsMarkdown,
                TaskName: taskRequest.Name,
                IsAiGenerated: true
            );
        } catch (Exception ex) {
            _logger.LogError(ex, "[AI] [ORCHESTRATOR ERROR] {Error}", ex.Message);
            throw;
        }
    }

    private static DiagnosticSeverity ParseSeverityFromString(string? severity) =>
        (severity ?? "Error").ToUpperInvariant() switch {
            "INFORMATION" or "INFO" => DiagnosticSeverity.Information,
            "WARNING" => DiagnosticSeverity.Warning,
            "ERROR" => DiagnosticSeverity.Error,
            "FATAL" or "CRITICAL" => DiagnosticSeverity.Fatal,
            _ => DiagnosticSeverity.Error
        };

    private static (DiagnosticSeverity Severity, string Message) ParseSeverity(string rawResponse) {
        var severity = DiagnosticSeverity.Error;
        var message = rawResponse;
        var match = System.Text.RegularExpressions.Regex.Match(rawResponse, @"^\[SEVERITY:(CRITICAL|ERROR|WARNING|INFO)\]\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success) {
            message = rawResponse[match.Length..].TrimStart();
            severity = match.Groups[1].Value.ToUpperInvariant() switch {
                "CRITICAL" => DiagnosticSeverity.Fatal,
                "ERROR" => DiagnosticSeverity.Error,
                "WARNING" => DiagnosticSeverity.Warning,
                "INFO" => DiagnosticSeverity.Information,
                _ => DiagnosticSeverity.Error
            };
        }
        return (severity, message);
    }
}
