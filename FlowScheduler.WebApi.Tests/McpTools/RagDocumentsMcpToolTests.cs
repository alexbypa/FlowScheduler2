using NSubstitute;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Models;
using FlowScheduler.WebApi.McpTools;

namespace FlowScheduler.WebApi.Tests.McpTools;

public class RagDocumentsMcpToolTests {
    private readonly IVectorStoreService _vectorStore = Substitute.For<IVectorStoreService>();
    private readonly RagDocumentsMcpTool _sut = new();

    [Fact]
    public async Task ListRagDocumentsAsync_WithDocs_ReturnsFormattedList() {
        var doc = new RagDocument { Title = "Redis Guide", Context = "library", Category = "database", CreatedAt = new DateTime(2026, 1, 15) };
        _vectorStore.SearchLibraryAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((new List<RagDocument> { doc } as IReadOnlyList<RagDocument>, 1L));

        var result = await _sut.ListRagDocumentsAsync(_vectorStore);

        Assert.Contains("Redis Guide", result);
        Assert.Contains("Page 1", result);
        Assert.Contains("1 total documents", result);
    }

    [Fact]
    public async Task ListRagDocumentsAsync_NoDocs_ReturnsNoDocumentsMessage() {
        _vectorStore.SearchLibraryAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((new List<RagDocument>() as IReadOnlyList<RagDocument>, 0L));

        var result = await _sut.ListRagDocumentsAsync(_vectorStore);

        Assert.Equal("No documents found", result);
    }

    [Fact]
    public async Task ListRagDocumentsAsync_EmptyTitle_FallsBackToSource() {
        var doc = new RagDocument { Title = "", Source = "my-source", Context = "ops", Category = "errors", CreatedAt = new DateTime(2026, 3, 1) };
        _vectorStore.SearchLibraryAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((new List<RagDocument> { doc } as IReadOnlyList<RagDocument>, 1L));

        var result = await _sut.ListRagDocumentsAsync(_vectorStore);

        Assert.Contains("my-source", result);
        Assert.DoesNotContain("[]", result);
    }
}
