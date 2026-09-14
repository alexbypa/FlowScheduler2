using Microsoft.Extensions.AI;

namespace FlowScheduler.Infrastructure.AI.ChatClient;

/// <summary>
/// Filtro che intercetta i messaggi e ottimizza il contesto prima dell'invio (Minerva Style).
/// </summary>
public class ContextLimitFilter : DelegatingChatClient {
    private readonly int _maxHistoryMessages;

    public ContextLimitFilter(IChatClient innerClient, int maxHistoryMessages = 10)
        : base(innerClient) {
        _maxHistoryMessages = maxHistoryMessages;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) {

        // Applichiamo il filtro ai messaggi prima di passarli al client successivo
        var filteredMessages = FilterMessages(messages);

        return await base.GetResponseAsync(filteredMessages, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {

        var filteredMessages = FilterMessages(messages);

        await foreach (var chunk in base.GetStreamingResponseAsync(filteredMessages, options, cancellationToken)) {
            yield return chunk;
        }
    }

    private IEnumerable<ChatMessage> FilterMessages(IEnumerable<ChatMessage> messages) {
        var messageList = messages.ToList();

        // 1. Cerchiamo il System Message (fondamentale per l'identità dell'IA)
        var systemMessage = messageList.FirstOrDefault(m => m.Role == ChatRole.System);

        // 2. Prendiamo solo gli ultimi N messaggi (esclusi quelli di sistema)
        var recentMessages = messageList
            .Where(m => m.Role != ChatRole.System)
            .TakeLast(_maxHistoryMessages)
            .ToList();

        // 3. Ricostruiamo la lista: System Message sempre in cima, poi la storia recente
        var result = new List<ChatMessage>();
        if (systemMessage != null)
            result.Add(systemMessage);
        result.AddRange(recentMessages);

        return result;
    }
}