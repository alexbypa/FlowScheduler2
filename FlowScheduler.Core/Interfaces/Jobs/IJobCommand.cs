using System.Threading;
using FlowScheduler.Core.Dtos;

namespace FlowScheduler.Core.Interfaces.Jobs;

public interface IJobCommand {
    Task ExecuteAsync(CreateTaskRequest TaskRequest, CancellationToken cancellationToken = default);
}
