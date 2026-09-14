using System.Threading;
using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.Jobs;

namespace FlowScheduler.Infrastructure.Persistence;

public class InMemoryMonitorRepository : ITaskSchedulerService {
    // Simuliamo un database in memoria
    private readonly List<CreateTaskRequest> _tasks = new();
    public async Task<IEnumerable<CreateTaskRequest>> GetAllTasksAsync(CancellationToken cancellationToken = default) {
        return await Task.FromResult(_tasks);
    }
    public async Task CreateTaskAsync(CreateTaskRequest task, TimeSpan[] retryIntervals, CancellationToken cancellationToken = default) {
        _tasks.Add(task);
        await Task.CompletedTask;
    }
    public async Task InitializeAsync() {
        await Task.CompletedTask;
    }
}