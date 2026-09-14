// Core/Dtos/HealthCheckResult.cs
namespace FlowScheduler.Core.Dtos;

public sealed record HealthCheckResult {
    public string? HealthJson { get; init; }
    public string? DoraJson { get; init; }
    public string? CiJson { get; init; }
    public string? DependenciesJson { get; init; }
    public string? CodeScanningJson { get; init; }
}