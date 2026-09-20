namespace CryptoStream.Worker.Configuration;

public class BinanceOptions
{
    public string WebSocketUrl { get; set; } = "wss://stream.binance.com:9443";
    public string[] Symbols { get; set; } = ["btcusdt"];
}
