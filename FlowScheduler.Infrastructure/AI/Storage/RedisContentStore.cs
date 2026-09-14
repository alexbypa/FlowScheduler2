using FlowScheduler.Core.Interfaces.AI;
using StackExchange.Redis;
using System.Threading;

namespace FlowScheduler.Infrastructure.AI.Storage;
/// <summary>
/// Implementazione di IChatHistoryStore su Redis. Serializza i ChatMessage MEAI e li persiste con chiave di sessione. Permette all'orchestratore di mantenere contesto tra invocazioni
/// successive.
/// </summary>
public class RedisContentStore : IContentStore {
    private readonly IConnectionMultiplexer _redis;
    private const string Prefix = "content:";

    public RedisContentStore(IConnectionMultiplexer redis) {
        _redis = redis;
    }

    public async Task<string> SetAsync(string content, TimeSpan? expiration = null, CancellationToken cancellationToken = default) {
        var db = _redis.GetDatabase();
        
        // Creiamo l'ID del "Documento di Drive"
        var id = Guid.NewGuid().ToString("N");
        var key = $"{Prefix}{id}";
        
        // Scadenza di default: 1 ora (è memoria di lavoro per l'AI, non a lungo termine)
        var ttl = expiration ?? TimeSpan.FromHours(48);

        await db.StringSetAsync(key, content, ttl);
        
        return id;
    }

    public async Task<string?> GetAsync(string contentId, CancellationToken cancellationToken = default) {
        var db = _redis.GetDatabase();
        var key = $"{Prefix}{contentId}";
        
        var value = await db.StringGetAsync(key);
        return value.HasValue ? value.ToString() : null;
    }
}
