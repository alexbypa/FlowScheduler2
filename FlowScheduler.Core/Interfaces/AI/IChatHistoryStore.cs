using System.Threading;
using Microsoft.Extensions.AI;

namespace FlowScheduler.Core.Interfaces.AI;

/// <summary>
/// Interfaccia per la persistenza della cronologia delle conversazioni AI. Permette di salvare e recuperare i messaggi di una sessione di chat, identificata da un ID.
/// </summary>
public interface IChatHistoryStore {
    Task<List<ChatMessage>> GetHistoryAsync(string conversationId, CancellationToken cancellationToken = default);
    Task AddMessageAsync(string conversationId, ChatMessage message, CancellationToken cancellationToken = default);
    Task DeleteHistoryAsync(string conversationId, CancellationToken cancellationToken = default);
}
