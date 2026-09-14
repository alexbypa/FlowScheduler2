using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace FlowScheduler.Infrastructure.AI.Tools;
/// <summary>
/// Cerca soluzioni note nella knowledge base vettoriale (Redis). Genera l'embedding della query e restituisce documenti simili con eventuali risoluzioni.
/// </summary>
public class RagTool {
    private readonly IVectorStoreService _vectorStore;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public RagTool(IVectorStoreService vectorStore, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator) {
        _vectorStore = vectorStore;
        _embeddingGenerator = embeddingGenerator;
    }

    [Description("Cerca soluzioni note o documentazione tecnica nella base di conoscenza interna.")]
    public async Task<string> SearchKnowledgeBaseAsync(
        [Description("La query testuale da cercare (es. 'errore timeout database')")] string query) {
        // 1. Generiamo l'embedding per la query
        var embedding = await _embeddingGenerator.GenerateVectorAsync(query);

        // 2. Cerchiamo su Redis (usiamo una soglia generosa per il debug)
        var results = await _vectorStore.SearchSimilarAsync(embedding, topK: 3, scoreThreshold: 0.8f);

        if (results.Count == 0)
            return "Nessuna soluzione trovata nella base di conoscenza.";

        // 3. Formattiamo i risultati per l'agente
        var sb = new System.Text.StringBuilder();
        foreach (var res in results) {
            sb.AppendLine($"[DOC] Fonte: {res.Doc.Source} | Rilevanza: {1 - res.Score:P}");
            sb.AppendLine($"Contenuto: {res.Doc.Content}");
            sb.AppendLine($"Risoluzione suggerita: {res.Doc.Resolution}");
            sb.AppendLine("---");
        }

        return sb.ToString();
    }
}