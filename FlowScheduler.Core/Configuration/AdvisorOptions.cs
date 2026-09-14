namespace FlowScheduler.Core.Configuration;

/// <summary>
/// Configurazione dell'Advisor Strategy per il routing intelligente dei prompt.
/// Mappa la sezione "AiOrchestration:Advisor" di appsettings.json.
///
/// SOLID — SRP: contiene solo dati di configurazione, nessuna logica.
/// </summary>
public sealed class AdvisorOptions {
    /// <summary>Se false, tutti i prompt vanno al modello Primary (nessun routing).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Soglia stimata di token sotto la quale un prompt e considerato "semplice".
    /// Token stimati = lunghezza caratteri / 4.
    /// </summary>
    public int SimpleTaskMaxTokens { get; set; } = 200;

    /// <summary>
    /// Parole chiave che indicano un task complesso (richiede modello pesante).
    /// Match case-insensitive su tutto il prompt.
    /// </summary>
    public List<string> ComplexKeywords { get; set; } =
    [
        "analyze", "diagnose", "investigate", "debug", "exception",
        "analizza", "diagnostica", "indaga", "eccezione", "stack trace"
    ];

    /// <summary>
    /// Parole chiave che indicano un task semplice (modello leggero sufficiente).
    /// </summary>
    public List<string> SimpleKeywords { get; set; } =
    [
        "summary", "status", "count", "list", "hello",
        "riepilogo", "stato", "conta", "elenca", "ciao"
    ];
}
