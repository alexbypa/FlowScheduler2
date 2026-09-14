using FlowScheduler.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlowScheduler.WebApi.Configuration;

internal static class AiSettingsConfiguration {
    public static (HangFireOptions Options, string KeySource) Resolve(IConfiguration configuration) {
        var options = configuration.GetSection("HangFireOptions").Get<HangFireOptions>() ?? new HangFireOptions();

        string? key = null;
        var source = "none";
        // Priorità: .env / variabili d'ambiente (docker-compose) prima di appsettings.json
        key = Environment.GetEnvironmentVariable("HangFireOptions__OpenAI__ApiKey");
        if (!string.IsNullOrWhiteSpace(key)) {
            source = "env:HangFireOptions__OpenAI__ApiKey";
        }

        if (string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(options.OpenAI.ApiKey)) {
            key = options.OpenAI.ApiKey;
            source = "appsettings:HangFireOptions:OpenAI:ApiKey";
        }

        if (string.IsNullOrWhiteSpace(key)) {
            key = configuration["HangFireOptions:OpenAI:ApiKey"];
            if (!string.IsNullOrWhiteSpace(key)) {
                source = "appsettings:HangFireOptions:OpenAI:ApiKey";
            }
        }

        options.OpenAI.ApiKey = key?.Trim() ?? string.Empty;
        return (options, source);
    }

    public static void LogResolvedKey(HangFireOptions options, string keySource, ILogger logger) {
        var suffix = KeySuffix(options.OpenAI.ApiKey, 6);
        logger.LogInformation("[AI] ApiKey origine={KeySource} | suffisso ...{Suffix} | lunghezza={KeyLength}",
            keySource, suffix, options.OpenAI.ApiKey.Length);
    }

    public static void EnsureApiKeyOrThrow(HangFireOptions options) {
        if (!string.IsNullOrWhiteSpace(options.OpenAI.ApiKey)) return;

        throw new InvalidOperationException(
            "Chiave API Gemini mancante. Aggiungi nel file .env alla root del repository:\n" +
            "  AiSettings__ApiKey=LA_TUA_CHIAVE\n" +
            "oppure:\n" +
            "  HangFireOptions__OpenAI__ApiKey=LA_TUA_CHIAVE\n" +
            "Poi riavvia FlowScheduler.WebApi.");
    }

    static string KeySuffix(string? key, int chars) {
        if (string.IsNullOrEmpty(key)) return "(vuota)";
        return key.Length <= chars ? key : key[^chars..];
    }
}
