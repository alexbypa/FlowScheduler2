using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.Jobs;
using FlowScheduler.Core.Shared;

namespace FlowScheduler.BackgroundJobs.Jobs;
public class RedisConsumerCommand : CommandObservable, IJobCommand {
    public async Task ExecuteAsync(CreateTaskRequest taskRequest, CancellationToken cancellationToken = default) {
        RaiseMessage(LogLevel.Info, $"[REDIS] Consumo messaggi per il task: {taskRequest}");
        await Task.Delay(100, cancellationToken);
    }
}