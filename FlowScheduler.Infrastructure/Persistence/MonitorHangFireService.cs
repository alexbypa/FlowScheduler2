using System.Threading;
using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.Jobs;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FlowScheduler.Infrastructure.Persistence;

/// <summary>
/// Hangfire-based implementation of ITaskSchedulerService.
/// Schedules recurring jobs via IRecurringJobManager.
/// Lives in Infrastructure because it depends on Hangfire (an infrastructure detail).
/// </summary>
public class MonitorHangFireService : ITaskSchedulerService {
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly ILogger<MonitorHangFireService> _logger;

    public MonitorHangFireService(IRecurringJobManager recurringJobManager, ILogger<MonitorHangFireService> logger) {
        _recurringJobManager = recurringJobManager;
        _logger = logger;
    }

    public Task CreateTaskAsync(CreateTaskRequest task, TimeSpan[] retryIntervals, CancellationToken cancellationToken = default) {
        _logger.LogInformation("[API-SERVICE] Creazione Job : comando {JobName} di tipo {CommandType} con parametri {Parameters}", task.HangFireJobName, task.CommandType, string.Join(",", task.ParametersTask));

        _recurringJobManager.AddOrUpdate<IBackgroundJobHandler>(
            task.HangFireJobName,
            handler => handler.RunJob(
                task.HangFireJobName,
                task,
                retryIntervals
            ),
            task.CronExpression,
            new RecurringJobOptions { MisfireHandling = MisfireHandlingMode.Ignorable }
        );

        return Task.CompletedTask;
    }
}
