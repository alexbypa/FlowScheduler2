using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Interfaces.Messaging;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;

namespace FlowScheduler.BackgroundJobs.Consumers;

/// <summary>
/// Gestisce la conversazione diretta tra l'utente Telegram e l'IA (MEAI).
/// Implementa i concetti di Thread (storia persistente) e Streaming (risposta live).
/// </summary>
public class TelegramChatHandler {
    private readonly AIAgent _agent;
    private readonly ITelegramService _telegramService;
    private readonly ILogger<TelegramChatHandler> _logger;
    private readonly IChatHistoryStore _historyStore;
    public TelegramChatHandler(
        [FromKeyedServices("MainAgent")] AIAgent agent,
        ITelegramService telegramService,
        IChatHistoryStore historyStore,
        ILogger<TelegramChatHandler> logger) {
        _agent = agent;
        _telegramService = telegramService;
        _historyStore = historyStore;
        _logger = logger;
    }

    public async Task HandleMessageAsync(long chatId, string userText) {
        var conversationId = chatId.ToString();
        _logger.LogInformation("[TELEGRAM AI] Messaggio ricevuto da {ChatId}: {Text}", conversationId, userText);

        try {
            // 1. CARICAMENTO STORIA (THREAD)
            _logger.LogInformation("[TELEGRAM FLOW] Step 1: Caricamento storia da Redis per conversazione {Id}", conversationId);
            var history = await _historyStore.GetHistoryAsync(conversationId);
            _logger.LogInformation("[TELEGRAM FLOW] Step 1: Storia caricata, {Count} messaggi esistenti", history.Count);

            // Aggiungiamo il nuovo messaggio dell'utente
            if (history.Count == 0) {
                history.Add(
                    new ChatMessage(ChatRole.System,
                    "Sei FlowScheduler AI, un assistente DevOps esperto. Hai accesso a strumenti reali (Tool) per: interrogare il database, leggere codice sorgente da GitHub, analizzare log SQL tecnici e riassumere errori business. Se l'utente ti chiede informazioni tecniche, USA SEMPRE i tool a tua disposizione prima di rispondere.")
                );
            }

            var userMessage = new ChatMessage(ChatRole.User, userText);
            history.Add(userMessage);
            await _historyStore.AddMessageAsync(conversationId, userMessage);

            // 2. MESSAGGIO DI ATTESA E STREAMING
            _logger.LogInformation("[TELEGRAM FLOW] Step 2: Invio messaggio di attesa a Telegram API");
            int messageId = await _telegramService.SendMessageWithIdAsync(chatId.ToString(), "<i>Sto pensando...</i>");
            _logger.LogInformation("[TELEGRAM FLOW] Step 2: Messaggio inviato, messageId={MsgId}", messageId);

            var fullResponse = new StringBuilder();
            var lastUpdate = DateTime.UtcNow;
            int chunkCount = 0;

            // 3. ESECUZIONE STREAMING (Minerva Style)
            _logger.LogInformation("[TELEGRAM FLOW] Step 3: Avvio streaming verso MainAgent");
            await foreach (var update in _agent.RunStreamingAsync(history)) {
                if (!string.IsNullOrEmpty(update.Text)) {
                    fullResponse.Append(update.Text);
                    chunkCount++;
                    // Aggiorniamo Telegram ogni ~1.5 secondi per evitare rate limit
                    if (chunkCount % 10 == 0 && (DateTime.UtcNow - lastUpdate).TotalMilliseconds > 1500) {
                        await _telegramService.UpdateMessageAsync(chatId.ToString(), messageId, fullResponse.ToString() + " ▌");
                        lastUpdate = DateTime.UtcNow;
                    }
                }
            }

            // 4. RISPOSTA FINALE
            _logger.LogInformation("[TELEGRAM FLOW] Step 4: Streaming completato, {Chunks} chunks ricevuti, {Length} caratteri totali", chunkCount, fullResponse.Length);
            var finalAiText = fullResponse.ToString();
            await _telegramService.UpdateMessageAsync(chatId.ToString(), messageId, finalAiText);

            // 5. SALVATAGGIO STORIA AGGIORNATA
            await _historyStore.AddMessageAsync(conversationId, new ChatMessage(ChatRole.Assistant, finalAiText));

            _logger.LogInformation("[TELEGRAM AI] Conversazione conclusa per {ChatId}", chatId);
        } catch (Exception ex) {
            _logger.LogError(ex, "[TELEGRAM AI] Errore durante la gestione del messaggio per {ChatId}", chatId);
            await _telegramService.SendMessageAsync(chatId.ToString(), "⚠️ Scusa, si è verificato un errore tecnico.");
        }
    }
}
