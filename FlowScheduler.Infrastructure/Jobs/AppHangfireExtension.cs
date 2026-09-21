using FlowScheduler.Core.Interfaces.Jobs;
using FlowScheduler.Infrastructure.Persistence;
using Hangfire;
using Hangfire.Console;
using Hangfire.Redis.StackExchange;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace FlowScheduler.Infrastructure.Jobs;

public static class AppHangfireExtension {
    public static IServiceCollection AddAppHangfire(this IServiceCollection services) {
        
        // Configurazione motore condivisa (Database, Serializzazione, Console)
        services.AddHangfire((sp, configuration) => {
            var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();

            configuration.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseColouredConsoleLogProvider()
                .UseRedisStorage(multiplexer, new RedisStorageOptions {
                    Db = 0,
                    Prefix = "hangfire:"
                })
                .UseConsole();
        });
        
        services.AddScoped<ITaskSchedulerService, MonitorHangFireService>();

        return services;
    }
}
