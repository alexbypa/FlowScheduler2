using Microsoft.Extensions.AI;

namespace FlowScheduler.WebApi.Tests;

/// <summary>Generatore deterministico per test (nessuna chiamata esterna).</summary>
internal sealed class FixedDimensionTestEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>> {
    readonly int _dimensions;

    public FixedDimensionTestEmbeddingGenerator(int dimensions) {
        _dimensions = dimensions;
        Metadata = new EmbeddingGeneratorMetadata("test", new Uri("http://localhost/"), "test-model", dimensions);
    }

    public EmbeddingGeneratorMetadata Metadata { get; }

    public void Dispose() { }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default) {
        var list = new List<Embedding<float>>();
        var idx = 0;
        foreach (var _ in values) {
            var vec = new float[_dimensions];
            vec[0] = 1f / (idx + 1);
            list.Add(new Embedding<float>(vec));
            idx++;
        }

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list));
    }
}
