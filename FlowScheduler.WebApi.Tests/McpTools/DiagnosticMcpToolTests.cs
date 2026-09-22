using NSubstitute;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Models;
using FlowScheduler.Infrastructure.AI.McpServerTools;

namespace FlowScheduler.WebApi.Tests.McpTools;

public class DiagnosticMcpToolTests {
    private readonly IRagSearchService _ragSearch = Substitute.For<IRagSearchService>();
    private readonly DiagnosticMcpTool _sut = new();

    [Fact]
    public async Task RunDiagnosticAsync_BypassMatch_ReturnsHighConfidence() {
        var bestMatch = new RagDocument { Resolution = "Restart the Redis connection pool", Severity = "Error" };
        _ragSearch.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new RagSearchResult("- [Soluzione 85%] Restart Redis", true, bestMatch, 0.15f));

        var result = await _sut.RunDiagnosticAsync(_ragSearch, "Redis connection failed");

        Assert.Contains("HIGH CONFIDENCE MATCH", result);
        Assert.Contains("Restart the Redis connection pool", result);
        Assert.Contains("Severity: Error", result);
    }

    [Fact]
    public async Task RunDiagnosticAsync_NormalMatch_ReturnsPossibleSolutions() {
        _ragSearch.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new RagSearchResult("- [Soluzione 60%] Check connection string", false, null, 0.4f));

        var result = await _sut.RunDiagnosticAsync(_ragSearch, "SQL timeout");

        Assert.Contains("Possible solutions", result);
        Assert.Contains("Check connection string", result);
        Assert.DoesNotContain("HIGH CONFIDENCE", result);
    }

    [Fact]
    public async Task RunDiagnosticAsync_NoMatch_ReturnsNoSolutions() {
        _ragSearch.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new RagSearchResult("", false, null, null));

        var result = await _sut.RunDiagnosticAsync(_ragSearch, "unknown error xyz");

        Assert.Contains("No known solutions", result);
    }
}
