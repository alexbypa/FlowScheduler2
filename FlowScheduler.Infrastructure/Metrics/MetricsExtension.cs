using FlowScheduler.Core.Interfaces.Metrics;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace FlowScheduler.Infrastructure.Metrics;

public static class MetricsExtension {
    public static IServiceCollection AddMetricsStore(this IServiceCollection services) {
        // Adaptee (IDatabase) risolto una volta dal multiplexer gia registrato, cosi RedisMetricsStore
        // riceve l'Adaptee direttamente nel costruttore, non l'oggetto che lo produce.
        services.AddSingleton(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
        services.AddSingleton<IMetricsStore, RedisMetricsStore>();
        return services;
    }
}
