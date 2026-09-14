using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.Jobs;
using FlowScheduler.Core.Shared;

namespace FlowScheduler.BackgroundJobs.Jobs;
public class HttpCallCommand : CommandObservable, IJobCommand {
    private readonly IHttpClientFactory _httpClientFactory;
    public HttpCallCommand(IHttpClientFactory httpClientFactory) {
        _httpClientFactory = httpClientFactory;
    }
    public async Task ExecuteAsync(CreateTaskRequest TaskRequest, CancellationToken cancellationToken = default) {
        RaiseMessage(LogLevel.Info, $"[HTTP] Esecuzione comando per il task: {TaskRequest.Name}");
        await Task.Delay(100, cancellationToken);
    }
}
