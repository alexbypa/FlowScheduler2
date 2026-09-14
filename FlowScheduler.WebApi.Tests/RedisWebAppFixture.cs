using FlowScheduler.WebApi;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.Redis;

namespace FlowScheduler.WebApi.Tests;

public sealed class RedisWebAppFixture : IAsyncLifetime {
    RedisContainer? _redis;

    public WebApplicationFactory<Program>? Factory { get; private set; }
    public HttpClient? Client { get; private set; }

    public async Task InitializeAsync() {
        if (!DockerEnv.IsDockerRunning()) {
            return;
        }

        _redis = new RedisBuilder().Build();
        await _redis.StartAsync();

        var host = _redis.Hostname;
        var port = _redis.GetMappedPublicPort(6379);

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, config) => {
                config.AddInMemoryCollection(new Dictionary<string, string?> {
                    ["RedisCacheOptions:Host"] = host,
                    ["RedisCacheOptions:Port"] = port.ToString(),
                    ["RedisCacheOptions:Password"] = "",
                    ["RedisCacheOptions:ConnectTimeout"] = "60000",
                    ["RedisCacheOptions:SyncTimeout"] = "60000",
                    ["RedisCacheOptions:ConnectRetry"] = "5",
                    ["RedisCacheOptions:AbortOnConnectFail"] = "false",
                    ["AiSettings:EmbeddingDimension"] = "768",
                });
            });
            builder.ConfigureTestServices(services => {
                services.RemoveAll(typeof(IEmbeddingGenerator<string, Embedding<float>>));
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                    _ => new FixedDimensionTestEmbeddingGenerator(768));
            });
        });

        Client = Factory.CreateClient();
    }

    public async Task DisposeAsync() {
        Client?.Dispose();
        Factory?.Dispose();
        if (_redis is not null) {
            await _redis.DisposeAsync();
        }
    }
}

[CollectionDefinition("redis")]
public class RedisCollection : ICollectionFixture<RedisWebAppFixture> { }
