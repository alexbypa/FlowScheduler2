using System.Text.RegularExpressions;
using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.Logging;

namespace FlowScheduler.Infrastructure.AI.Tools;

/// <summary>
/// Risolve path da stack trace in (repo, path relativo).
/// Chain di risoluzione:
///   1. Strip line number (regex :(\d+)$ e :line \d+)
///   2. Normalize slashes
///   3. Match automatico via LocalPath prefix su tutti i repo configurati
///   4. Fallback su repoName esplicito dall'LLM
///   5. Fallback su primo repo (best effort)
/// </summary>
public sealed class StackTraceFilePathResolver : IFilePathResolver
{
    private static readonly Regex LineNumberSuffix = new(
        @":(line\s*)?\d+\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger<StackTraceFilePathResolver> _logger;

    public StackTraceFilePathResolver(ILogger<StackTraceFilePathResolver> logger)
    {
        _logger = logger;
    }

    public FilePathResolution? Resolve(string filePath, string? repoName, IReadOnlyList<GitHubOption> availableRepos)
    {
        _logger.LogDebug("[FilePathResolver] Input: filePath={FilePath}, repoName={RepoName}, repos=[{Repos}]",
            filePath, repoName ?? "(null)", string.Join(", ", availableRepos.Select(r => r.Name)));

        if (string.IsNullOrWhiteSpace(filePath) || availableRepos.Count == 0)
        {
            _logger.LogWarning("[FilePathResolver] Input non valido: filePath vuoto={Empty}, repos count={Count}",
                string.IsNullOrWhiteSpace(filePath), availableRepos.Count);
            return null;
        }

        var stripped = StripLineNumber(filePath);
        var cleanPath = stripped.Replace("\\", "/").Trim().TrimStart('/');
        _logger.LogDebug("[FilePathResolver] Path dopo cleanup: raw={Raw} → stripped={Stripped} → clean={Clean}",
            filePath, stripped, cleanPath);

        // Strategy 1: match automatico LocalPath prefix (piu affidabile)
        var localMatch = MatchByLocalPath(cleanPath, availableRepos);
        if (localMatch != null)
        {
            _logger.LogWarning("[FilePathResolver] ROOT RESOLUTION: repo={Repo}, relativePath={Path} (match via LocalPath prefix)",
                localMatch.Repo.Name, localMatch.RelativePath);
            return localMatch;
        }

        // Strategy 2: repoName esplicito dall'LLM
        if (!string.IsNullOrEmpty(repoName))
        {
            var namedRepo = availableRepos.FirstOrDefault(r =>
                string.Equals(r.Name, repoName, StringComparison.OrdinalIgnoreCase));
            if (namedRepo != null)
            {
                var relativePath = StripLocalPathPrefix(cleanPath, namedRepo);
                _logger.LogWarning("[FilePathResolver] ROOT RESOLUTION: repo={Repo}, relativePath={Path} (match via repoName esplicito)",
                    namedRepo.Name, relativePath);
                return new FilePathResolution(namedRepo, relativePath);
            }
            _logger.LogWarning("[FilePathResolver] repoName '{RepoName}' non trovato in configurazione", repoName);
        }

        // Strategy 3: fallback primo repo con path pulito (best effort)
        var fallback = availableRepos[0];
        var fallbackPath = StripLocalPathPrefix(cleanPath, fallback);
        _logger.LogWarning("[FilePathResolver] Nessun match LocalPath, fallback a repo={Repo}, path={Path}",
            fallback.Name, fallbackPath);
        return new FilePathResolution(fallback, fallbackPath);
    }

    private static string StripLineNumber(string path)
    {
        return LineNumberSuffix.Replace(path, string.Empty);
    }

    private FilePathResolution? MatchByLocalPath(string cleanPath, IReadOnlyList<GitHubOption> repos)
    {
        foreach (var repo in repos)
        {
            if (string.IsNullOrEmpty(repo.LocalPath))
            {
                _logger.LogDebug("[FilePathResolver] Repo '{Repo}' ha LocalPath vuoto, skip", repo.Name);
                continue;
            }

            var normalizedLocal = repo.LocalPath.Replace("\\", "/").TrimEnd('/') + "/";
            _logger.LogDebug("[FilePathResolver] Confronto: path={Path} vs LocalPath={Local}", cleanPath, normalizedLocal);

            if (cleanPath.StartsWith(normalizedLocal, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = cleanPath[normalizedLocal.Length..].TrimStart('/');
                if (!string.IsNullOrEmpty(relativePath))
                {
                    _logger.LogDebug("[FilePathResolver] HIT! Repo={Repo}, relativePath={Path}", repo.Name, relativePath);
                    return new FilePathResolution(repo, relativePath);
                }
                _logger.LogWarning("[FilePathResolver] LocalPath matchato ma relativePath vuoto per repo={Repo}", repo.Name);
            }
        }
        _logger.LogDebug("[FilePathResolver] Nessun repo matchato via LocalPath");
        return null;
    }

    private string StripLocalPathPrefix(string cleanPath, GitHubOption repo)
    {
        if (string.IsNullOrEmpty(repo.LocalPath))
        {
            _logger.LogDebug("[FilePathResolver] StripPrefix: LocalPath vuoto, ritorno path invariato={Path}", cleanPath);
            return cleanPath;
        }

        var normalizedLocal = repo.LocalPath.Replace("\\", "/").TrimEnd('/') + "/";
        if (cleanPath.StartsWith(normalizedLocal, StringComparison.OrdinalIgnoreCase))
        {
            var result = cleanPath[normalizedLocal.Length..].TrimStart('/');
            _logger.LogDebug("[FilePathResolver] StripPrefix: {Input} → {Output}", cleanPath, result);
            return result;
        }

        // Path non matcha LocalPath — potrebbe essere gia relativo o filename puro
        _logger.LogDebug("[FilePathResolver] StripPrefix: nessun prefix match, path invariato={Path} (LocalPath={Local})",
            cleanPath, normalizedLocal);
        return cleanPath;
    }
}
