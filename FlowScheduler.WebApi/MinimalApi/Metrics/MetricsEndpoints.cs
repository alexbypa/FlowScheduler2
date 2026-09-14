using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.Metrics;

namespace FlowScheduler.WebApi.MinimalApi.Metrics;

public class MetricsEndpoints : IEndpointDefinition {
    public void DefineEndpoints(WebApplication app) {
        app.MapGet("/metrics/query", async (string category, string name, DateTimeOffset from, DateTimeOffset to, IMetricsStore metricsStore, ILogger<MetricsEndpoints> logger, CancellationToken cancellationToken) => {
            try {
                var entries = await metricsStore.QueryAsync(category, name, from, to, cancellationToken);
                return Results.Ok(entries);
            } catch (Exception ex) {
                logger.LogWarning(ex, "[METRICS] Query fallita per {Category}/{Name}", category, name);
                return Results.Ok(Array.Empty<MetricEntry>());
            }
        })
        .WithName("MetricsQuery")
        .WithTags("Metrics");
    }
}
