using CSharpEssentials.LoggerHelper.Sink.HangfireConsole;
using FlowScheduler.Core.Interfaces.Jobs;
using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Shared;
using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using DashLogLevel = FlowScheduler.Core.Shared.LogLevel;

namespace FlowScheduler.BackgroundJobs.Jobs;

public class BackgroundJobHandler : IBackgroundJobHandler {
    private readonly IInjectCommandFactory _injectCommandFactory;
    private readonly HangFireOptions _options;
    private readonly IConnectionMultiplexer _redis;
    private readonly IPerformContextAccessor _performContextAccessor;
    private readonly ILogger<BackgroundJobHandler> _logger;
    public BackgroundJobHandler(IInjectCommandFactory injectCommandFactory, IOptions<HangFireOptions> options, IConnectionMultiplexer redis, IPerformContextAccessor performContextAccessor, ILogger<BackgroundJobHandler> logger) {
        _injectCommandFactory = injectCommandFactory;
        _options = options.Value;
        _redis = redis;
        _performContextAccessor = performContextAccessor;
        _logger = logger;
    }
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new int[] { 10, 30, 60 })]
    [JobDisplayName("{0}")]
    [DisableConcurrentExecution(300)]
    public async Task RunJob(string jobName, CreateTaskRequest taskRequest, TimeSpan[] retriesInterval) {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var performcontext = _performContextAccessor.Current;
        performcontext?.WriteLine($"🔒 DisableConcurrentExecution timeout: 300s | Start: {DateTime.UtcNow:HH:mm:ss}");
        var jobId = performcontext?.BackgroundJob?.Id ?? "N/A";
        performcontext?.WriteLine($"[DEDUP] RunJob START | Job={jobName} | JobId={jobId} | Time={DateTime.UtcNow:O}");

        var db = _redis.GetDatabase();
        var dedupKey = $"hangfire:dedup:{jobName}";
        var isFirst = await db.StringSetAsync(dedupKey, jobId, TimeSpan.FromMinutes(2), When.NotExists);
        if (!isFirst) {
            _logger.LogWarning("[DEDUP] SKIP duplicato | Job={JobName} | JobId={JobId}", jobName, jobId);
            performcontext?.WriteLine($"⚠️ Esecuzione duplicata rilevata — SKIP");
            BackgroundJob.Delete(jobId);
            return;
        }

        _logger.LogInformation("[1] HANGFIRE → RunJob START: {TaskName} | CommandType: {CommandType}", taskRequest.Name, taskRequest.CommandType);
        performcontext?.WriteLine($" >>> WORKER ENGINE V.{_options.Version} - EXEC: {taskRequest.Name} <<< ");
        if (_options == null) {
            performcontext?.SetTextColor(ConsoleTextColor.Red);
            performcontext?.WriteLine("[FATAL ERROR] HangFireOptions non configurate.");
            throw new InvalidOperationException("HangFireOptions non configurate.");
        }

        var command = _injectCommandFactory.GetCommandByName(taskRequest.CommandType);
        _logger.LogInformation("[2] FACTORY → Comando risolto: {CommandName}", command.GetType().Name);

        WriteTextOnDashboardHandler handler = (level, e) => {
            switch (level) {
                case DashLogLevel.Info:
                    performcontext?.SetTextColor(ConsoleTextColor.Gray); break;
                case DashLogLevel.Warning:
                    performcontext?.SetTextColor(ConsoleTextColor.Yellow); break;
                case DashLogLevel.Error:
                    performcontext?.SetTextColor(ConsoleTextColor.Red); break;
                case DashLogLevel.Fatal:
                    performcontext?.SetTextColor(ConsoleTextColor.DarkRed); break;
            }
            performcontext?.WriteLine(e.Text);
            performcontext?.ResetTextColor();
        };

        var observableCommand = command as ICommandObservable;
        try {
            if (observableCommand != null && performcontext != null) {
                observableCommand.OnWriteText += handler;
            }
            await command!.ExecuteAsync(taskRequest);
        } catch (Exception ex) {
            performcontext?.SetTextColor(ConsoleTextColor.Red);
            _logger.LogError(ex, "[3] HANGFIRE → RunJob FAILED: {Error}", ex.Message);
            throw; // Rilancia per permettere i retry di Hangfire //TODO: usare retriesInterval !!!
        } finally {
            if (observableCommand != null) {
                observableCommand.OnWriteText -= handler;
            }
            _performContextAccessor.Clear();
            sw.Stop();
            performcontext?.WriteLine($"⏱️ Job completato in {sw.Elapsed.TotalSeconds:F1}s (Timeout lock: 30s)");
        }
    }
}