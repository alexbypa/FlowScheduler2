using System.Threading;
using FlowScheduler.Core.Dtos;

namespace FlowScheduler.Core.Interfaces.Jobs;
public interface ITaskSchedulerService {
    Task<IEnumerable<CreateTaskRequest>> GetAllTasksAsync(CancellationToken cancellationToken = default);
    Task CreateTaskAsync(CreateTaskRequest task, TimeSpan[] retryIntervals, CancellationToken cancellationToken = default);
}
