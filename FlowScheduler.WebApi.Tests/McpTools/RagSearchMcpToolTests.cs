using NSubstitute;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Models;
using FlowScheduler.Infrastructure.AI.McpServerTools;

namespace FlowScheduler.WebApi.Tests.McpTools;

public class RagSearchMcpToolTests {
    private readonly IRagSearchService _ragSearch = Substitute.For<IRagSearchService>();
    private readonly RagSearchMcpTool _sut = new();

    [Fact]
    public async Task SearchKnowledgeAsync_WithResults_ReturnsFormattedContext() {
        _ragSearch.SearchFreeAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                  .Returns(new RagSearchResult("- [Soluzione 80%] Fix connection timeout", false, null, null));

        var result = await _sut.SearchKnowledgeAsync(_ragSearch, "connection error", 3);

        Assert.Contains("Fix connection timeout", result);
    }

    [Fact]
    public async Task SearchKnowledgeAsync_NoResults_ReturnsNoResultsMessage() {
        _ragSearch.SearchFreeAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                 .Returns(new RagSearchResult("", false, null, null));

        var result = await _sut.SearchKnowledgeAsync(_ragSearch, "unknown error", 3);

        Assert.Contains("No results found", result);
    }
}