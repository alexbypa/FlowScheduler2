using System.Text;
using CryptoStream.Worker.Configuration;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace CryptoStream.Worker.Services;

public sealed class RabbitMqPublisher(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqPublisher> logger) : IAsyncDisposable
{
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task InitAsync(CancellationToken ct)
    {
        var cfg = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = cfg.HostName,
            Port = cfg.Port,
            UserName = cfg.UserName,
            Password = cfg.Password
        };

        _connection = await factory.CreateConnectionAsync("CryptoStream.Publisher", ct);
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);

        await _channel.QueueDeclareAsync(
            queue: cfg.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);

        logger.LogInformation("RabbitMQ publisher connected to {Host}:{Port}, queue {Queue}",
            cfg.HostName, cfg.Port, cfg.QueueName);
    }

    public async Task PublishAsync(string json, CancellationToken ct)
    {
        if (_channel is null)
            throw new InvalidOperationException("Publisher not initialized. Call InitAsync first.");

        var body = Encoding.UTF8.GetBytes(json);
        var props = new BasicProperties { Persistent = true };

        await _channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: options.Value.QueueName,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }

        logger.LogInformation("RabbitMQ publisher disposed");
    }
}
