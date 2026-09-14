using System.Threading;

namespace FlowScheduler.Core.Interfaces.AI;

/// <summary>
/// Defines a store for holding large text or objects (like log files or DB results)
/// to pass them by reference (ID) between AI Agents, saving LLM tokens.
/// </summary>
public interface IContentStore {
    /// <summary>
    /// Saves the content and returns a unique identifier.
    /// </summary>
    Task<string> SetAsync(string content, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the content by its unique identifier.
    /// </summary>
    Task<string?> GetAsync(string contentId, CancellationToken cancellationToken = default);
}
