namespace FlowScheduler.Core.Interfaces.Monitoring;

public interface IHealthMonitorService {
    Task CheckAllAsync(CancellationToken ct = default);
}