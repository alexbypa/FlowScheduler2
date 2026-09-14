using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FlowScheduler.Infrastructure.AI.ChatClient;

/// <summary>
/// Filtro MEAI che traccia tutte le chiamate al modello AI con timing e dettagli.
/// Posizionato vicino all'innerClient per catturare la chiamata effettiva a Gemini.
/// </summary>
public class ConnectionTracingFilter : DelegatingChatClient {
    private readonly ILogger _logger;

    public ConnectionTracingFilter(IChatClient innerClient, ILogger logger) : base(innerClient) {
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) {

        var msgCount = messages.Count();
        var toolCount = options?.Tools?.Count ?? 0;
        _logger.LogInformation("[AI TRACE] GetResponseAsync -> Gemini API. Messaggi: {MsgCount}, Tools: {ToolCount}", msgCount, toolCount);
        
        var sw = Stopwatch.StartNew();
        try {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            sw.Stop();
            _logger.LogInformation("[AI TRACE] GetResponseAsync <- Gemini OK in {ElapsedMs}ms. FinishReason: {Reason}",
                sw.ElapsedMilliseconds, response.FinishReason);
            return response;
        } catch (Exception ex) {
            sw.Stop();
            _logger.LogError(ex, "[AI TRACE] GetResponseAsync <- Gemini FAIL dopo {ElapsedMs}ms: {Error}", sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {

        var msgCount = messages.Count();
        var toolCount = options?.Tools?.Count ?? 0;
        _logger.LogInformation("[AI TRACE] GetStreamingResponseAsync -> Gemini API (streaming). Messaggi: {MsgCount}, Tools: {ToolCount}", msgCount, toolCount);

        var sw = Stopwatch.StartNew();
        int chunkCount = 0;
        bool firstChunk = true;

        await foreach (var chunk in base.GetStreamingResponseAsync(messages, options, cancellationToken)) {
            if (firstChunk) {
                _logger.LogInformation("[AI TRACE] Primo chunk ricevuto da Gemini dopo {ElapsedMs}ms (TTFB)", sw.ElapsedMilliseconds);
                firstChunk = false;
            }
            chunkCount++;
            yield return chunk;
        }

        sw.Stop();
        _logger.LogInformation("[AI TRACE] GetStreamingResponseAsync <- Gemini completato in {ElapsedMs}ms, {Chunks} chunks",
            sw.ElapsedMilliseconds, chunkCount);
    }
}
