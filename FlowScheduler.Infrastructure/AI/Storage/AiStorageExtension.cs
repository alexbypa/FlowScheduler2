using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Infrastructure.AI.RAG;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using StackExchange.Redis;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace FlowScheduler.Infrastructure.AI.Storage;

/// <summary>
/// Layer 3: Storage — ChatHistory, ContentStore, VectorStore, EmbeddingGenerator.
/// Tutto basato su Redis per persistenza in-memory ad alte prestazioni.
/// </summary>
public static class AiStorageExtension {
    public static IServiceCollection AddAiStorage(this IServiceCollection services) {
        services.AddSingleton<IChatHistoryStore, RedisChatHistoryStore>();
        services.AddScoped<IContentStore, RedisContentStore>();

        // Embedding Generator (Google AI Studio via OpenAI-compatible SDK)
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp => {
            var hangFireOptions = sp.GetRequiredService<HangFireOptions>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("EmbeddingClient");

            var openAIClient = new OpenAIClient(
                new ApiKeyCredential(hangFireOptions.OpenAI.ApiKey),
                new OpenAIClientOptions {
                    Endpoint = new Uri(hangFireOptions.OpenAI.EmbeddingEndpoint),
                    Transport = new HttpClientPipelineTransport(httpClient)
                }
            );
            return openAIClient.GetEmbeddingClient(hangFireOptions.OpenAI.EmbeddingModelId).AsIEmbeddingGenerator();
        });

        // Vector Store (Redis Search)
        services.AddSingleton<IVectorStoreService, RedisVectorStoreService>(sp => {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var hangFireOptions = sp.GetRequiredService<HangFireOptions>();
            return new RedisVectorStoreService(redis, hangFireOptions, sp.GetRequiredService<ILoggerFactory>().CreateLogger<RedisVectorStoreService>());
        });

        return services;
    }
}
