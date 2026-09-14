using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Infrastructure.AI.Advisor;
using FlowScheduler.Infrastructure.AI.Agents;
using FlowScheduler.Infrastructure.AI.ChatClient;
using FlowScheduler.Infrastructure.AI.Middleware;
using FlowScheduler.Infrastructure.AI.RAG;
using FlowScheduler.Infrastructure.AI.Registry;
using FlowScheduler.Infrastructure.AI.Storage;
using FlowScheduler.Infrastructure.AI.Tools;
using FlowScheduler.Infrastructure.AI.Transport;
using FlowScheduler.Infrastructure.Database;
using FlowScheduler.Infrastructure.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using StackExchange.Redis;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Headers;

namespace FlowScheduler.Infrastructure.AI;

/// <summary>
/// Orchestratore della pipeline AI — compone i 7 layer nell'ordine di dipendenza.
/// Non contiene logica di registrazione propria: delega a ogni layer extension.
///
/// Transport -> ChatClient -> Storage -> Tools -> Middleware -> Agents -> Consumers
/// </summary>
public static class AiPipelineExtension {
    public static void AddAiPipeline(this IServiceCollection services, IConfiguration configuration) {
        var hangFireOptions = configuration.GetSection("HangFireOptions").Get<HangFireOptions>() ?? new HangFireOptions();
        services.AddSingleton(hangFireOptions);

        var orchestrationOptions = configuration
            .GetSection(AiOrchestrationOptions.SectionName)
            .Get<AiOrchestrationOptions>() ?? new AiOrchestrationOptions();
        services.AddSingleton(orchestrationOptions);
        // Calcola i tool referenziati da almeno un agente abilitato
        var enabledToolNames = orchestrationOptions.Agents
            .Where(a => a.Value.Enabled)
            .SelectMany(a => a.Value.Tools)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        services.AddSingleton<IToolRegistry>(sp => {
            var registry = new ToolRegistry(sp.GetRequiredService<ILogger<ToolRegistry>>());
            registry.RegisterDefaults(enabledToolNames);
            return registry;
        });
        services.AddSingleton<IAdvisorStrategy>(sp =>
            new ComplexityAdvisorStrategy(orchestrationOptions.Advisor, sp.GetRequiredService<ILogger<ComplexityAdvisorStrategy>>())
        );

        // Layer 1: Transport HTTP
        services.AddAiTransport();
        // Layer 2: Chat Client Pipeline
        services.AddAiChatClientPipeline(hangFireOptions);

        // AdvisorChatClientFactory — dopo Layer 2 (primary client disponibile)
        services.AddSingleton<AdvisorChatClientFactory>(sp => {
            var primaryClient = sp.GetRequiredService<IChatClient>();
            var ollamaEndpoint = AiChatClientExtension.BuildOllamaEndpoint(hangFireOptions.Ollama?.Host);
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var localClient = new OllamaChatClient(ollamaEndpoint, hangFireOptions.Ollama?.Model ?? "llama3.2:1b", httpClientFactory.CreateClient("OllamaClient"));
            var advisor = sp.GetRequiredService<IAdvisorStrategy>();
            return new AdvisorChatClientFactory(primaryClient, localClient, advisor, sp.GetRequiredService<ILogger<AdvisorChatClientFactory>>());
        });
        services.AddSingleton<IChatClientFactory>(sp => sp.GetRequiredService<AdvisorChatClientFactory>());

        // Layer 3: Storage (ContentStore, VectorStore, ChatHistory, Embedding)
        services.AddAiStorage(hangFireOptions);
        // Layer 4: Tools
        services.AddAiTools(hangFireOptions);
        // Layer 5: Middleware
        services.AddAiMiddleware();
        // Layer 6: Agents (registrati direttamente nei consumer, per ora)
        //Obsolet: // AiAgentsExtension.AddAiAgents(services); 
        services.AddConfigurableAgentsFromOptions(orchestrationOptions);
        // Layer 7: Consumers (registrati direttamente nei job HangFire, per ora)
        services.AddAiConsumers();
    }
}