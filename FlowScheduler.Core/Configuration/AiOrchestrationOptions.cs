using System.ComponentModel;

namespace FlowScheduler.Core.Configuration;

/// <summary>
/// Root della configurazione per l'orchestrazione AI.
/// Mappa la sezione "AiOrchestration" di appsettings.json.
///
/// Esempio JSON:
/// <code>
/// "AiOrchestration": {
///   "Agents": {
///     "MainAgent": { "Id": "main-orchestrator", "Enabled": true, ... },
///     "ErrorSummaryAgent": { "Id": "error-summary-agent", "Enabled": true, ... }
///   },
///   "Advisor": { "Enabled": true, "SimpleTaskMaxTokens": 200 }
/// }
/// </code>
///
/// SOLID — OCP: la struttura e estendibile aggiungendo nuove chiavi nel dizionario Agents.
/// SOLID — SRP: aggregazione pura di sotto-configurazioni, nessuna logica.
/// </summary>
public sealed class AiOrchestrationOptions {
    public const string SectionName = "AiOrchestration";

    /// <summary>
    /// Dizionario chiave DI -> descrittore agente.
    /// La chiave (es. "MainAgent") diventa il keyed service name nel DI.
    /// </summary>
    public Dictionary<string, AgentDescriptor> Agents { get; set; } = new();

    /// <summary>Configurazione dell'Advisor Strategy.</summary>
    public AdvisorOptions Advisor { get; set; } = new();
}
