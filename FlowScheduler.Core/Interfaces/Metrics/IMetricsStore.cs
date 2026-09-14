using FlowScheduler.Core.Dtos;

namespace FlowScheduler.Core.Interfaces.Metrics;

public interface IMetricsStore {
    Task RecordAsync(string category, string name, double value, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MetricEntry>> QueryAsync(string category, string name, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
