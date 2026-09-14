using FlowScheduler.Core.Dtos;

namespace FlowScheduler.Core.Interfaces.Jobs;

/// <summary>
/// Domain contract for executing a scheduled background job.
/// Hangfire-specific attributes belong on the concrete implementation only.
/// </summary>
public interface IBackgroundJobHandler {
    Task RunJob(string jobName, CreateTaskRequest taskRequest, TimeSpan[] retriesInterval);
}
