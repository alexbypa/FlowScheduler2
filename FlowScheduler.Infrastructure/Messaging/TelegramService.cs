using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace FlowScheduler.Infrastructure.Messaging;

public class TelegramService : ITelegramService {
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HangFireOptions _Options;
    private readonly ILogger<TelegramService> _logger;
    private readonly string _chatId = "5615062004";
    public TelegramService(IHttpClientFactory httpClientFactory, IOptions<HangFireOptions> Options, ILogger<TelegramService> logger) {
        _httpClientFactory = httpClientFactory;
        _Options = Options.Value;
        _logger = logger;
    }

    public async Task SendMessageAsync(string chatId, string text, CancellationToken cancellationToken = default) {
        var client = _httpClientFactory.CreateClient("TelegramClient");
        var payload = new {
            chat_id = chatId,
            text = text,
            parse_mode = "HTML"
        };
        await client.PostAsJsonAsync($"https://api.telegram.org/bot{_Options.Telegram.BotChatId}:{_Options.Telegram.BotToken}/sendMessage", payload, cancellationToken);
    }

    public async Task<int> SendMessageWithIdAsync(string chatId, string text, CancellationToken cancellationToken = default) {
        var client = _httpClientFactory.CreateClient("TelegramClient");
        var payload = new {
            chat_id = chatId,
            text = text,
            parse_mode = "HTML"
        };
        var response = await client.PostAsJsonAsync($"https://api.telegram.org/bot{_Options.Telegram.BotChatId}:{_Options.Telegram.BotToken}/sendMessage", payload, cancellationToken);
        var content = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken);
        return content.GetProperty("result").GetProperty("message_id").GetInt32();
    }

    public async Task UpdateMessageAsync(string chatId, int messageId, string text, CancellationToken cancellationToken = default) {
        var client = _httpClientFactory.CreateClient("TelegramClient");
        var payload = new {
            chat_id = chatId,
            message_id = messageId,
            text = text,
            parse_mode = "HTML"
        };
        await client.PostAsJsonAsync($"https://api.telegram.org/bot{_Options.Telegram.BotChatId}:{_Options.Telegram.BotToken}/editMessageText", payload, cancellationToken);
    }

    public async Task SendWithInlineKeyboardAsync(string chatId, string text, Dictionary<string, string> buttons, CancellationToken cancellationToken = default) {
        var client = _httpClientFactory.CreateClient("TelegramClient");
        var inlineButtons = buttons.Select(b => new { text = b.Key, callback_data = b.Value }).ToArray();
        var payload = new {
            chat_id = chatId,
            text = text,
            parse_mode = "HTML",
            reply_markup = new {
                inline_keyboard = new[] { inlineButtons }
            }
        };
        var response = await client.PostAsJsonAsync($"https://api.telegram.org/bot{_Options.Telegram.BotChatId}:{_Options.Telegram.BotToken}/sendMessage", payload, cancellationToken);
        if (!response.IsSuccessStatusCode) {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("[TELEGRAM ERROR] {StatusCode} - {ErrorContent}", response.StatusCode, errorContent);
        }
    }

    public async Task SendDiagnosticAsync(DiagnosticResult result, string taskName, string jobName, CancellationToken cancellationToken = default) {
        var client = _httpClientFactory.CreateClient("TelegramClient");

        var sanitizedAiMessage = EscapeHtml(result.Message);
        // Usiamo HTML invece di Markdown per evitare errori di parsing con caratteri speciali
        var text = $"<b>DIAGNOSI AI</b>\n\n" +
                   $"<b>job Name</b>{EscapeHtml(jobName)}\n" +
                   $"<b>Task:</b> {EscapeHtml(taskName)}\n" +
                   $"<b>Analisi:</b> {sanitizedAiMessage}";

        var payload = new Dictionary<string, object> {
            ["chat_id"] = _chatId,
            ["text"] = text,
            ["parse_mode"] = "HTML"
        };

        if (result.IsAiGenerated) {
            payload["reply_markup"] = new {
                inline_keyboard = new[] {
                    new[] {
                        new { text = "Approva", callback_data = $"confirm_fix|{result.CorrelationId}" },
                        new { text = "Ignora", callback_data = $"ignore_fix|{result.CorrelationId}" }
                    }
                }
            };
        }

        _logger.LogInformation("[TELEGRAM] Sending diagnostic for job '{JobName}' and task '{TaskName}' with correlation ID '{CorrelationId}'", jobName, taskName, result.CorrelationId);

        var response = await client.PostAsJsonAsync($"https://api.telegram.org/bot{_Options.Telegram.BotChatId}:{_Options.Telegram.BotToken}/sendMessage", payload, cancellationToken);

        if (!response.IsSuccessStatusCode) {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("[TELEGRAM ERROR] {StatusCode} - {ErrorContent}", response.StatusCode, errorContent);
        }
    }
    private string EscapeHtml(string text) {
        if (string.IsNullOrEmpty(text))
            return text;
        return text
          .Replace("&", "&amp;") // Deve essere il primo!
          .Replace("<", "&lt;")
          .Replace(">", "&gt;");
    }
}
