using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace FlowScheduler.Infrastructure.AI.Tools;
/// <summary>
/// Recupera log tecnici completi dal ContentStore dato un contentId. Serve all'orchestratore per leggere i dettagli che nel prompt arrivano solo come sintesi.
/// </summary>
public class DiagnosticTool {
    private readonly IContentStore _contentStore;
    private readonly ILogger<DiagnosticTool> _logger;

    public DiagnosticTool(IContentStore contentStore, ILogger<DiagnosticTool> logger) {
        _contentStore = contentStore;
        _logger = logger;
    }

    [Description("Recupera il contenuto completo dei log tecnici partendo da un ContentId ricevuto nel prompt.")]
    public async Task<string> GetLogDetails(
        [Description("L'ID univoco del contenuto (es. a1b2c3d4e5f6). Non include il prefisso 'content:'.")] string contentId) {

        _logger.LogInformation("[DIAGNOSTIC TOOL] Recupero log per ContentId: {ContentId}...", contentId);

        var content = await _contentStore.GetAsync(contentId);

        if (string.IsNullOrEmpty(content)) {
            return "ERRORE: Log non trovato o scaduto nel ContentStore.";
        }

        return content;
    }
}