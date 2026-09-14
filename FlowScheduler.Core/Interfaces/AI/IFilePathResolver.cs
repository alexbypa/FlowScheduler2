using FlowScheduler.Core.Dtos;

namespace FlowScheduler.Core.Interfaces.AI;

/// <summary>
/// Risolve un path da stack trace in (repo config, path relativo nel repo).
/// Responsabilita: parsing path, match LocalPath, estrazione path relativo.
/// </summary>
public interface IFilePathResolver
{
    /// <summary>
    /// Dato un filePath (completo o parziale) e un repoName opzionale dall'LLM,
    /// identifica il repo corretto e il path relativo nel repository GitHub.
    /// </summary>
    /// <param name="filePath">Path dal stack trace (es. C:\Work\Projects\betfair.com\...\Client.cs:403)</param>
    /// <param name="repoName">Nome repo suggerito dall'LLM (puo essere null o errato)</param>
    /// <param name="availableRepos">Lista dei repo configurati in appsettings</param>
    /// <returns>Repo matchato e path relativo pulito, o null se nessun match</returns>
    FilePathResolution? Resolve(string filePath, string? repoName, IReadOnlyList<GitHubOption> availableRepos);
}

/// <summary>
/// Risultato della risoluzione: repo selezionato + path relativo nel repo.
/// </summary>
public sealed record FilePathResolution(GitHubOption Repo, string RelativePath);
