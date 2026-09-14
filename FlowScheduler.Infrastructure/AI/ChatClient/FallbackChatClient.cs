using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace FlowScheduler.Infrastructure.AI.ChatClient;

/// <summary>
/// Filtro per esewguire Ollama in caso di fallback dell' AI innerClient
/// </summary>
public class FallbackChatClient : DelegatingChatClient {
    private readonly IChatClient _fallbackClient;
    private readonly bool _GenerateContentEnabled;
    private readonly TimeSpan _rateLimitWindow;
    private readonly ILogger _logger;

    private static DateTime _cooldownUntil = DateTime.MinValue;
    private static readonly object _lock = new();

    public FallbackChatClient(IChatClient innerClient, IChatClient fallbackClient, bool GenerateContentEnabled, TimeSpan rateLimitWindow, ILogger logger) : base(innerClient) {
        _fallbackClient = fallbackClient;
        _GenerateContentEnabled = GenerateContentEnabled;
        _rateLimitWindow = rateLimitWindow;
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default) {
        if (IsInCooldown()) {
            var remaining = GetRemainingCooldown();
            _logger.LogWarning("[RATE LIMIT] Gemini in cooldown ({RemainingSeconds}s rimanenti). Deviazione diretta su Ollama.", remaining.TotalSeconds);
            return await CallFallbackAsync(chatMessages, options, cancellationToken);
        }

        // Check PRIMA della call HTTP — evita spreco API call quando LLM disabilitato
        if (!_GenerateContentEnabled) {
            _logger.LogWarning("[FALLBACK] LLM disabled da configurazione, deviazione diretta su Ollama.");
            return await CallFallbackAsync(chatMessages, options, cancellationToken);
        }

        try {
            _logger.LogInformation("[FALLBACK] Verifica Abilitazione LLM");
            var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);
            response.AdditionalProperties ??= new();
            response.AdditionalProperties["AiProvider"] = $"Gemini ({response.ModelId ?? "unknown"})";
            return response;
        } catch (Exception ex) {
            if (IsRetryableError(ex)) {
                _logger.LogError(ex, "[FALLBACK RETRYABLE] Chiamata a Gemini fallita (retryable): {Error}", ex.Message);

                if (IsRateLimitError(ex))
                    TriggerCooldown();

                if (ex.InnerException != null)
                    _logger.LogError("[FALLBACK INNER] {InnerError}", ex.InnerException.Message);
                _logger.LogWarning("[FALLBACK] Deviazione su Ollama in corso...");

                return await CallFallbackAsync(chatMessages, options, cancellationToken);
            }

            // Errore NON retryable — bug reale, log e propaga (NON mascherare con Ollama)
            _logger.LogError(ex, "[FALLBACK NON-RETRYABLE] Errore NON di rete/timeout, propagazione: {Type}: {Error}", ex.GetType().Name, ex.Message);
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {

        var updates = await CollectStreamingUpdatesAsync(chatMessages, options, cancellationToken);
        foreach (var update in updates) {
            yield return update;
        }
    }

    private async Task<List<ChatResponseUpdate>> CollectStreamingUpdatesAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options,
        CancellationToken cancellationToken) {

        var results = new List<ChatResponseUpdate>();

        if (IsInCooldown()) {
            var remaining = GetRemainingCooldown();
            _logger.LogWarning("[RATE LIMIT STREAMING] Gemini in cooldown ({RemainingSeconds}s rimanenti). Deviazione diretta su Ollama.", remaining.TotalSeconds);
            try {
                var fallbackOptions = BuildFallbackOptions(options);
                var fallbackMessages = BuildFallbackMessages(chatMessages);
                var response = await _fallbackClient.GetResponseAsync(fallbackMessages, fallbackOptions, cancellationToken);
                var text = response.Text ?? "Ollama non ha prodotto una risposta.";
                results.Add(new ChatResponseUpdate(ChatRole.Assistant, text));
            } catch (Exception fallbackEx) {
                var errorMsg = $"Errore critico: Sia Gemini (rate limited) che Ollama hanno fallito.\n\nDettaglio Ollama: {fallbackEx.Message}";
                results.Add(new ChatResponseUpdate(ChatRole.Assistant, errorMsg));
            }
            return results;
        }

        try {
            await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken)) {
                results.Add(update);
            }
            return results;
        } catch (Exception ex) {
            if (IsRetryableError(ex)) {
                _logger.LogError(ex, "[FALLBACK STREAMING RETRYABLE] Chiamata streaming a Gemini fallita (retryable): {Error}", ex.Message);

                if (IsRateLimitError(ex))
                    TriggerCooldown();

                if (ex.InnerException != null)
                    _logger.LogError("[FALLBACK INNER] {InnerError}", ex.InnerException.Message);
                _logger.LogWarning("[FALLBACK] Deviazione su Ollama (non-streaming) in corso...");

                try {
                    var fallbackOptions = BuildFallbackOptions(options);
                    var fallbackMessages = BuildFallbackMessages(chatMessages);
                    var response = await _fallbackClient.GetResponseAsync(fallbackMessages, fallbackOptions, cancellationToken);
                    var text = response.Text ?? "Ollama non ha prodotto una risposta.";
                    results.Add(new ChatResponseUpdate(ChatRole.Assistant, text));
                } catch (Exception fallbackEx) {
                    var errorMsg = $"Errore critico: Sia Gemini che Ollama hanno fallito.\n\nDettaglio Ollama: {fallbackEx.Message}";
                    results.Add(new ChatResponseUpdate(ChatRole.Assistant, errorMsg));
                }
                return results;
            }

            // Errore NON retryable — bug reale, log e propaga
            _logger.LogError(ex, "[FALLBACK STREAMING NON-RETRYABLE] Errore NON di rete/timeout, propagazione: {Type}: {Error}", ex.GetType().Name, ex.Message);
            throw;
        }
    }

    private async Task<ChatResponse> CallFallbackAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options, CancellationToken cancellationToken) {
        try {
            var fallbackOptions = BuildFallbackOptions(options);
            var fallbackMessages = BuildFallbackMessages(chatMessages);
            var response = await _fallbackClient.GetResponseAsync(fallbackMessages, fallbackOptions, cancellationToken);
            response.AdditionalProperties ??= new();
            response.AdditionalProperties["AiProvider"] = "Ollama";
            return response;
        } catch (Exception fallbackEx) {
            var errorMsg = $"Errore critico: Sia Gemini (rate limited) che Ollama hanno fallito.\n\nDettaglio Ollama: {fallbackEx.Message}";
            var errorResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, errorMsg));
            errorResponse.AdditionalProperties ??= new();
            errorResponse.AdditionalProperties["AiProvider"] = "None (Error)";
            return errorResponse;
        }
    }

    private bool IsInCooldown() {
        lock (_lock) {
            return DateTime.UtcNow < _cooldownUntil;
        }
    }

    private TimeSpan GetRemainingCooldown() {
        lock (_lock) {
            var elapsed = _cooldownUntil - DateTime.UtcNow;
            return elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
        }
    }

    private void TriggerCooldown() {
        lock (_lock) {
            _cooldownUntil = DateTime.UtcNow.Add(_rateLimitWindow);
        }
    }

    private static ChatOptions BuildFallbackOptions(ChatOptions? original) {
        return new ChatOptions {
            Temperature = original?.Temperature ?? 0.7f,
            MaxOutputTokens = 512,
        };
    }

    private static List<ChatMessage> BuildFallbackMessages(IEnumerable<ChatMessage> chatMessages) {
        var messages = chatMessages.ToList();
        var result = new List<ChatMessage>();

        var systemMsg = messages.FirstOrDefault(m => m.Role == ChatRole.System);
        if (systemMsg != null) {
            result.Add(new ChatMessage(ChatRole.System,
                systemMsg.Text + "\n\nNOTA: Il servizio AI principale non è disponibile. Rispondi in modo conciso e diretto, senza usare strumenti esterni."));
        } else {
            result.Add(new ChatMessage(ChatRole.System,
                "Sei un assistente AI di fallback. Rispondi in modo conciso e diretto."));
        }

        var recentMessages = messages
            .Where(m => m.Role != ChatRole.System)
            .TakeLast(4)
            .ToList();
        result.AddRange(recentMessages);

        return result;
    }

    /// <summary>
    /// Errori retryable: rete, timeout, rate limit, server error.
    /// Tutto il resto (NullRef, ArgumentException, serialization, ecc.) NON viene catchato
    /// e propaga al chiamante — evita di mascherare bug reali con fallback Ollama.
    /// </summary>
    private static bool IsRetryableError(Exception ex) {
        return ex is HttpRequestException
                 || ex is TaskCanceledException { InnerException: TimeoutException }
                 || ex.Message.Contains("429")
                 || ex.Message.Contains("Too Many Requests")
                 || ex.Message.Contains("503")
                 || ex.Message.Contains("502");
    }

    private static bool IsRateLimitError(Exception ex) {
        return ex.Message.Contains("429")
            || ex.Message.Contains("Too Many Requests")
            || ex.Message.Contains("503");
    }
}
