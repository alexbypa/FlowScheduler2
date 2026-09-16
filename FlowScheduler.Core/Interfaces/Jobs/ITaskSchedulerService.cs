using System.Threading;
using FlowScheduler.Core.Dtos;

namespace FlowScheduler.Core.Interfaces.Jobs;
public interface ITaskSchedulerService {
    Task CreateTaskAsync(CreateTaskRequest task, TimeSpan[] retryIntervals, CancellationToken cancellationToken = default);
}
