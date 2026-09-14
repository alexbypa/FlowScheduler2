namespace FlowScheduler.Core.Models;

public class RagDocument {
    public string Id { get; set; } = string.Empty;
    /// <summary>Contesto indicizzato TAG: <see cref="VectorStoreConstants.ContextOps"/> (tool FlowScheduler) o <see cref="VectorStoreConstants.ContextLibrary"/> (frontend).</summary>
    public string Context { get; set; } = VectorStoreConstants.ContextOps;
    public string Content { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    /// <summary>Titolo visualizzato (testo); <see cref="Source"/> resta slug TAG per compatibilità.</summary>
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    /// <summary>TAG indicizzato; valore sentinella <c>none</c> se assente.</summary>
    public string SubCategory { get; set; } = string.Empty;
    /// <summary>TAG indicizzato (es. document, tutorial, other).</summary>
    public string DocumentType { get; set; } = string.Empty;
    /// <summary>Corpo markdown per la libreria RAG (testo non vettoriale dedicato).</summary>
    public string Markdown { get; set; } = string.Empty;
    /// <summary>Severity suggerita quando questo documento è usato come risposta RAG bypass (Information, Warning, Error, Fatal). Default: Error.</summary>
    public string Severity { get; set; } = "Error";
    public DateTime CreatedAt { get; set; }
    public ReadOnlyMemory<float> Embedding { get; set; }
}
