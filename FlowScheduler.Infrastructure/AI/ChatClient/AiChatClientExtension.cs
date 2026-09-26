using FlowScheduler.Core.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace FlowScheduler.Infrastructure.AI.ChatClient;

/// <summary>
/// Layer 2: Pipeline IChatClient con Gemini (primary) + Ollama (fallback).
/// Catena: OpenAI -> Logging -> ContextLimitFilter -> FunctionInvocation -> ConnectionTracingFilter -> FallbackChatClient
/// </summary>
public static class AiChatClientExtension {
    public static IServiceCollection AddAiChatClientPipeline(this IServiceCollection services) {
        services.AddChatClient(sp => {
            var hangFireOptions = sp.GetRequiredService<HangFireOptions>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("AiChatClient");
            var ollamaEndpoint = BuildOllamaEndpoint(hangFireOptions.Ollama?.Host);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var initLogger = loggerFactory.CreateLogger("AI Initialization");

            initLogger.LogInformation("[DEBUG INIT] Configurazione EMBEDDING in corso...");
            initLogger.LogInformation($"[DEBUG INIT] Endpoint: '{hangFireOptions.OpenAI.EmbeddingEndpoint}'");
            initLogger.LogInformation($"[DEBUG INIT] ModelId: '{hangFireOptions.OpenAI.EmbeddingModelId}'");
            initLogger.LogInformation($"[DEBUG INIT] Dimensioni: {hangFireOptions.OpenAI.EmbeddingDimension}");
            initLogger.LogInformation("[DEBUG DI] Avvio configurazione IChatClient");
            initLogger.LogInformation($"[DEBUG DI] OpenAI ApiKey present: {!string.IsNullOrEmpty(hangFireOptions.OpenAI?.ApiKey)}");
            initLogger.LogInformation($"[DEBUG DI] OpenAI Endpoint: '{hangFireOptions.OpenAI?.GenerateContentEndpoint}'");
            initLogger.LogInformation($"[DEBUG DI] OpenAI GenerateContentModelId: '{hangFireOptions.OpenAI?.GenerateContentModelId}'");
            initLogger.LogInformation($"[DEBUG DI] Ollama Host: '{ollamaEndpoint}'");
            initLogger.LogInformation($"[DEBUG DI] Ollama Model: '{hangFireOptions.Ollama?.Model}'");

            var openAIClient = new OpenAIClient(
                new ApiKeyCredential(hangFireOptions.OpenAI.ApiKey),
                new OpenAIClientOptions() {
                    Endpoint = new Uri(hangFireOptions.OpenAI.GenerateContentEndpoint),
                    Transport = new HttpClientPipelineTransport(httpClient)
                }
            );
            IChatClient mainClient = openAIClient.GetChatClient(hangFireOptions.OpenAI.GenerateContentModelId).AsIChatClient();
            IChatClient backupClient = new OllamaChatClient(ollamaEndpoint, hangFireOptions.Ollama.Model, httpClientFactory.CreateClient("OllamaClient"));

            // TODO: UseDistributedCache disabilitata temporaneamente — le chiavi cache MEAI (hash interni)
            // non vengono svuotate con FlowAI:* e servono risposte stale con modello sbagliato.
            // Riattivare dopo aver stabilizzato il modello Gemini.
            return mainClient.AsBuilder()
                .UseLogging(loggerFactory)
                .Use(inner => new ContextLimitFilter(inner))
                .UseFunctionInvocation(loggerFactory, options => options.MaximumIterationsPerRequest = 8)
                .Use(inner => new ConnectionTracingFilter(inner, loggerFactory.CreateLogger("AI.ConnectionTracing")))
                .Use(inner => new FallbackChatClient(inner, backupClient, hangFireOptions.OpenAI.GenerateContentEnabled, hangFireOptions.OpenAI.RateLimitWindow, loggerFactory.CreateLogger<FallbackChatClient>()))
                //.UseDistributedCache(cache)
                .Build();
        });

        return services;
    }

    /// <summary>
    /// Costruisce l'endpoint Ollama normalizzando host, protocollo e porta.
    /// </summary>
    public static Uri BuildOllamaEndpoint(string? host) {
        var finalHost = host ?? "localhost";
        if (!finalHost.StartsWith("http"))
            finalHost = $"http://{finalHost}";
        if (finalHost.IndexOf(":", 6) == -1)
            finalHost = finalHost.TrimEnd('/') + ":11434";
        return new Uri(finalHost);
    }
}
