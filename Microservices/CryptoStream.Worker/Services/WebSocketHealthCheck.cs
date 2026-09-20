using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CryptoStream.Worker.Services;

public sealed class WebSocketHealthCheck(BinanceWebSocketReader reader) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var result = reader.IsConnected
            ? HealthCheckResult.Healthy("WebSocket connected")
            : HealthCheckResult.Unhealthy("WebSocket disconnected");

        return Task.FromResult(result);
    }
}
