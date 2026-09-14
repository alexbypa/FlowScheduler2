using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.Jobs;
using FlowScheduler.Core.Shared;

namespace FlowScheduler.BackgroundJobs.Jobs;
public class LogJobCommand : CommandObservable, IJobCommand {
    public async Task ExecuteAsync(CreateTaskRequest TaskRequest, CancellationToken cancellationToken = default) {
        RaiseMessage(LogLevel.Info, $"[LOG] Esecuzione comando per il task: {TaskRequest.Name}");
        await Task.Delay(100, cancellationToken);
        RaiseMessage(LogLevel.Error, "Simulazione errore di log.");
    }
}
