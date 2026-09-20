using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CryptoStream.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace CryptoStream.Worker.Services;

public sealed class BinanceWebSocketReader(
    IOptions<BinanceOptions> options,
    RabbitMqPublisher publisher,
    ILogger<BinanceWebSocketReader> logger) : BackgroundService
{
    private const int ReconnectBaseDelayMs = 1000;
    private const int ReconnectMaxDelayMs = 30_000;
    private const int ReceiveBufferSize = 4096;

    public bool IsConnected { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await publisher.InitAsync(ct);

        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndConsumeAsync(ct);
                attempt = 0; // reset on clean disconnect
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                attempt++;
                var delay = Math.Min(ReconnectBaseDelayMs * (1 << attempt), ReconnectMaxDelayMs);
                logger.LogWarning(ex, "WebSocket disconnected. Reconnecting in {Delay}ms (attempt {Attempt})", delay, attempt);
                await Task.Delay(delay, ct);
            }
        }

        logger.LogInformation("BinanceWebSocketReader stopped");
    }

    private async Task ConnectAndConsumeAsync(CancellationToken ct)
    {
        var cfg = options.Value;

        logger.LogInformation($"Connecting to Binance WebSocket: {cfg.WebSocketUrl} and subscribing to streams: {string.Join(", ", cfg.Symbols)}");

        var streams = string.Join("/", cfg.Symbols.Select(s => $"{s}@trade"));
        var uri = new Uri($"{cfg.WebSocketUrl}/stream?streams={streams}");

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(uri, ct);
        IsConnected = true;
        logger.LogInformation("Connected to Binance WebSocket: {Uri}", uri);

        var buffer = new byte[ReceiveBufferSize];
        var messageBuffer = new StringBuilder();

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(buffer, ct);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                logger.LogInformation("Binance sent close frame");
                break;
            }

            messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage)
            {
                var json = messageBuffer.ToString();
                messageBuffer.Clear();

                var normalized = NormalizeTradeJson(json);
                logger.LogInformation("NORMALIZED: {Result}", normalized is not null ? "OK" : "NULL");
                if (normalized is not null)
                    await publisher.PublishAsync(normalized, ct);
            }
        }
    }

    /// <summary>
    /// Extracts "data" from Binance combined stream wrapper and remaps single-letter keys
    /// to readable column names matching the CryptoTrades table schema.
    /// </summary>
    private string? NormalizeTradeJson(string rawJson) {
        try {
            using var doc = JsonDocument.Parse(rawJson);
            logger.LogInformation("Received Binance trade message: {Json}", rawJson);
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return null;

            if (!data.TryGetProperty("e", out var eventType) || eventType.GetString() != "trade")
                return null;

            var trade = new Dictionary<string, object?> {
                ["event_type"] = data.GetProperty("e").GetString(),
                ["event_time"] = data.GetProperty("E").GetInt64(),
                ["symbol"] = data.GetProperty("s").GetString(),
                ["trade_id"] = data.GetProperty("t").GetInt64(),
                ["price"] = data.GetProperty("p").GetString(),
                ["quantity"] = data.GetProperty("q").GetString(),
                ["trade_time"] = data.GetProperty("T").GetInt64(),
                ["is_buyer_maker"] = data.GetProperty("m").GetBoolean()
            };
            return JsonSerializer.Serialize(trade);
        } catch (Exception) {
            return null;
        }
    }
}
