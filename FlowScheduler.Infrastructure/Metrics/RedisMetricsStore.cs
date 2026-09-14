using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.Metrics;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace FlowScheduler.Infrastructure.Metrics;

public class RedisMetricsStore : IMetricsStore {
    // Adaptee: driver Redis generico (key/member/score), niente concetto di "metrica".
    private readonly IDatabase _db;

    public RedisMetricsStore(IDatabase db) {
        _db = db;
    }

    public async Task<IReadOnlyList<MetricEntry>> QueryAsync(string category, string name, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) {
        var entries = await _db.SortedSetRangeByScoreWithScoresAsync(BuildKey(category, name), from.ToUnixTimeMilliseconds(), to.ToUnixTimeMilliseconds());
        return entries.Select(entry => ToMetricEntry(category, name, entry)).ToList();
    }

    public async Task RecordAsync(string category, string name, double value, CancellationToken cancellationToken = default) {
        var (member, score) = ToRedisEntry(value, DateTimeOffset.UtcNow);
        await _db.SortedSetAddAsync(BuildKey(category, name), member, score);
    }

    // --- Traduzione dominio (MetricEntry) <-> vocabolario Redis (key/member/score) ---

    private static string BuildKey(string category, string name) => $"metrics:{category}:{name}";

    private static (RedisValue member, double score) ToRedisEntry(double value, DateTimeOffset timestamp) {
        var member = JsonConvert.SerializeObject(new { v = value, id = Guid.NewGuid().ToString("N") });
        return (member, timestamp.ToUnixTimeMilliseconds());
    }

    private static MetricEntry ToMetricEntry(string category, string name, SortedSetEntry entry) {
        var payload = JsonConvert.DeserializeAnonymousType(entry.Element.ToString(), new { v = 0.0, id = "" });
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)entry.Score);
        return new MetricEntry(category, name, payload!.v, timestamp);
    }
}
