using CSharpEssentials.LoggerHelper;
using CSharpEssentials.LoggerHelper.Sink.HangfireConsole;
using FlowScheduler.BackgroundJobs.Extensions;
using FlowScheduler.BackgroundJobs.Jobs;
using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.MCP;
using FlowScheduler.Infrastructure.Jobs;
using FlowScheduler.Infrastructure.Persistence;
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
            //1 -- LoggerHelper
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

                // 2 -- Lettura delle impostazioni Redis dal file appsettings.json
                services.AddAppRedis(hostContext.Configuration);

                // 3 -- Configurazione di Hangfire con Redis come storage
                services.AddAppHangfire();
                services.AddHangfireServer(options => {
                    options.ServerName = "flow-worker-main";
                });

                // 4 -- Registrazione del Manager e dei Processori
                services.AddWorkerServices(hostContext.Configuration);
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