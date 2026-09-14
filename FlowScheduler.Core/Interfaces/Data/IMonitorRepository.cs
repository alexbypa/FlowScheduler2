using System.Threading;
using FlowScheduler.Core.Entities;

namespace FlowScheduler.Core.Interfaces.Data;
public interface IMonitorRepository {
    Task CreateTaskAsync(MonitorTask task, CancellationToken cancellationToken = default);
    Task<IEnumerable<MonitorTask>> GetAllTasksAsync(CancellationToken cancellationToken = default);
}
