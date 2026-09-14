using FlowScheduler.Core.Dtos;

namespace FlowScheduler.Core.Interfaces.Processing;

public interface IResultProcessor {
    Task<IEnumerable<IGrouping<string, Dictionary<string, object>>>> getErrorsGrouped(CreateTaskRequest taskRequest, object DataSource, CancellationToken cancellationToken = default);
}
