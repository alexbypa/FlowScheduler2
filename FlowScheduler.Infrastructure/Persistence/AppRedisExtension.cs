using FlowScheduler.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace FlowScheduler.Infrastructure.Persistence;

public static class AppRedisExtension {
    public static IServiceCollection AddAppRedis(this IServiceCollection services, IConfiguration configuration) {
        var redisOptions = configuration.GetSection("redisCacheOptions").Get<RedisCacheOptions>();

        if (redisOptions == null) {
            return services;
        }

        // 1. Registra le Opzioni (utili se qualche servizio le chiede tramite iniezione diretta)
        services.AddSingleton(redisOptions);

        // 2. Registra il Multiplexer puro (Richiesto da Hangfire in Program.cs)
        services.AddSingleton<IConnectionMultiplexer>(sp => {
            var configOptions = new ConfigurationOptions {
                EndPoints = { $"{redisOptions.Host}:{redisOptions.Port}" },
                Password = redisOptions.Password,
                AbortOnConnectFail = false, // Evita crash all'avvio
                ConnectTimeout = redisOptions.ConnectTimeout,
                SyncTimeout = redisOptions.SyncTimeout,
                ConnectRetry = 5
            };
            return ConnectionMultiplexer.Connect(configOptions);
        });

        // 3. Registra IDistributedCache (Risolve il bug dei 5 test falliti su WebApi)
        services.AddStackExchangeRedisCache(options => {
            options.ConfigurationOptions = new ConfigurationOptions {
                EndPoints = { $"{redisOptions.Host}:{redisOptions.Port}" },
                Password = redisOptions.Password,
                AbortOnConnectFail = false
            };
            options.InstanceName = "FlowAI:";
        });

        return services;
    }
}
