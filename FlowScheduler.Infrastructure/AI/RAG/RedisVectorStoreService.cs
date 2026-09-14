using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Models;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FlowScheduler.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using NRedisStack;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using NRedisStack.Search.Literals.Enums;
using StackExchange.Redis;

namespace FlowScheduler.Infrastructure.AI.RAG;

public class RedisVectorStoreService : IVectorStoreService {
    private readonly IConnectionMultiplexer _redis;
    private readonly HangFireOptions _hangFireOptions;
    private bool _indexCreated;
    private bool _libraryAlterAttempted;
    private bool _legacyContextHashMigrated;
    private const string IndexName = "rag_idx";
    private const string KeyPrefix = "rag:doc:";

    public const string ContextOps = "ops";
    public const string ContextLibrary = "library";

    public const string SubCategoryNoneTag = "none";
    public const string DocumentTypeDefault = "document";

    private readonly ILogger<RedisVectorStoreService> _logger;

    public RedisVectorStoreService(IConnectionMultiplexer redis, HangFireOptions hangFireOptions, ILogger<RedisVectorStoreService> logger) {
        _redis = redis;
        _hangFireOptions = hangFireOptions;
        _logger = logger;
    }

    public async Task EnsureIndexCreatedAsync() {
        if (!_indexCreated) {
            var db = _redis.GetDatabase();
            var ft = db.FT();

            try {
                await ft.InfoAsync(IndexName);
                _indexCreated = true;
            } catch (RedisServerException) {
                var schema = new Schema()
                    .AddTextField("content")
                    .AddTextField("resolution")
                    .AddTagField("source")
                    .AddTextField("title")
                    .AddTagField("category")
                    .AddTagField("subcategory")
                    .AddTagField("doc_type")
                    .AddTagField("context")
                    .AddTextField("markdown")
                    .AddTagField("severity")
                    .AddNumericField("created_at", sortable: true)
                    .AddVectorField("embedding", Schema.VectorField.VectorAlgo.HNSW,
                        new Dictionary<string, object> {
                            { "TYPE", "FLOAT32" },
                            { "DIM", _hangFireOptions.OpenAI.EmbeddingDimension },
                            { "DISTANCE_METRIC", "COSINE" }
                        });

                var parameters = FTCreateParams.CreateParams()
                    .On(IndexDataType.HASH)
                    .Prefix(KeyPrefix);

                await ft.CreateAsync(IndexName, parameters, schema);
                _indexCreated = true;
            }
        }

        await EnsureLibraryFieldsAlterAsync();
        await MigrateLegacyDocumentContextHashesAsync();
    }

    /// <summary>
    /// Indici creati prima dell'estensione libreria: aggiunge campi mancanti con FT.ALTER.
    /// </summary>
    public async Task EnsureLibraryFieldsAlterAsync() {
        if (_libraryAlterAttempted)
            return;
        _libraryAlterAttempted = true;

        var db = _redis.GetDatabase();
        var fieldsToAdd = new[] {
          new[] { "subcategory", "TAG" },
          new[] { "doc_type", "TAG" },
          new[] { "markdown", "TEXT" },
          new[] { "title", "TEXT" },
          new[] { "context", "TAG" },
          new[] { "resolution", "TEXT" },
          new[] { "severity", "TAG" }
      };

        foreach (var field in fieldsToAdd) {
            try {
                await db.ExecuteAsync("FT.ALTER", IndexName, "SCHEMA", "ADD", field[0], field[1]);
            } catch (RedisException) {
                // Campo già presente — ignorato
            }
        }
    }
    /// <summary>
    /// Documenti creati prima del TAG context: imposta <see cref="ContextOps"/> sull'hash (indice RediSearch si aggiorna).
    /// </summary>
    public async Task MigrateLegacyDocumentContextHashesAsync() {
        if (_legacyContextHashMigrated) return;

        if (!_indexCreated) return;

        try {
            var db = _redis.GetDatabase();
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            foreach (var key in server.Keys(database: db.Database, pattern: $"{KeyPrefix}*")) {
                var ctx = await db.HashGetAsync(key, "context");
                if (!ctx.HasValue) {
                    await db.HashSetAsync(key, "context", ContextOps);
                }
            }

            _legacyContextHashMigrated = true;
        } catch (Exception ex) {
            _logger.LogWarning(ex, "[REDIS VCTR] Migrazione context su hash: {Error}", ex.Message);
        }
    }

    public async Task StoreDocumentAsync(RagDocument doc, CancellationToken cancellationToken = default) {
        await EnsureIndexCreatedAsync();

        var db = _redis.GetDatabase();
        var key = $"{KeyPrefix}{doc.Id}";

        var embeddingBytes = MemoryMarshal.AsBytes(doc.Embedding.Span).ToArray();
        if (doc.Embedding.Length > 0 && doc.Embedding.Length != _hangFireOptions.OpenAI.EmbeddingDimension) {
            _logger.LogWarning("[REDIS VCTR] WARN documento {DocId}: embedding {EmbeddingDim}D, indice {IndexDim}D — il documento può non comparire in ricerca vettoriale",
                doc.Id, doc.Embedding.Length, _hangFireOptions.OpenAI.EmbeddingDimension);
        }

        var subTag = string.IsNullOrWhiteSpace(doc.SubCategory) ? SubCategoryNoneTag : doc.SubCategory;
        var typeTag = string.IsNullOrWhiteSpace(doc.DocumentType) ? DocumentTypeDefault : doc.DocumentType;
        var ctxTag = NormalizeContextTag(doc.Context);

        await db.HashSetAsync(key, new HashEntry[] {
            new("content", doc.Content),
            new("resolution", doc.Resolution),
            new("source", doc.Source),
            new("title", doc.Title ?? ""),
            new("category", doc.Category),
            new("subcategory", subTag),
            new("doc_type", typeTag),
            new("context", ctxTag),
            new("markdown", doc.Markdown ?? ""),
            new("severity", string.IsNullOrWhiteSpace(doc.Severity) ? "Error" : doc.Severity),
            new("created_at", new DateTimeOffset(doc.CreatedAt).ToUnixTimeSeconds()),
            new("embedding", embeddingBytes)
        });
    }

    public async Task<RagDocument?> GetDocumentAsync(string id, CancellationToken cancellationToken = default) {
        await EnsureIndexCreatedAsync();
        var db = _redis.GetDatabase();
        var entries = await db.HashGetAllAsync($"{KeyPrefix}{id}");
        if (entries.Length == 0) return null;
        return MapHashToDocument(id, entries);
    }

    public async Task<bool> UpdateDocumentFieldsAsync(RagDocument doc, bool updateEmbedding, CancellationToken cancellationToken = default) {
        await EnsureIndexCreatedAsync();
        var db = _redis.GetDatabase();
        var key = $"{KeyPrefix}{doc.Id}";
        if (!await db.KeyExistsAsync(key)) return false;

        var subTag = string.IsNullOrWhiteSpace(doc.SubCategory) ? SubCategoryNoneTag : doc.SubCategory;
        var typeTag = string.IsNullOrWhiteSpace(doc.DocumentType) ? DocumentTypeDefault : doc.DocumentType;
        var ctxTag = NormalizeContextTag(doc.Context);

        var hashEntries = new List<HashEntry> {
            new("content", doc.Content),
            new("resolution", doc.Resolution),
            new("source", doc.Source),
            new("title", doc.Title ?? ""),
            new("category", doc.Category),
            new("subcategory", subTag),
            new("doc_type", typeTag),
            new("context", ctxTag),
            new("markdown", doc.Markdown ?? ""),
            new("severity", string.IsNullOrWhiteSpace(doc.Severity) ? "Error" : doc.Severity)
        };

        if (updateEmbedding) {
            var embeddingBytes = MemoryMarshal.AsBytes(doc.Embedding.Span).ToArray();
            hashEntries.Add(new HashEntry("embedding", embeddingBytes));
        }

        await db.HashSetAsync(key, hashEntries.ToArray());
        return true;
    }

    /// <summary>
    /// Elenco documenti (senza KNN), filtri TAG opzionali, ordinamento per data decrescente.
    /// </summary>
    public async Task<(IReadOnlyList<RagDocument> Items, long TotalCount)> SearchLibraryAsync(
        string? context,
        string? category,
        string? subCategory,
        string? docType,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) {
        await EnsureIndexCreatedAsync();

        page = Math.Max(0, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        // Elenco UI: lettura diretta dagli hash (affidabile anche se RediSearch non ha indicizzato il vettore)
        var all = await LoadDocumentsFromHashesAsync(cancellationToken);
        var filtered = all
            .Where(d => MatchesLibraryFilters(d, context, category, subCategory, docType))
            .OrderByDescending(d => d.CreatedAt)
            .ToList();

        var total = filtered.Count;
        var items = filtered.Skip(page * pageSize).Take(pageSize).ToList();
        return (items, total);
    }

    /// <summary>
    /// Elenco distinto di categoria/sottocategoria (campione fino a maxDocs documenti).
    /// </summary>
    public async Task<IReadOnlyList<(string Category, string SubCategory, string Context)>> ListLibraryCategoryPairsAsync(
        string? context,
        int maxDocs = 2000,
        CancellationToken cancellationToken = default) {
        await EnsureIndexCreatedAsync();

        maxDocs = Math.Clamp(maxDocs, 1, 5000);
        var all = await LoadDocumentsFromHashesAsync(cancellationToken);

        var set = all
            .Where(d => MatchesLibraryFilters(d, context, null, null, null))
            .Take(maxDocs)
            .Select(d => (d.Category, d.SubCategory, d.Context))
            .ToHashSet();

        return set.OrderBy(p => p.Context, StringComparer.Ordinal).ThenBy(p => p.Category, StringComparer.Ordinal).ThenBy(p => p.SubCategory, StringComparer.Ordinal).ToList();
    }

    private async Task<List<RagDocument>> LoadDocumentsFromHashesAsync(CancellationToken cancellationToken) {
        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var list = new List<RagDocument>();

        foreach (var key in server.Keys(database: db.Database, pattern: $"{KeyPrefix}*")) {
            cancellationToken.ThrowIfCancellationRequested();
            var keyStr = key.ToString();
            var id = keyStr.StartsWith(KeyPrefix, StringComparison.Ordinal) ? keyStr[KeyPrefix.Length..] : keyStr;
            var entries = await db.HashGetAllAsync(key);
            if (entries.Length == 0) continue;
            list.Add(MapHashToDocument(id, entries));
        }

        return list;
    }

    private static bool MatchesLibraryFilters(
        RagDocument doc,
        string? context,
        string? category,
        string? subCategory,
        string? docType) {
        if (!string.IsNullOrWhiteSpace(context)) {
            if (!string.Equals(doc.Context, NormalizeContextTag(context), StringComparison.Ordinal)) {
                return false;
            }
        } else if (doc.Context is not (ContextOps or ContextLibrary)) {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(category) &&
            !string.Equals(doc.Category, NormalizeTag(category, "general"), StringComparison.Ordinal)) {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(subCategory)) {
            var wantSub = NormalizeTag(subCategory, SubCategoryNoneTag);
            var docSub = string.IsNullOrWhiteSpace(doc.SubCategory) ? SubCategoryNoneTag : doc.SubCategory;
            if (!string.Equals(docSub, wantSub, StringComparison.Ordinal)) {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(docType) &&
            !string.Equals(doc.DocumentType, NormalizeTag(docType, DocumentTypeDefault), StringComparison.Ordinal)) {
            return false;
        }

        return true;
    }

    private static string BuildLibraryTagQuery(string? context, string? category, string? subCategory, string? docType) {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(context)) {
            parts.Add("@context:{" + EscapeTagValue(NormalizeContextTag(context)) + "}");
        } else {
            parts.Add("@context:{ops|library}");
        }

        if (!string.IsNullOrWhiteSpace(category)) {
            parts.Add("@category:{" + EscapeTagValue(category.Trim()) + "}");
        }

        if (!string.IsNullOrWhiteSpace(subCategory)) {
            var sub = subCategory.Trim();
            parts.Add("@subcategory:{" + EscapeTagValue(sub) + "}");
        }

        if (!string.IsNullOrWhiteSpace(docType)) {
            parts.Add("@doc_type:{" + EscapeTagValue(docType.Trim()) + "}");
        }

        return parts.Count == 0 ? "*" : string.Join(" ", parts);
    }

    private static string BuildHybridKnnQuery(int topK, string? contextFilter) {
        //var prefix = string.IsNullOrWhiteSpace(contextFilter) ? "(@context:{ops|library})" : $"(@context:{EscapeTagValue(NormalizeContextTag(contextFilter))})";
        var ctxValue = string.IsNullOrWhiteSpace(contextFilter) ? "ops|library" : contextFilter.ToLowerInvariant();
        return $"(@context:{{{ctxValue}}})=>[KNN {topK} @embedding $query_vec AS score]";
    }

    private static string EscapeTagValue(string value) {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace(".", "\\.", StringComparison.Ordinal);
    }

    public async Task<List<(RagDocument Doc, float Score)>> SearchSimilarAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int topK = 3,
        float scoreThreshold = 0.40f,
        string? contextFilter = null,
        CancellationToken cancellationToken = default) {
        
        await EnsureIndexCreatedAsync();
        var db = _redis.GetDatabase();
        var ft = db.FT();

        // 1. Log dei parametri di ricerca
        var ctxTag = NormalizeContextTag(contextFilter);
        _logger.LogDebug("[SEARCH DEBUG] Avvio ricerca. Contesto filtrato: '{ContextTag}' | TopK: {TopK}", ctxTag, topK);

        var queryBytes = MemoryMarshal.AsBytes(queryEmbedding.Span).ToArray();
        var knnQuery = BuildHybridKnnQuery(topK, contextFilter);

        _logger.LogDebug("[SEARCH DEBUG] Query RediSearch costruita: {KnnQuery}", knnQuery);
        _logger.LogDebug("[SEARCH DEBUG] Lunghezza vettore query inviato a Redis: {ByteLength} byte (Dimensioni: {Dimensions})", queryBytes.Length, queryBytes.Length / 4);

        var query = new Query(knnQuery)
            .AddParam("query_vec", queryBytes)
            .SetSortBy("score")
            .Limit(0, topK)
            .Dialect(2)
            .ReturnFields("content", "resolution", "source", "title", "category", "subcategory", "doc_type", "context", "markdown", "severity", "created_at", "score");

        var results = await ft.SearchAsync(IndexName, query);
        _logger.LogDebug("[SEARCH DEBUG] Redis ha trovato {DocCount} documenti potenziali.", results.Documents.Count);

        var documents = new List<(RagDocument, float)>();
        foreach (var result in results.Documents) {
            var scoreRv = result["score"];
            var scoreValue = scoreRv.IsNull ? null : scoreRv.ToString();
            var score = float.Parse(string.IsNullOrEmpty(scoreValue) ? "1" : scoreValue, CultureInfo.InvariantCulture);

            _logger.LogDebug("[SEARCH DEBUG] Trovato documento: {Source} | Score: {Score:F4} (Similarità: {Similarity:P2})", result["source"], score, 1 - score);

            var id = result.Id.ToString().Replace(KeyPrefix, "", StringComparison.Ordinal);
            var doc = MapSearchDocToDocument(id, result);
            documents.Add((doc, score));
        }
        return documents;
    }

    public async Task<bool> DeleteDocumentAsync(string id, CancellationToken cancellationToken = default) {
        var db = _redis.GetDatabase();
        return await db.KeyDeleteAsync($"{KeyPrefix}{id}");
    }

    private static RagDocument MapHashToDocument(string id, HashEntry[] entries) {
        var map = entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        return MapFieldsToDocument(id, map);
    }

    private static RagDocument MapSearchDocToDocument(string id, Document result) {
        static string Cell(Document d, string key) => d[key].ToString() ?? "";

        var map = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["content"] = Cell(result, "content"),
            ["resolution"] = Cell(result, "resolution"),
            ["source"] = Cell(result, "source"),
            ["title"] = Cell(result, "title"),
            ["category"] = Cell(result, "category"),
            ["subcategory"] = Cell(result, "subcategory"),
            ["doc_type"] = Cell(result, "doc_type"),
            ["context"] = Cell(result, "context"),
            ["markdown"] = Cell(result, "markdown"),
            ["severity"] = Cell(result, "severity"),
            ["created_at"] = Cell(result, "created_at")
        };

        return MapFieldsToDocument(id, map);
    }

    private static RagDocument MapFieldsToDocument(string id, IReadOnlyDictionary<string, string> map) {
        var sub = map.GetValueOrDefault("subcategory", "");
        if (string.Equals(sub, SubCategoryNoneTag, StringComparison.Ordinal)) sub = "";

        var docType = map.GetValueOrDefault("doc_type", "");

        return new RagDocument {
            Id = id,
            Context = NormalizeContextTag(map.GetValueOrDefault("context", "")),
            Content = map.GetValueOrDefault("content", ""),
            Resolution = map.GetValueOrDefault("resolution", ""),
            Source = map.GetValueOrDefault("source", ""),
            Title = map.GetValueOrDefault("title", ""),
            Category = map.GetValueOrDefault("category", ""),
            SubCategory = sub,
            DocumentType = string.IsNullOrEmpty(docType) ? DocumentTypeDefault : docType,
            Markdown = map.GetValueOrDefault("markdown", ""),
            Severity = map.GetValueOrDefault("severity", "Error"),
            CreatedAt = long.TryParse(map.GetValueOrDefault("created_at", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts)
                ? DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime
                : DateTime.MinValue,
            Embedding = default
        };
    }

    /// <summary>
    /// Valori ammessi: <see cref="ContextOps"/>, <see cref="ContextLibrary"/>.
    /// </summary>
    public static string NormalizeContextTag(string? value) {
        if (string.IsNullOrWhiteSpace(value)) return ContextOps;
        var v = value.Trim().ToLowerInvariant();
        if (v == ContextLibrary) return ContextLibrary;
        if (v == ContextOps) return ContextOps;
        return ContextOps;
    }

    /// <summary>
    /// Normalizza valori TAG per ingest (lettere minuscole, trattini, senza spazi problematici).
    /// </summary>
    public static string NormalizeTag(string? value, string fallback) {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var s = value.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\-]+", "-");
        s = Regex.Replace(s, "-{2,}", "-").Trim('-');
        return string.IsNullOrEmpty(s) ? fallback : s;
    }
}