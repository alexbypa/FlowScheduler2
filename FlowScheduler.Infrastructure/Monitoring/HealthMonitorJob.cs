using FlowScheduler.Core.Interfaces.Monitoring;
using Microsoft.Extensions.Logging;

namespace FlowScheduler.Infrastructure.Jobs;

public sealed class HealthMonitorJob {
    private readonly IHealthMonitorService _healthMonitorService;
    private readonly ILogger<HealthMonitorJob> _logger;
    public HealthMonitorJob(IHealthMonitorService healthMonitorService, ILogger<HealthMonitorJob> logger) {
        _healthMonitorService = healthMonitorService;
        _logger = logger;
    }
    public async Task ExecuteAsync(CancellationToken ct) {
        _logger.LogInformation("Executing HealthMonitorJob");
        await _healthMonitorService.CheckAllAsync(ct);
    }
}