using FlowScheduler.Core.Interfaces.AI;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace FlowScheduler.Infrastructure.AI.McpServerTools;

[McpServerToolType]
public class RagDocumentsMcpTool {
    [McpServerTool(Name = "list_rag_documents", ReadOnly = true)]
    [Description("List RAG documents in the vector store with optional filters.")]
    public async Task<string> ListRagDocumentsAsync(
          IVectorStoreService vectorStoreService,
          [Description("Filter by context: 'ops' or 'library' (null for all)")] string? context = null,
          [Description("Filter by category (null for all)")] string? category = null,
          [Description("Filter by subcategory (null for all)")] string? subCategory = null,
          [Description("Filter by document type (null for all)")] string? docType = null,
          [Description("Page number (default 1)")] int page = 1,
          [Description("Page size (1-50, default 10)")] int pageSize = 10) {
        // 1. Clamp: page min 1, pageSize clamp 1-50
        page = Math.Clamp(page, 1, int.MaxValue);
        pageSize = Math.Clamp(pageSize, 1, 50);
        // 2. SearchLibraryAsync usa page 0-based (Skip(page*pageSize)); qui page e' 1-based
        var result = await vectorStoreService.SearchLibraryAsync(context, category, subCategory, docType, page - 1, pageSize);
        // 3. Se Items vuoto → return "No documents found"
        if (result.Items.Count == 0) {
            return "No documents found";
        }
        // 4. StringBuilder, per ogni doc:
        //    "- [{doc.Context}] {doc.Title ?? doc.Source} (cat: {doc.Category}, created: {doc.CreatedAt:yyyy-MM-dd})"
        //    Se ha Content → troncalo a 200 char e aggiungi su riga successiva
        var sb = new System.Text.StringBuilder();
        foreach (var item in result.Items) {
            string title = string.IsNullOrEmpty(item.Title) ? item.Source : item.Title;
            sb.AppendLine($"- [{item.Context}] {title} (cat: {item.Category}, created: {item.CreatedAt:yyyy-MM-dd})");
        }
        // 5. Aggiungi footer: "Page {page} — {totalCount} total documents"
        sb.AppendLine($"Page {page} — {result.TotalCount} total documents");

        return sb.ToString();
    }
}
