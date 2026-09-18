using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Infrastructure.AI.ChatClient;
using FlowScheduler.Infrastructure.AI.Middleware;
using FlowScheduler.Infrastructure.AI.Registry;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace FlowScheduler.Infrastructure.AI.Agents;

/// <summary>
/// Registrazione agenti AI guidata dalla configurazione JSON.
/// Sostituisce AiAgentsExtension.cs eliminando tutto l'hardcoding.
///
/// Flusso:
/// 1. Legge AiOrchestrationOptions.Agents dal DI (bind da appsettings.json).
/// 2. Per ogni AgentDescriptor con Enabled=true:
///    a. Risolve i tool dal IToolRegistry per nome.
///    b. Costruisce ricorsivamente i sub-agenti (anch'essi dichiarati come AgentDescriptor).
///    c. Seleziona l'IChatClient dal AdvisorChatClientFactory in base al ModelTier.
///    d. Registra l'agente come keyed service con AddAIAgent().
///    e. Applica tutti gli IAgentMiddleware registrati.
///
/// SOLID — OCP: aggiungere un agente = aggiungere una sezione JSON. Zero codice.
/// SOLID — SRP: responsabilita unica = leggere config e registrare agenti nel DI.
/// SOLID — DIP: dipende da IToolRegistry, IAdvisorStrategy (astrazioni), non da tool concreti.
///
/// Performance:
/// - Tutta la logica esegue al startup (DI registration + factory lambda).
/// - Le lambda catturano riferimenti, nessuna allocazione per-request.
/// - I tool vengono risolti dal DI scoped quando l'agente viene effettivamente costruito.
/// </summary>
public static class ConfigurableAgentsExtension {


    /// <summary>
    /// Metodo di estensione da chiamare DOPO che AiOrchestrationOptions e stato registrato.
    /// Registra tutti gli agenti dichiarati nella configurazione come keyed services.
    /// </summary>
    public static void AddConfigurableAgentsFromOptions(
        this IServiceCollection services, AiOrchestrationOptions options) {
        services.AddSingleton<AgentFactory>();

        foreach (var (key, descriptor) in options.Agents) {
            if (!descriptor.Enabled)
                continue;

            services.AddAIAgent(key, (sp, serviceKey) => {
                var factory = sp.GetRequiredService<AgentFactory>();
                return factory.Build(descriptor, options, sp);
            });
        }
    }
}

/// <summary>
/// Factory per la costruzione di agenti AI a partire da un AgentDescriptor.
/// Singleton nel DI, risolve tool e sub-agenti on-demand.
///
/// SOLID — SRP: responsabilita unica = costruire un AIAgent da un descrittore.
/// </summary>
public sealed class AgentFactory {
    private readonly ILogger<AgentFactory> _logger;

    public AgentFactory(ILogger<AgentFactory> logger) {
        _logger = logger;
    }

    /// <summary>
    /// Costruisce un AIAgent dal descrittore, risolvendo tool e sub-agenti dal DI.
    /// </summary>
    public AIAgent Build(
        AgentDescriptor descriptor,
        AiOrchestrationOptions allOptions,
        IServiceProvider sp) {
        var toolRegistry = sp.GetRequiredService<IToolRegistry>();
        var chatClientFactory = sp.GetRequiredService<IChatClientFactory>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var middlewares = sp.GetServices<IAgentMiddleware>();

        // 1. Seleziona il client in base al ModelTier
        var chatClient = chatClientFactory.GetClient(descriptor.ModelTier);

        // 2. Risolvi i tool diretti dal registry
        var tools = new List<AITool>();
        foreach (var toolName in descriptor.Tools) {
            var tool = toolRegistry.Resolve(toolName, sp);
            if (tool != null) {
                tools.Add(tool);
                _logger.LogDebug("[AgentFactory] Tool '{Tool}' assegnato a '{Agent}'", toolName, descriptor.Name);
            } else {
                _logger.LogWarning("[AgentFactory] Tool '{Tool}' non trovato per agent '{Agent}'", toolName, descriptor.Name);
            }
        }

        // 3. Costruisci e aggiungi sub-agenti come AIFunction
        foreach (var subAgentKey in descriptor.SubAgents) {
            if (!allOptions.Agents.TryGetValue(subAgentKey, out var subDescriptor)) {
                _logger.LogWarning("[AgentFactory] Sub-agent '{SubAgent}' non trovato nella configurazione", subAgentKey);
                continue;
            }

            if (!subDescriptor.Enabled) {
                _logger.LogInformation("[AgentFactory] Sub-agent '{SubAgent}' disabilitato, skip", subAgentKey);
                continue;
            }

            // Costruzione ricorsiva del sub-agente
            var subAgent = Build(subDescriptor, allOptions, sp);
            tools.Add(subAgent.AsAIFunction());
            _logger.LogDebug("[AgentFactory] Sub-agent '{SubAgent}' aggiunto come tool a '{Agent}'",
                subAgentKey, descriptor.Name);
        }

        // 4. Costruisci l'agente
        var agent = chatClient.AsAIAgent(new() {
            Id = descriptor.Id,
            Name = descriptor.Name,
            Description = descriptor.Description,
            ChatOptions = new() {
                Instructions = descriptor.Instructions,
                Tools = tools.Count > 0 ? tools : null
            }
        }, loggerFactory: loggerFactory, services: sp);

        // 5. Applica middleware
        return agent.WithMiddlewares(middlewares);
    }
}

