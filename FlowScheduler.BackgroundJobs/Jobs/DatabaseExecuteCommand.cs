using FlowScheduler.Infrastructure.Processing;
using FlowScheduler.Core.Interfaces.Jobs;
using FlowScheduler.Core.Interfaces.Messaging;
using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.Data;
using FlowScheduler.Core.Interfaces.Processing;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Interfaces.Metrics;
using FlowScheduler.Core.Models;
using FlowScheduler.Core.Shared;
using Microsoft.Agents.AI;
using DashLogLevel = FlowScheduler.Core.Shared.LogLevel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using StackExchange.Redis;
using System.Data;
using System.Diagnostics;
using System.Text;

namespace FlowScheduler.BackgroundJobs.Jobs;

public class DatabaseExecuteCommand : CommandObservable, IJobCommand {
    private readonly HangFireOptions _options;
    private readonly IJobDiagnosticAgent _agent;
    private readonly ITelegramService _telegramService;
    private readonly IConnectionMultiplexer _redis;
    private readonly IDbConnectionFactoryResolver _factoryResolver;
    private readonly AIAgent _exceptionAnalyzer;
    private readonly IResultProcessorFactory _resultProcessorFactory;
    private readonly AIAgent _localSummarizer;
    private readonly ILogger<DatabaseExecuteCommand> _logger;
    private readonly IMetricsStore _metricsStore;
    public DatabaseExecuteCommand(
        IOptions<HangFireOptions> options,
        IJobDiagnosticAgent agent,
        IHttpClientFactory httpClientFactory,
        ITelegramService telegramService,
        IConnectionMultiplexer redis,
        IDbConnectionFactoryResolver factoryResolver,
        [FromKeyedServices("ExceptionAnalyzer")] AIAgent exceptionAnalyzer,
        IResultProcessorFactory resultProcessorFactory,
        [FromKeyedServices("ErrorSummaryAgent")] AIAgent localSummarizer,
        IMetricsStore metricsStore,
        ILogger<DatabaseExecuteCommand> logger) {
        _options = options.Value;
        _agent = agent;
        _telegramService = telegramService;
        _redis = redis;
        _factoryResolver = factoryResolver;
        _exceptionAnalyzer = exceptionAnalyzer;
        _resultProcessorFactory = resultProcessorFactory;
        _localSummarizer = localSummarizer;
        _metricsStore = metricsStore;
        _logger = logger;
    }
    public async Task ExecuteAsync(CreateTaskRequest taskRequest, CancellationToken cancellationToken = default) {
        if (_options == null || string.IsNullOrEmpty(_options.ConnectionString)) {
            RaiseMessage(DashLogLevel.Error, "[DB ERROR] Stringa di connessione non configurata.");
            return;
        }
        _logger.LogInformation("[4] DB → ExecuteAsync START: SP={TaskName}", taskRequest.Name);
        RaiseMessage(DashLogLevel.Info, $"[DB] Esecuzione Stored Procedure per: {taskRequest.Name}");
        bool isAiEnabled = true;
        var stopwatch = Stopwatch.StartNew();
        try {
            string ConnectionString = taskRequest.ConnectionString ?? _options.ConnectionString;
            var dbFactory = _factoryResolver.Resolve(taskRequest.DatabaseType ?? "SqlServer");
            using (var connection = dbFactory.CreateConnection(ConnectionString)) {
                RaiseMessage(DashLogLevel.Info, $"[DB] connessione verso {connection.ConnectionString}");
                await connection.OpenAsync(cancellationToken);


                string CorrelationId = Guid.NewGuid().ToString("N");
                using (var command = dbFactory.CreateCommand(taskRequest.Name, connection)) {
                    // Esempio parametri: se il taskName serve alla procedura
                    foreach (var param in taskRequest.ParametersTask) {
                        dbFactory.AddParameter(command, param.Key, param.Value);
                    }

                    var resultProcessor = _resultProcessorFactory.GetProcessor("ResultProcessorFromDatabase");
                    var errorGroups = await resultProcessor.getErrorsGrouped(taskRequest, command.ExecuteReaderAsync(cancellationToken), cancellationToken);
                    var errorGroupsList = errorGroups.ToList();
                    var aiEnabledGroups = new List<IGrouping<string, Dictionary<string, object>>>();
                    foreach (var group in errorGroupsList) {
                        var groupRows = group.ToList();

                        isAiEnabled = groupRows.FirstOrDefault()?.ContainsKey("EnableAiAnalysis") == true
                                           && Convert.ToBoolean(groupRows.First()["EnableAiAnalysis"]);

                        RaiseMessage(DashLogLevel.Error, $"[ALERT] Gruppo '{group.Key}': {groupRows.Count} errori su {taskRequest.Name} (AI Enabled: {isAiEnabled})");

                        if (isAiEnabled) {
                            var singleGroupList = new List<IGrouping<string, Dictionary<string, object>>> { group };
                            string markdownErrors = ErrorContextFormatter.FormatErrorGroups(singleGroupList, taskRequest.Name);
                            RaiseMessage(DashLogLevel.Info, $"[AI] Avvio analisi per il gruppo: '{group.Key}'");

                            var result = await _agent.AnalyzeAsync(markdownErrors, taskRequest, CorrelationId, group.Key, markdownErrors, cancellationToken);

                            // SALVATAGGIO SU REDIS (Opzionale ma utile per la cronologia)
                            try {
                                var db = _redis.GetDatabase();
                                var redisKey = $"diag:{CorrelationId}:{group.Key.GetHashCode()}"; // Chiave unica per gruppo
                                await db.StringSetAsync(redisKey, JsonConvert.SerializeObject(result), TimeSpan.FromHours(24));
                            } catch { /* ignore */ }

                            // NOTIFICA FILTRATA: Solo se Severity >= Error
                            if (result.Severity >= DiagnosticSeverity.Error) {
                                await _telegramService.SendDiagnosticAsync(result, taskRequest.Name, taskRequest.HangFireJobName, cancellationToken);
                                RaiseMessage(DashLogLevel.Error, $"[AI AGENT] [{result.Severity}] {result.Message}");
                            } else {
                                var dashLevel = result.Severity == DiagnosticSeverity.Warning ? DashLogLevel.Warning : DashLogLevel.Info;
                                RaiseMessage(dashLevel, $"[AI AGENT] [{result.Severity}] {result.Message}");
                            }

                        } else {
                            RaiseMessage(DashLogLevel.Warning, $"[AI SKIP] Analisi AI disabilitata per il gruppo '{group.Key}' via database.");

                            string fallbackText = ErrorContextFormatter.FormatSingleGroup(group.Key, groupRows);

                            var result = new DiagnosticResult(
                                Message: fallbackText,
                                Severity: DiagnosticSeverity.Error,
                                CorrelationId: CorrelationId,
                                OriginalErrorJson: fallbackText,
                                TaskName: taskRequest.Name,
                                IsAiGenerated: false
                            );

                            RaiseMessage(DashLogLevel.Info, $"[8] DB → Risposta manuale ricevuta per '{group.Key}' ({result.Message.Length} chars) | Severity: {result.Severity}");
                            if (result.Severity >= DiagnosticSeverity.Error)
                                await _telegramService.SendDiagnosticAsync(result, taskRequest.Name, taskRequest.HangFireJobName, cancellationToken);
                        }

                    }

                    if (!errorGroupsList.Any()) {
                        RaiseMessage(DashLogLevel.Info, $"[DB] Nessun errore trovato per {taskRequest.Name}");
                    }
                }
            }
        } catch (Exception ex) {
            var errorMessage = ex.InnerException != null ? $"{ex.Message} (Inner: {ex.InnerException.Message})" : ex.Message;
            RaiseMessage(DashLogLevel.Error, $"[FATAL ERROR] {errorMessage}.");
            if (isAiEnabled) {
                RaiseMessage(DashLogLevel.Info, "Invoco l'analisi AI silenziosa via Microsoft.Extensions.AI...");
                try {
                    // ANALISI AI SILENZIOSA — Ollama locale (leggero)
                    var stackTrace = ex.StackTrace ?? "N/A";
                    if (stackTrace.Length > 1000)
                        stackTrace = stackTrace[..1000] + "...";
                    var prompt = $"Analizza l'errore del task '{taskRequest.Name}':\nTipo: {ex.GetType().Name}\nMessaggio: {ex.Message}\nStack Trace: {stackTrace}\nInner Exception: {ex.InnerException?.Message ?? "N/A"}";
                    var analysisResponse = await _exceptionAnalyzer.RunAsync(prompt);
                    var analysisResult = analysisResponse.Text ?? "L'analisi AI non ha prodotto risultati.";

                    var diagResult = new DiagnosticResult(
                        Message: analysisResult,
                        Severity: DiagnosticSeverity.Error,
                        CorrelationId: Guid.NewGuid().ToString("N"),
                        OriginalErrorJson: $"{ex.GetType().Name}: {ex.Message}",
                        TaskName: taskRequest.Name,
                        IsAiGenerated: true
                    );

                    await _telegramService.SendDiagnosticAsync(diagResult, taskRequest.Name, taskRequest.HangFireJobName, cancellationToken);
                    RaiseMessage(DashLogLevel.Info, $"[AI SYSTEM ANALYSIS] {analysisResult}");

                } catch (Exception aiEx) {
                    RaiseMessage(DashLogLevel.Error, $"[AI AGENT ERROR] Impossibile analizzare l'eccezione: {aiEx.Message}");
                }

                RaiseMessage(DashLogLevel.Error, $"[FATAL ERROR] {errorMessage}\n{ex.StackTrace}");
            }
        } finally {
            try {
                await _metricsStore.RecordAsync(MetricsConstants.CategoryJob, taskRequest.Name, stopwatch.Elapsed.TotalMilliseconds, cancellationToken);
            } catch (Exception metricsEx) {
                _logger.LogWarning(metricsEx, "[METRICS] Impossibile registrare durata job per {TaskName}", taskRequest.Name);
            }
        }
    }
}