// Core/Interfaces/AI/IRagBridgeService.cs
using FlowScheduler.Core.Dtos;

namespace FlowScheduler.Core.Interfaces.AI;

public interface IRagBridgeService {
    Task IngestHealthReportAsync(HealthCheckResult data, string owner, string repo, CancellationToken ct = default);
    Task<string> FormatHealthReportAsync(HealthCheckResult data, string owner, string repo, CancellationToken ct = default);
}