using CSharpEssentials.LoggerHelper;
using CSharpEssentials.LoggerHelper.Sink.HangfireConsole;
using FlowScheduler.BackgroundJobs.Extensions;
using FlowScheduler.BackgroundJobs.Jobs;
using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.MCP;
using FlowScheduler.Infrastructure.Jobs;
using Hangfire;
using Hangfire.Console;
using Hangfire.Redis.StackExchange;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FlowScheduler.BackgroundJobs;

public class Program {
    public static async Task Main(string[] args) {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) => {
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                config.AddJsonFile("appsettings.LoggerHelper.json", optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((hostContext, services) => {
                services.AddHangfireConsoleSink();
                services.AddLoggerHelper(hostContext.Configuration);
            })
            .ConfigureServices((hostContext, services) => {

                // 1. Lettura delle impostazioni Redis dal file appsettings.json
                var redisOptions = hostContext.Configuration.GetSection("redisCacheOptions").Get<RedisCacheOptions>();



                if (redisOptions != null) {
                    // 1.  Registrazione Opzioni per l'Iniezione
                    services.AddSingleton(redisOptions);

                    // 2. Configurazione del Multiplexer Redis
                    services.AddSingleton<IConnectionMultiplexer>(sp => {
                        var configOptions = new ConfigurationOptions {
                            EndPoints = { $"{redisOptions.Host}:{redisOptions.Port}" },
                            Password = redisOptions.Password,
                            AbortOnConnectFail = false, // Evita crash all'avvio se Redis è giù
                            ConnectTimeout = redisOptions.ConnectTimeout,
                            SyncTimeout = redisOptions.SyncTimeout
                        };
                        return ConnectionMultiplexer.Connect(configOptions);
                    });
                }

                services.AddHangfire(configuration => {
                    configuration.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings()
                    .UseRedisStorage(hostContext.Configuration.GetConnectionString("Redis"), new RedisStorageOptions {
                        Db = 0,
                        Prefix = "hangfire:"
                    })
                    .UseConsole();
                });
                services.AddHangfireServer(options => {
                    options.ServerName = "flow-worker-main";
                });

                services.AddWorkerServices(hostContext.Configuration);
                // 3. Registrazione del Manager e dei Processori (SOLID)
            })
            .Build();

        // Register PerformContext filter — sets IPerformContextAccessor before each job,
        // decoupling PerformContext from the IBackgroundJobHandler Core interface.
        var accessor = host.Services.GetRequiredService<IPerformContextAccessor>();
        GlobalJobFilters.Filters.Add(new PerformContextJobFilter(accessor));

        // MCP: discover and register tools from configured MCP servers
        try {
            var mcpClient = host.Services.GetRequiredService<IMcpClientService>();
            await mcpClient.RegisterAllServersAsync();
        } catch (Exception ex) {
            var startupLogger = host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger<Program>();
            startupLogger.LogWarning(ex,
                "MCP server registration failed at startup — health monitoring may not work until servers are available");
        }
        // Recurring Jobs: proactive health monitoring (S3)
        // Use IRecurringJobManager (DI) instead of static RecurringJob API
        // because JobStorage is not initialized until the host starts.
        var monitorOptions = host.Services
            .GetRequiredService<IOptions<HealthMonitorOptions>>().Value;
        if (monitorOptions.Enabled) {
            var jobManager = host.Services.GetRequiredService<IRecurringJobManager>();
            jobManager.AddOrUpdate<HealthMonitorJob>(
                "health-monitor",
                job => job.ExecuteAsync(CancellationToken.None),
                monitorOptions.CronExpression);
        }

        await host.RunAsync();
    }
}