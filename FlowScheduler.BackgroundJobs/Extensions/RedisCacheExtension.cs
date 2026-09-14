using FlowScheduler.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlowScheduler.BackgroundJobs.Extensions;

/// <summary>
/// Registrazione IDistributedCache con backend Redis.
/// </summary>
public static class RedisCacheExtension {
    public static IServiceCollection AddRedisCache(this IServiceCollection services, IConfiguration configuration) {
        services.AddStackExchangeRedisCache(options => {
            var redis = configuration.GetSection("redisCacheOptions").Get<RedisCacheOptions>();
            options.Configuration = $"{redis?.Host}:{redis?.Port}";
            options.InstanceName = "FlowAI:";
        });
        return services;
    }
}
