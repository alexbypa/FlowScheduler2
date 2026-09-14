using FlowScheduler.Infrastructure.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;

namespace FlowScheduler.Infrastructure.AI.Transport;

/// <summary>
/// Layer 1: Registrazione HttpClient nominati per i servizi AI.
/// Ogni client ha il LoggingHttpHandler per tracciabilita.
/// </summary>
public static class AiTransportExtension {
    public static IServiceCollection AddAiTransport(this IServiceCollection services) {
        services.AddTransient<LoggingHttpHandler>();

        services.AddHttpClient("OllamaClient")
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(3))
            .AddHttpMessageHandler<LoggingHttpHandler>();

        services.AddHttpClient("AiChatClient")
            .AddHttpMessageHandler<LoggingHttpHandler>()
            .AddResilienceHandler("ai-chat-retry", (builder, context) => {
                var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AiChatRetry");
                builder.AddRetry(new HttpRetryStrategyOptions {
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.FromMinutes(1),
                    BackoffType = DelayBackoffType.Constant,
                    // Solo 503 (transient): ritentare ha senso.
                    // 429 (quota giornaliera): ritentare brucia richieste inutilmente.
                    // Il FallbackChatClient gestisce 429 con cooldown + fallback Ollama.
                    ShouldHandle = args => ValueTask.FromResult(
                        args.Outcome.Result?.StatusCode is
                            System.Net.HttpStatusCode.ServiceUnavailable
                    ),
                    OnRetry = async args => {
                        var body = args.Outcome.Result?.Content != null
                            ? await args.Outcome.Result.Content.ReadAsStringAsync()
                            : "no body";
                        logger.LogWarning("[AI CHAT RETRY] Attempt {Attempt}/{Max}, Status: {Status}, Delay: {Delay}s, Body: {Body}",
                            args.AttemptNumber, 2, (int?)args.Outcome.Result?.StatusCode, args.RetryDelay.TotalSeconds, body);
                    }
                });
            });

        services.AddHttpClient("TelegramClient")
            .AddHttpMessageHandler<LoggingHttpHandler>();

        services.AddHttpClient("EmbeddingClient")
            .AddHttpMessageHandler<LoggingHttpHandler>();

        return services;
    }
}
