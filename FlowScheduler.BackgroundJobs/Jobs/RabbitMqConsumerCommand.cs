using System.Diagnostics;
using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Interfaces.Jobs;
using FlowScheduler.Core.Interfaces.Messaging;
using FlowScheduler.Core.Interfaces.Metrics;
using FlowScheduler.Core.Models;
using FlowScheduler.Core.Shared;
using FlowScheduler.Infrastructure.Messaging.RabbitMq;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DashLogLevel = FlowScheduler.Core.Shared.LogLevel;

namespace FlowScheduler.BackgroundJobs.Jobs;

public class RabbitMqConsumerCommand(
    RabbitMqConsumerService consumerService,
    DynamicJsonDbWriter dbWriter,
    IJobDiagnosticAgent agent,
    IMetricsStore metricsStore,
    ITelegramService telegramService,
    [FromKeyedServices("ExceptionAnalyzer")] AIAgent exceptionAnalyzer,
    ILogger<RabbitMqConsumerCommand> logger) : CommandObservable, IJobCommand
{
    public async Task ExecuteAsync(CreateTaskRequest taskRequest, CancellationToken cancellationToken = default)
    {
        var parameters = taskRequest.ParametersTask;

        if (!parameters.TryGetValue("queue", out var queue) || string.IsNullOrWhiteSpace(queue))
        {
            RaiseMessage(DashLogLevel.Error, "[RABBITMQ] Missing required parameter: 'queue'");
            return;
        }

        if (!parameters.TryGetValue("targetTable", out var targetTable) || string.IsNullOrWhiteSpace(targetTable))
        {
            RaiseMessage(DashLogLevel.Error, "[RABBITMQ] Missing required parameter: 'targetTable'");
            return;
        }

        var targetConnection = parameters.GetValueOrDefault("targetConnection") ?? taskRequest.ConnectionString;
        var databaseType = parameters.GetValueOrDefault("databaseType") ?? taskRequest.DatabaseType ?? "SqlServer";
        var batchSize = int.TryParse(parameters.GetValueOrDefault("batchSize"), out var bs) ? bs : 100;
        var flushInterval = int.TryParse(parameters.GetValueOrDefault("flushIntervalSeconds"), out var fi) ? fi : 5;

        RaiseMessage(DashLogLevel.Info,
            $"[RABBITMQ] Start Connection on {queue} → {targetTable} (batch={batchSize}, flush={flushInterval}s)");

        var stopwatch = Stopwatch.StartNew();
        var totalPersisted = 0;
        var errorCount = 0;

        try
        {
            await consumerService.ConsumeAsync(
                queueName: queue,
                onBatch: async batch =>
                {
                    try
                    {
                        await dbWriter.WriteBatchAsync(batch, targetConnection, databaseType, targetTable, cancellationToken);
                        totalPersisted += batch.Count;
                        logger.LogDebug("[RABBITMQ] Persisted {Count} to {Table} (total: {Total})",
                            batch.Count, targetTable, totalPersisted);
                    }
                    catch (Exception batchEx)
                    {
                        errorCount++;
                        RaiseMessage(DashLogLevel.Error,
                            $"[RABBITMQ] Error on batch write #{errorCount}: {batchEx.Message}");
                        throw;
                    }
                },
                batchSize: batchSize,
                flushIntervalSeconds: flushInterval,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            RaiseMessage(DashLogLevel.Info,
                $"[RABBITMQ] Closing Connection on {queue}. Total: {totalPersisted} records, {errorCount} errors, uptime: {stopwatch.Elapsed:hh\\:mm\\:ss}");
        }
        catch (Exception ex)
        {
            var errorMessage = ex.InnerException is not null
                ? $"{ex.Message} (Inner: {ex.InnerException.Message})"
                : ex.Message;

            RaiseMessage(DashLogLevel.Error, $"[RABBITMQ FATAL] {errorMessage}");

            try
            {
                var stackTrace = (ex.StackTrace ?? "N/A").Length > 1000
                    ? ex.StackTrace![..1000] + "..."
                    : ex.StackTrace ?? "N/A";

                var prompt = $"Analyze RabbitMQ consumer error for task '{taskRequest.Name}':\n" +
                             $"Queue: {queue}\nTarget: {targetTable}\n" +
                             $"Type: {ex.GetType().Name}\nMessage: {ex.Message}\n" +
                             $"Stack Trace: {stackTrace}";

                var analysisResponse = await exceptionAnalyzer.RunAsync(prompt);

                var diagResult = new DiagnosticResult(
                    Message: analysisResponse.Text ?? "AI analysis produced no results.",
                    Severity: DiagnosticSeverity.Error,
                    CorrelationId: Guid.NewGuid().ToString("N"),
                    OriginalErrorJson: $"{ex.GetType().Name}: {ex.Message}",
                    TaskName: taskRequest.Name,
                    IsAiGenerated: true);

                await telegramService.SendDiagnosticAsync(
                    diagResult, taskRequest.Name, taskRequest.HangFireJobName, cancellationToken);

                RaiseMessage(DashLogLevel.Info, $"[AI ANALYSIS] {diagResult.Message}");
            }
            catch (Exception aiEx)
            {
                RaiseMessage(DashLogLevel.Error, $"[AI ERROR] Cannot analyze exception: {aiEx.Message}");
            }
        }
        finally
        {
            try
            {
                await metricsStore.RecordAsync(
                    MetricsConstants.CategoryJob, taskRequest.Name,
                    stopwatch.Elapsed.TotalMilliseconds, cancellationToken);
            }
            catch (Exception metricsEx)
            {
                logger.LogWarning(metricsEx, "[METRICS] Cannot record duration for {TaskName}", taskRequest.Name);
            }
        }
    }
}
