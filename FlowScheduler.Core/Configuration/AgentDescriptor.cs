namespace FlowScheduler.Core.Configuration;

/// <summary>
/// Descrittore dichiarativo di un agente AI. Ogni istanza mappa 1:1 con una sezione
/// in appsettings.json sotto "AiOrchestration:Agents:{Key}".
///
/// SOLID — OCP: aggiungere un agente = aggiungere una sezione JSON, zero codice.
/// SOLID — SRP: descrive SOLO la configurazione, nessuna logica di costruzione.
/// </summary>
public sealed class AgentDescriptor {
    /// <summary>Identificativo univoco dell'agente (es. "main-orchestrator").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Nome leggibile (es. "MainAgent"). Usato come chiave DI keyed service.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Descrizione breve per il framework MEAI (mostrata all'orchestratore).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>System prompt / istruzioni per l'agente.</summary>
    public string Instructions { get; set; } = string.Empty;

    /// <summary>Se false, l'agente non viene registrato nel DI.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Tier del modello: "Primary" (Gemini+fallback), "Local" (Ollama), "Auto" (Advisor decide).
    /// </summary>
    public string ModelTier { get; set; } = "Primary";

    /// <summary>
    /// Nomi dei tool da assegnare (risolti da IToolRegistry).
    /// Es. ["Rag", "GitHub", "Diagnostic"]
    /// </summary>
    public List<string> Tools { get; set; } = [];

    /// <summary>
    /// Chiavi di altri AgentDescriptor da includere come sub-agenti (.AsAIFunction()).
    /// Es. ["RagAgent", "GitHubAgent"]
    /// </summary>
    public List<string> SubAgents { get; set; } = [];
}
