using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Infrastructure.AI.Advisor;
using FlowScheduler.Infrastructure.AI.ChatClient;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowScheduler.Infrastructure.AI.ChatClient;

/// <summary>
/// Factory che produce IChatClient in base al ModelTier richiesto.
///
/// - Primary: Gemini con FallbackChatClient (fallback Ollama) — pipeline completa.
/// - Local: Ollama diretto — leggero, nessun rate limit, tool limitati.
/// - Auto: AdvisorRoutingChatClient che decide per-request in base al prompt.
///
/// SOLID — SRP: unica responsabilita = creare il client giusto per il tier.
/// SOLID — OCP: nuovi tier aggiungibili estendendo l'enum e il switch, senza toccare i consumer.
/// SOLID — DIP: dipende da IAdvisorStrategy (astrazione), non da implementazione concreta.
///
/// Performance:
/// - I client Primary, Local e Auto sono pre-costruiti al startup (singleton).
/// - La factory non crea nuove istanze a runtime, restituisce riferimenti cached.
/// - Per ModelTier.Auto, il costo aggiuntivo e solo la chiamata a IAdvisorStrategy.Evaluate() per-request.
/// </summary>
public sealed class AdvisorChatClientFactory : IChatClientFactory {
    private readonly IChatClient _primaryClient;
    private readonly IChatClient _localClient;
    private readonly IChatClient _autoRoutingClient;
    private readonly ILogger<AdvisorChatClientFactory> _logger;

    public AdvisorChatClientFactory(
        IChatClient primaryClient,
        IChatClient localClient,
        IAdvisorStrategy advisor,
        ILogger<AdvisorChatClientFactory> logger) {
        _primaryClient = primaryClient;
        _localClient = localClient;
        _logger = logger;
        _autoRoutingClient = new AdvisorRoutingChatClient(primaryClient, localClient, advisor, logger);
    }

    /// <summary>
    /// Restituisce il client appropriato per il tier specificato.
    /// Per Auto, ritorna AdvisorRoutingChatClient che decide per-request in base al prompt.
    /// </summary>
    /// <param name="tier">Tier dal descrittore dell'agente.</param>
    /// <param name="prompt">Prompt opzionale (ignorato — il routing per-request legge i messaggi direttamente).</param>
    public IChatClient GetClient(ModelTier tier, string? prompt = null) {
        return tier switch {
            ModelTier.Primary => _primaryClient,
            ModelTier.Local => _localClient,
            ModelTier.Auto => _autoRoutingClient,
            _ => _primaryClient
        };
    }

    /// <summary>
    /// Restituisce il client per un ModelTier espresso come stringa (dal config JSON).
    /// </summary>
    public IChatClient GetClient(string modelTierStr, string? prompt = null) {
        var tier = Enum.TryParse<ModelTier>(modelTierStr, ignoreCase: true, out var parsed)
            ? parsed
            : ModelTier.Primary;

        return GetClient(tier, prompt);
    }
}
