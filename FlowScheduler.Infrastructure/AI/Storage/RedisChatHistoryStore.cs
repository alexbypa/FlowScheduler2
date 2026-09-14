using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Threading;

namespace FlowScheduler.Infrastructure.AI.Storage;
/// <summary>
/// Implementazione di IContentStore. Salva contenuti voluminosi (log JSON, risultati SQL) in Redis con TTL, restituendo un contentId leggero. È il "magazzino" che evita di passare payload
/// enormi nei prompt, risparmiando token.
/// </summary>
public class RedisChatHistoryStore : IChatHistoryStore {
    private readonly IDistributedCache _cache;
    private readonly TimeSpan _expiration = TimeSpan.FromHours(4);

    public RedisChatHistoryStore(IDistributedCache cache) {
        _cache = cache;
    }

    public async Task<List<ChatMessage>> GetHistoryAsync(string conversationId, CancellationToken cancellationToken = default) {
        var data = await _cache.GetStringAsync($"chat_history:{conversationId}", cancellationToken);
        if (string.IsNullOrEmpty(data))
            return new List<ChatMessage>();

        var dto = JsonSerializer.Deserialize<List<ChatEntryDto>>(data);
        return dto?.Select(d => new ChatMessage(new ChatRole(d.Role), d.Content)).ToList() ?? new List<ChatMessage>();
    }

    public async Task AddMessageAsync(string conversationId, ChatMessage message, CancellationToken cancellationToken = default) {
        var history = await GetHistoryAsync(conversationId, cancellationToken);
        history.Add(message);

        // Applichiamo qui una prima logica di "filtro" semplice: teniamo gli ultimi 20 messaggi
        if (history.Count > 20)
            history = history.TakeLast(20).ToList();

        var dto = history.Select(m => new ChatEntryDto { Role = m.Role.Value, Content = m.Text ?? "" }).ToList();

        await _cache.SetStringAsync(
            $"chat_history:{conversationId}",
            JsonSerializer.Serialize(dto),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _expiration },
            cancellationToken
        );
    }

    public async Task DeleteHistoryAsync(string conversationId, CancellationToken cancellationToken = default) {
        await _cache.RemoveAsync($"chat_history:{conversationId}", cancellationToken);
    }

    private class ChatEntryDto {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }
}