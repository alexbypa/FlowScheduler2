using System.Net;
using System.Net.Http.Json;
using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowScheduler.WebApi.Tests;

[Collection("redis")]
public class MetricsEndpointsTests(RedisWebAppFixture fixture) {

    [SkippableFact]
    public async Task Query_before_any_data_returns_empty_array() {
        Skip.If(fixture.Client is null, "Docker non disponibile: eseguire i test con Docker avviato.");

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"));
        var res = await fixture.Client!.GetAsync($"/metrics/query?category=job&name=NoSuchTask&from={from}&to={to}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var entries = await res.Content.ReadFromJsonAsync<List<MetricEntry>>();
        Assert.NotNull(entries);
        Assert.Empty(entries!);
    }

    [SkippableFact]
    public async Task Query_after_record_returns_seeded_value() {
        Skip.If(fixture.Client is null, "Docker non disponibile: eseguire i test con Docker avviato.");

        using var scope = fixture.Factory!.Services.CreateScope();
        var metricsStore = scope.ServiceProvider.GetRequiredService<IMetricsStore>();
        await metricsStore.RecordAsync("job", "TestSp", 42.5);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"));
        var res = await fixture.Client!.GetAsync($"/metrics/query?category=job&name=TestSp&from={from}&to={to}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var entries = await res.Content.ReadFromJsonAsync<List<MetricEntry>>();
        Assert.NotNull(entries);
        Assert.Single(entries!);
        Assert.Equal(42.5, entries![0].Value);
    }
}
