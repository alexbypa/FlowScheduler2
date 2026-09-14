using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace FlowScheduler.Infrastructure.AI.Advisor;

/// <summary>
/// DelegatingChatClient che decide a REQUEST TIME quale modello usare (Primary o Local).
///
/// Risolve il problema build-time vs request-time:
///   - AdvisorChatClientFactory seleziona il client al DI build time (nessun prompt disponibile).
///   - Questo client differisce la decisione a GetResponseAsync (prompt disponibile).
///
/// Chain di decisione per ogni request:
///   1. Estrae testo ultimo messaggio utente.
///   2. Chiama IAdvisorStrategy.Evaluate(prompt) → ModelTier.
///   3. Guardia tool: se Advisor→Local MA request ha tools, forza Primary
///      (Ollama modelli piccoli non supportano function calling).
///   4. Forwarda a Primary (InnerClient, pipeline completa) o Local (Ollama diretto).
///
/// SOLID — SRP: unica responsabilita = routing per-request, nessuna logica di valutazione.
/// SOLID — DIP: dipende da IAdvisorStrategy (astrazione Core), non da implementazione concreta.
/// SOLID — OCP: nuove strategie di routing sostituibili senza modificare questo client.
/// </summary>
public sealed class AdvisorRoutingChatClient : DelegatingChatClient {
    private readonly IChatClient _localClient;
    private readonly IAdvisorStrategy _advisor;
    private readonly ILogger _logger;

    public AdvisorRoutingChatClient(
        IChatClient primaryClient,
        IChatClient localClient,
        IAdvisorStrategy advisor,
        ILogger logger)
        : base(primaryClient) {
        _localClient = localClient;
        _advisor = advisor;
        _logger = logger;
    }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) {

        var target = ResolveTarget(chatMessages, options);
        return target.GetResponseAsync(chatMessages, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {

        var target = ResolveTarget(chatMessages, options);
        await foreach (var update in target.GetStreamingResponseAsync(chatMessages, options, cancellationToken)) {
            yield return update;
        }
    }

    /// <summary>
    /// Valuta il prompt e decide il target client per questa request.
    /// InnerClient = Primary (pipeline completa: Logging → ContextLimit → FunctionInvocation → ConnectionTracing → Fallback).
    /// _localClient = Ollama diretto (leggero, no pipeline).
    /// </summary>
    private IChatClient ResolveTarget(IEnumerable<ChatMessage> messages, ChatOptions? options) {
        var prompt = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
        var tier = _advisor.Evaluate(prompt ?? string.Empty);

        // Guardia: tools presenti → forza Primary (Ollama modelli piccoli non supportano function calling)
        bool hasTools = options?.Tools?.Count > 0;
        if (tier == ModelTier.Local && hasTools) {
            _logger.LogWarning("[AdvisorRouting] Advisor→Local ma request ha {ToolCount} tools, forcing Primary (Ollama no function calling)",
                options!.Tools!.Count);
            tier = ModelTier.Primary;
        }

        _logger.LogWarning("[AdvisorRouting] Routing → {Tier} (prompt {Chars} chars, tools={HasTools})",
            tier, prompt?.Length ?? 0, hasTools);

        return tier == ModelTier.Local ? _localClient : InnerClient;
    }
}
