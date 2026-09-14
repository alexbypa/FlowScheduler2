using NSubstitute;
using FlowScheduler.Core.Interfaces.Metrics;
using FlowScheduler.Core.Dtos;
using FlowScheduler.WebApi.McpTools;

namespace FlowScheduler.WebApi.Tests.McpTools;

public class MetricsQueryMcpToolTests {
    private readonly IMetricsStore _metricsStore = Substitute.For<IMetricsStore>();
    private readonly MetricsQueryMcpTool _sut = new();

    [Fact]
    public async Task QueryMetricsAsync_WithEntries_ReturnsFormattedList() {
        var entry = new MetricEntry("job", "duration", 42.5, DateTimeOffset.UtcNow);
        _metricsStore.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<MetricEntry> { entry } as IReadOnlyList<MetricEntry>);

        var result = await _sut.QueryMetricsAsync(_metricsStore, "job", "duration", 24);

        Assert.Contains("duration", result);
        Assert.Contains("42", result);
    }

    [Fact]
    public async Task QueryMetricsAsync_NoEntries_ReturnsNoMetricsMessage() {
        _metricsStore.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<MetricEntry>() as IReadOnlyList<MetricEntry>);

        var result = await _sut.QueryMetricsAsync(_metricsStore, "job", "duration", 24);

        Assert.Contains("No metrics found", result);
    }

    [Fact]
    public async Task QueryMetricsAsync_HoursBackClamped_To168Max() {
        _metricsStore.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<MetricEntry>() as IReadOnlyList<MetricEntry>);

        await _sut.QueryMetricsAsync(_metricsStore, "job", "duration", 500);

        await _metricsStore.Received(1).QueryAsync(
            "job", "duration",
            Arg.Is<DateTimeOffset>(d => d > DateTimeOffset.UtcNow.AddHours(-170)),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }
}
