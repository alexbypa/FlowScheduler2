namespace FlowScheduler.Core.Dtos;

public record MetricEntry(string Category, string Name, double Value, DateTimeOffset Timestamp);