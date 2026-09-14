using FlowScheduler.BackgroundJobs.Consumers;
using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace FlowScheduler.BackgroundJobs.Hosting;

public class TelegramPollingService : BackgroundService {
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly StackExchange.Redis.IConnectionMultiplexer _redis;
    private readonly HangFireOptions _Options;
    private readonly ILogger<TelegramPollingService> _logger;
    private int _lastUpdateId = 0;
    public TelegramPollingService(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider, StackExchange.Redis.IConnectionMultiplexer redis, IOptions<HangFireOptions> options, ILogger<TelegramPollingService> logger) {
        _httpClientFactory = httpClientFactory;
        _serviceProvider = serviceProvider;
        _redis = redis;
        _Options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var client = _httpClientFactory.CreateClient("TelegramClient");
        client.Timeout = TimeSpan.FromSeconds(45);

        _logger.LogInformation("[TELEGRAM POLL] Avvio servizio di polling per BotId: {BotChatId}", _Options.Telegram.BotChatId);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                var url = $"https://api.telegram.org/bot{_Options.Telegram.BotChatId}:{_Options.Telegram.BotToken}/getUpdates?offset={_lastUpdateId + 1}&timeout=30";
                var response = await client.GetFromJsonAsync<TelegramUpdateResponse>(url, stoppingToken);

                if (response is { Ok: true, Result: not null } && response.Result.Count > 0) {
                    _logger.LogInformation("[TELEGRAM POLL] Ricevuti {UpdateCount} nuovi update.", response.Result.Count);
                    foreach (var update in response.Result) {
                        _lastUpdateId = update.UpdateId;

                        if (update.CallbackQuery != null) {
                            _logger.LogInformation("[TELEGRAM POLL] Trovato CallbackQuery: ID={CallbackId}, Data={CallbackData}", update.CallbackQuery.Id, update.CallbackQuery.Data);
                            await HandleCallbackQuery(update.CallbackQuery);
                        } else if (update.Message != null && !string.IsNullOrWhiteSpace(update.Message.Text)) {
                            _logger.LogInformation("[TELEGRAM POLL] Ricevuto Messaggio da {ChatId}: {Text}", update.Message.Chat.Id, update.Message.Text);
                            await HandleUserMessage(update.Message);
                        } else {
                            _logger.LogWarning("[TELEGRAM POLL] Update ricevuto non gestito (UpdateId: {UpdateId})", update.UpdateId);
                        }
                    }
                } else if (response is { Ok: false }) {
                    _logger.LogWarning("[TELEGRAM POLL] API returned error (Ok=false).");
                }

                // Ritardo precauzionale di 1 secondo tra i long polling
                await Task.Delay(5000, stoppingToken);
            } catch (Exception ex) {
                _logger.LogError(ex, "[TELEGRAM POLL ERROR] Eccezione nel loop: {Error} su bot {BotChatId}", ex.Message, _Options.Telegram.BotChatId);
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task HandleUserMessage(TelegramMessage message) {
        using var scope = _serviceProvider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<TelegramChatHandler>();
        await handler.HandleMessageAsync(message.Chat.Id, message.Text);
    }

    private async Task HandleCallbackQuery(TelegramCallbackQuery callback) {
        using var scope = _serviceProvider.CreateScope();
        var httpClient = _httpClientFactory.CreateClient("TelegramClient");
        var answerUrl = $"https://api.telegram.org/bot{_Options.Telegram.BotChatId}:{_Options.Telegram.BotToken}/answerCallbackQuery?callback_query_id={callback.Id}";

        _logger.LogInformation("[TELEGRAM CALLBACK] Ricevuto callback_data: {CallbackData}", callback.Data);

        if (callback.Data.StartsWith("confirm_fix|")) {
            var correlationId = callback.Data.Split('|')[1];
            _logger.LogInformation("[TELEGRAM CALLBACK] [APPROVA] Inizio elaborazione per CorrelationId: {CorrelationId}", correlationId);

            // 1. RECUPERO DATI DA REDIS
            var db = _redis.GetDatabase();
            var redisKey = $"diag:{correlationId}";
            var cachedData = await db.StringGetAsync(redisKey);

            if (cachedData.IsNullOrEmpty) {
                _logger.LogError("[TELEGRAM CALLBACK] [ERROR] Dati non trovati in Redis per chiave: {RedisKey}", redisKey);
                await httpClient.GetAsync($"{answerUrl}&text=Errore: Sessione scaduta o dati mancanti.");
                return;
            }

            try {
                var diagnostic = Newtonsoft.Json.JsonConvert.DeserializeObject<DiagnosticResult>(cachedData!);

                if (diagnostic == null) {
                    _logger.LogError("[TELEGRAM CALLBACK] [ERROR] Deserializzazione fallita per {CorrelationId}", correlationId);
                    await httpClient.GetAsync($"{answerUrl}&text=Errore interno di decodifica.");
                    return;
                }

                _logger.LogInformation("[TELEGRAM CALLBACK] Dati recuperati: Task={TaskName}, Soluzione={SolutionLength} chars", diagnostic.TaskName, diagnostic.Message.Length);

                // 2. CHIAMATA WEBAPI (URL INTERNO DOCKER sulla porta 8080)
                var confirmRequest = new {
                    CorrelationId = correlationId,
                    TaskName = diagnostic.TaskName,
                    ErrorsJson = diagnostic.OriginalErrorJson,
                    AiSolution = diagnostic.Message
                };

                _logger.LogInformation("[TELEGRAM CALLBACK] Invio conferma a WebApi: http://flow-webapi:8080/rag/confirm-solution");
                var webApiResponse = await httpClient.PostAsJsonAsync("http://flow-webapi:8080/rag/confirm-solution", confirmRequest);

                if (webApiResponse.IsSuccessStatusCode) {
                    _logger.LogInformation("[TELEGRAM CALLBACK] [SUCCESS] Soluzione appresa correttamente.");
                    await httpClient.GetAsync($"{answerUrl}&text=Grazie! Soluzione appresa.");
                } else {
                    var error = await webApiResponse.Content.ReadAsStringAsync();
                    _logger.LogError("[TELEGRAM CALLBACK] [ERROR] WebApi ha risposto {StatusCode}: {Error}", webApiResponse.StatusCode, error);
                    await httpClient.GetAsync($"{answerUrl}&text=Errore durante l'apprendimento lato server.");
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "[TELEGRAM CALLBACK] [FATAL] Eccezione: {Error}", ex.Message);
                await httpClient.GetAsync($"{answerUrl}&text=Errore imprevisto.");
            }
        } else if (callback.Data.StartsWith("ignore_fix|")) {
            var correlationId = callback.Data.Split('|')[1];
            _logger.LogInformation("[TELEGRAM CALLBACK] [IGNORA] Utente ha ignorato la soluzione per {CorrelationId}", correlationId);
            await httpClient.GetAsync($"{answerUrl}&text=Operazione ignorata.");
        } else {
            _logger.LogWarning("[TELEGRAM CALLBACK] [UNKNOWN] Ricevuto callback_data non gestito: {CallbackData}", callback.Data);
            await httpClient.GetAsync($"{answerUrl}&text=Azione non riconosciuta.");
        }
    }
}

// Classi di supporto per il JSON di Telegram con mappatura corretta dei nomi snake_case
public record TelegramUpdateResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("ok")] bool Ok,
    [property: System.Text.Json.Serialization.JsonPropertyName("result")] List<TelegramUpdate> Result);

public record TelegramUpdate(
    [property: System.Text.Json.Serialization.JsonPropertyName("update_id")] int UpdateId,
    [property: System.Text.Json.Serialization.JsonPropertyName("callback_query")] TelegramCallbackQuery? CallbackQuery,
    [property: System.Text.Json.Serialization.JsonPropertyName("message")] TelegramMessage? Message);

public record TelegramMessage(
    [property: System.Text.Json.Serialization.JsonPropertyName("message_id")] int MessageId,
    [property: System.Text.Json.Serialization.JsonPropertyName("text")] string? Text,
    [property: System.Text.Json.Serialization.JsonPropertyName("chat")] TelegramChat Chat);

public record TelegramChat(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")] long Id);

public record TelegramCallbackQuery(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")] string Id,
    [property: System.Text.Json.Serialization.JsonPropertyName("data")] string Data);
