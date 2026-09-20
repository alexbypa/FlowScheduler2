using CryptoStream.Worker.Configuration;
using CryptoStream.Worker.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BinanceOptions>(builder.Configuration.GetSection("Binance"));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));

builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddSingleton<BinanceWebSocketReader>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BinanceWebSocketReader>());

builder.Services.AddHealthChecks()
    .AddCheck<WebSocketHealthCheck>("websocket");

var app = builder.Build();

app.MapHealthChecks("/healthz");

app.Run();
