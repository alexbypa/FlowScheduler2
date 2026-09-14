namespace FlowScheduler.Core.Interfaces.AI;

/// <summary>
/// Tier del modello AI selezionabile per un agente.
/// </summary>
public enum ModelTier {
    /// <summary>Modello primario (es. Gemini) con fallback automatico.</summary>
    Primary,

    /// <summary>Modello locale leggero (es. Ollama/llama3.2).</summary>
    Local,

    /// <summary>Selezione automatica basata sull'Advisor Strategy.</summary>
    Auto
}

/// <summary>
/// Strategia di routing intelligente che valuta la complessita di un prompt
/// e decide quale tier di modello utilizzare.
///
/// SOLID — DIP: astrazione in Core, implementazione in Infrastructure.
/// SOLID — OCP: nuove strategie implementabili senza modificare i consumer.
/// SOLID — SRP: valuta SOLO la complessita, non costruisce client o agenti.
///
/// Performance: l'implementazione deve essere leggera (keyword matching),
/// senza chiamate LLM o I/O. Target: &lt;0.1ms per valutazione.
/// </summary>
public interface IAdvisorStrategy {
    /// <summary>
    /// Valuta il prompt e restituisce il ModelTier consigliato.
    /// </summary>
    /// <param name="prompt">Testo del prompt utente o del task da analizzare.</param>
    /// <returns>ModelTier.Primary per task complessi, ModelTier.Local per task semplici.</returns>
    ModelTier Evaluate(string prompt);
}
