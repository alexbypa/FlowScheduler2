using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlowScheduler.Infrastructure.AI.Tools;

/// <summary>
/// Legge codice sorgente da GitHub via API. Quando lo stack trace indica un file .cs,
/// l'agente GitHub lo usa per analizzare il codice nel punto dell'errore.
/// Supporta multi-repo: seleziona la configurazione corretta in base al path locale.
///
/// SOLID — SRP: HTTP fetch + URL construction. Path resolution delegata a IFilePathResolver.
/// SOLID — DIP: dipende da IFilePathResolver (astrazione), non da logica di parsing concreta.
///
/// Strategia di risoluzione case-sensitive:
///   1. Path diretto via Contents API (se case corretto → 1 HTTP call)
///   2. Se 404 → Trees API recursive=1 (carica albero repo, match case-insensitive locale, cache)
///   3. Call successive → path cache → 1 HTTP call
/// </summary>
public class GitHubTool {
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly List<GitHubOption> _options;
    private readonly IFilePathResolver _pathResolver;
    private readonly ILogger<GitHubTool> _logger;

    /// <summary>
    /// Cache path corretti per repo (case-sensitive GitHub).
    /// Key: "owner/repo::relativePath" (case-insensitive), Value: path esatto nel repo.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _pathCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cache dell'albero completo del repo (da Trees API recursive=1).
    /// Key: "owner/repo", Value: lista di tutti i path blob nel repo.
    /// Caricato lazy alla prima 404, riusato per tutte le risoluzioni successive.
    /// </summary>
    private readonly ConcurrentDictionary<string, List<string>> _treeCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cache del contenuto dei file gia scaricati.
    /// Key: "owner/repo::resolvedPath", Value: contenuto completo del file.
    /// Evita HTTP ripetute per lo stesso file con line number diversi (es. Client.cs:403, :405, :329).
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _contentCache = new(StringComparer.OrdinalIgnoreCase);

    private const int LineWindowRadius = 25;

    private static readonly Regex LineNumberPattern = new(
        @":(line\s*)?(\d+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public GitHubTool(
        IHttpClientFactory httpClientFactory,
        List<GitHubOption> options,
        IFilePathResolver pathResolver,
        ILogger<GitHubTool> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _pathResolver = pathResolver;
        _logger = logger;
    }

    [Description("Legge il codice sorgente di un file C# da GitHub per analizzare bug o errori. USALO SEMPRE quando vedi un percorso .cs in uno stack trace, anche se sembra locale. Passa SEMPRE il percorso COMPLETO dal stack trace, incluso il numero di riga. Passa sempre il parametro repoName se il prompt indica 'Repository: X'.")]
    public async Task<string> ReadGitHubCode(
        [Description("Il percorso COMPLETO del file .cs dallo stack trace (es. C:\\Work\\Projects\\betfair.com\\Service\\Client.cs:403). Non passare solo il nome del file.")] string filePath,
        [Description("Il nome del repository configurato (es. 'Betfair', 'IsibetPro'). Corrisponde al valore 'Repository' indicato nel prompt.")] string? repoName = null) {

        _logger.LogInformation("[GITHUB TOOL] Richiesta lettura file: {FilePath}, Repo: {RepoName}", filePath, repoName ?? "(auto-detect)");

        // Estrai line number PRIMA della risoluzione path (il resolver lo strippa)
        int? lineNumber = ExtractLineNumber(filePath);
        if (lineNumber != null)
            _logger.LogDebug("[GITHUB TOOL] Line number estratto: {Line}", lineNumber);

        var resolution = _pathResolver.Resolve(filePath, repoName, _options.AsReadOnly());
        if (resolution == null) {
            return "Impossibile risolvere il path del file. Configurazione GitHub mancante o path non valido.";
        }

        var repo = resolution.Repo;
        var relativePath = resolution.RelativePath;

        if (repo.Owner == null || repo.Repository == null) {
            return "Configurazione GitHub incompleta: Owner o Repository mancante.";
        }

        var repoKey = $"{repo.Owner}/{repo.Repository}";
        var cacheKey = $"{repoKey}::{relativePath}";

        // Tentativo 1: path cache + content cache (zero HTTP se file gia scaricato)
        if (_pathCache.TryGetValue(cacheKey, out var cachedPath)) {
            var contentKey = $"{repoKey}::{cachedPath}";
            if (_contentCache.TryGetValue(contentKey, out var cachedContent)) {
                _logger.LogWarning("[GITHUB TOOL] LETTURA OK (cache): '{Path}' ({Chars} chars, 0 HTTP calls)", cachedPath, cachedContent.Length);
                return FormatResponse(cachedContent, cachedPath, lineNumber);
            }
            var cachedResult = await FetchAndCacheContentAsync(repo, cachedPath, repoKey);
            if (cachedResult != null) {
                _logger.LogWarning("[GITHUB TOOL] LETTURA OK (path cache + fetch): '{Path}' ({Chars} chars)", cachedPath, cachedResult.Length);
                return FormatResponse(cachedResult, cachedPath, lineNumber);
            }
        }

        // Tentativo 2: path diretto via Contents API (funziona se case e gia corretto)
        var result = await FetchAndCacheContentAsync(repo, relativePath, repoKey);
        if (result != null) {
            _pathCache[cacheKey] = relativePath;
            _logger.LogWarning("[GITHUB TOOL] LETTURA OK (Contents API diretta): '{Path}' ({Chars} chars)", relativePath, result.Length);
            return FormatResponse(result, relativePath, lineNumber);
        }

        // Tentativo 3: Trees API recursive=1 — carica albero, match case-insensitive, fetch con path corretto
        _logger.LogWarning("[GITHUB TOOL] Path diretto 404, risoluzione via Trees API: {Path}", relativePath);
        var resolvedPath = await ResolvePathViaTreeAsync(repo, relativePath);
        if (resolvedPath != null) {
            _pathCache[cacheKey] = resolvedPath;
            _logger.LogInformation("[GITHUB TOOL] Tree match: '{Original}' → '{Resolved}'", relativePath, resolvedPath);

            var treeResult = await FetchAndCacheContentAsync(repo, resolvedPath, repoKey);
            if (treeResult != null) {
                _logger.LogWarning("[GITHUB TOOL] LETTURA OK (Trees API case-fix): '{Original}' → '{Resolved}' ({Chars} chars)",
                    relativePath, resolvedPath, treeResult.Length);
                return FormatResponse(treeResult, resolvedPath, lineNumber);
            }
        }

        _logger.LogWarning("[GITHUB TOOL] LETTURA FALLITA: file non trovato su {RepoKey}, path tentato: '{Path}'", repoKey, relativePath);
        return $"File non trovato su GitHub ({repoKey}). Path tentato: {relativePath}. NON riprovare con gli stessi argomenti.";
    }

    /// <summary>
    /// Carica l'albero completo del repo (1 sola call HTTP, poi cache) e cerca il path
    /// con match case-insensitive. Risolve il problema di case mismatch tra path locale
    /// e path nel repo GitHub (es. Betfair.Shared.Libraries vs betfair.shared.libraries).
    /// </summary>
    private async Task<string?> ResolvePathViaTreeAsync(GitHubOption repo, string relativePath) {
        var repoKey = $"{repo.Owner}/{repo.Repository}";

        if (!_treeCache.TryGetValue(repoKey, out var treePaths)) {
            treePaths = await FetchRepoTreeAsync(repo);
            if (treePaths == null) return null;
            _treeCache[repoKey] = treePaths;
        }

        // Match case-insensitive sul path completo
        var match = treePaths.FirstOrDefault(p =>
            string.Equals(p, relativePath, StringComparison.OrdinalIgnoreCase));

        if (match != null) {
            _logger.LogDebug("[GITHUB TOOL] Tree exact match: '{Path}'", match);
            return match;
        }

        // Fallback: match sul filename con path similarity scoring
        var fileName = Path.GetFileName(relativePath);
        var candidates = treePaths
            .Where(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase))
            .Select(p => (Path: p, Score: ComputePathSimilarity(relativePath, p)))
            .OrderByDescending(c => c.Score)
            .ToList();

        if (candidates.Count > 0) {
            var best = candidates[0];
            _logger.LogInformation("[GITHUB TOOL] Tree best match: '{Path}' (similarity={Score:F2}, candidates={Count})",
                best.Path, best.Score, candidates.Count);
            return best.Path;
        }

        _logger.LogWarning("[GITHUB TOOL] Tree: nessun match per '{Path}' (albero ha {Count} file)", relativePath, treePaths.Count);
        return null;
    }

    /// <summary>
    /// Chiama GET /repos/{owner}/{repo}/git/trees/{branch}?recursive=1
    /// Ritorna la lista di tutti i path "blob" (file) nel repo.
    /// 1 sola HTTP call, risultato cachato in _treeCache.
    /// </summary>
    private async Task<List<string>?> FetchRepoTreeAsync(GitHubOption repo) {
        var branch = repo.Branch ?? "main";
        var url = $"https://api.github.com/repos/{repo.Owner}/{repo.Repository}/git/trees/{branch}?recursive=1";
        _logger.LogInformation("[GITHUB TOOL] Trees API: {Url}", url);

        try {
            var httpClient = CreateGitHubClient(repo);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) {
                _logger.LogWarning("[GITHUB TOOL] Trees API {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var tree = doc.RootElement.GetProperty("tree");

            var paths = new List<string>();
            for (int i = 0; i < tree.GetArrayLength(); i++) {
                var item = tree[i];
                if (item.GetProperty("type").GetString() == "blob") {
                    var path = item.GetProperty("path").GetString();
                    if (path != null)
                        paths.Add(path);
                }
            }

            _logger.LogInformation("[GITHUB TOOL] Trees API: {Count} file caricati per {Owner}/{Repo}",
                paths.Count, repo.Owner, repo.Repository);
            return paths;
        } catch (Exception ex) {
            _logger.LogError(ex, "[GITHUB TOOL] Errore Trees API");
            return null;
        }
    }

    /// <summary>
    /// Se c'e un line number, ritorna solo una finestra di ~50 righe centrata su quella linea.
    /// Riduce drasticamente i token (25K chars → ~2K chars) ed evita 413 token limit.
    /// </summary>
    private string FormatResponse(string fullContent, string filePath, int? lineNumber) {
        if (lineNumber == null)
            return fullContent;

        var lines = fullContent.Split('\n');
        int targetLine = lineNumber.Value;
        int totalLines = lines.Length;

        if (targetLine < 1 || targetLine > totalLines) {
            _logger.LogWarning("[GITHUB TOOL] Line {Line} fuori range (file ha {Total} righe), ritorno file intero", targetLine, totalLines);
            return fullContent;
        }

        int start = Math.Max(0, targetLine - 1 - LineWindowRadius);
        int end = Math.Min(totalLines, targetLine - 1 + LineWindowRadius + 1);

        var window = new System.Text.StringBuilder();
        window.AppendLine($"// File: {filePath} (linee {start + 1}-{end} di {totalLines}, centrato su linea {targetLine})");
        window.AppendLine();
        for (int i = start; i < end; i++) {
            var marker = (i == targetLine - 1) ? " // ◄◄◄ ERRORE QUI" : "";
            window.AppendLine($"{i + 1,4}: {lines[i]}{marker}");
        }

        _logger.LogInformation("[GITHUB TOOL] Finestra linee {Start}-{End} di {Total} (target: {Target})",
            start + 1, end, totalLines, targetLine);

        return window.ToString();
    }

    private static int? ExtractLineNumber(string filePath) {
        var match = LineNumberPattern.Match(filePath);
        if (match.Success && int.TryParse(match.Groups[2].Value, out var line))
            return line;
        return null;
    }

    /// <summary>
    /// Fetch + salva in content cache. Le call successive per lo stesso file (con line number diverso)
    /// saranno servite dalla cache senza HTTP.
    /// </summary>
    private async Task<string?> FetchAndCacheContentAsync(GitHubOption repo, string relativePath, string repoKey) {
        var contentKey = $"{repoKey}::{relativePath}";
        if (_contentCache.TryGetValue(contentKey, out var cached))
            return cached;

        var content = await FetchFileContentAsync(repo, relativePath);
        if (content != null)
            _contentCache[contentKey] = content;
        return content;
    }

    private async Task<string?> FetchFileContentAsync(GitHubOption repo, string relativePath) {
        var url = $"https://api.github.com/repos/{repo.Owner}/{repo.Repository}/contents/{relativePath}?ref={repo.Branch ?? "main"}";
        _logger.LogInformation("[GITHUB TOOL] Contents URL: {Url}", url);

        try {
            var httpClient = CreateGitHubClient(repo);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3.raw"));

            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) {
                _logger.LogWarning("[GITHUB TOOL] Contents API {Status} per {Url}", response.StatusCode, url);
                return null;
            }
            return await response.Content.ReadAsStringAsync();
        } catch (Exception ex) {
            _logger.LogError(ex, "[GITHUB TOOL] Errore HTTP Contents API");
            return null;
        }
    }

    /// <summary>
    /// Calcola similarita tra path input (dal stack trace) e path candidato (da GitHub).
    /// Conta quanti segmenti di directory dell'input sono presenti nel candidato.
    /// </summary>
    private static double ComputePathSimilarity(string inputPath, string candidatePath) {
        var inputParts = inputPath.Replace("\\", "/").Split('/', StringSplitOptions.RemoveEmptyEntries);
        var candidateParts = candidatePath.Replace("\\", "/").Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (inputParts.Length == 0) return 0;

        int matches = inputParts.Count(part =>
            candidateParts.Any(cp => string.Equals(cp, part, StringComparison.OrdinalIgnoreCase)));

        return (double)matches / inputParts.Length;
    }

    private HttpClient CreateGitHubClient(GitHubOption repo) {
        var httpClient = _httpClientFactory.CreateClient("GitHubClient");
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FlowScheduler-AI", "1.0"));
        if (!string.IsNullOrEmpty(repo.Token)) {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", repo.Token);
        }
        return httpClient;
    }
}
