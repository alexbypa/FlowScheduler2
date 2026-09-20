using System.Text;
using FlowScheduler.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FlowScheduler.Infrastructure.Messaging.RabbitMq;

public class RabbitMqConsumerService : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConsumerService> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqConsumerService(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConsumerService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task ConsumeAsync(
        string queueName,
        Func<IReadOnlyList<string>, Task> onBatch,
        int batchSize,
        int flushIntervalSeconds,
        CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: _options.PrefetchCount,
            global: false,
            cancellationToken: cancellationToken);

        await _channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        var batch = new List<string>(batchSize);
        var deliveryTags = new List<ulong>(batchSize);
        var batchLock = new SemaphoreSlim(1, 1);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var json = Encoding.UTF8.GetString(ea.Body.Span);

            await batchLock.WaitAsync(cancellationToken);
            try
            {
                batch.Add(json);
                deliveryTags.Add(ea.DeliveryTag);

                if (batch.Count >= batchSize)
                    await FlushBatchAsync(batch, deliveryTags, onBatch, cancellationToken);
            }
            finally
            {
                batchLock.Release();
            }
        };

        await _channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        _logger.LogInformation("RabbitMQ consumer started on queue {Queue}, batchSize={BatchSize}, flushInterval={FlushInterval}s",
            queueName, batchSize, flushIntervalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(flushIntervalSeconds));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await batchLock.WaitAsync(CancellationToken.None);
            try
            {
                if (batch.Count > 0)
                    await FlushBatchAsync(batch, deliveryTags, onBatch, cancellationToken);
            }
            finally
            {
                batchLock.Release();
            }
        }

        // Graceful shutdown: drain remaining batch
        await batchLock.WaitAsync(CancellationToken.None);
        try
        {
            if (batch.Count > 0)
                await FlushBatchAsync(batch, deliveryTags, onBatch, CancellationToken.None);
        }
        finally
        {
            batchLock.Release();
        }
    }

    private async Task FlushBatchAsync(
        List<string> batch,
        List<ulong> deliveryTags,
        Func<IReadOnlyList<string>, Task> onBatch,
        CancellationToken cancellationToken)
    {
        var lastTag = deliveryTags[^1];
        var count = batch.Count;

        try
        {
            await onBatch(batch);

            if (_channel is not null)
                await _channel.BasicAckAsync(lastTag, multiple: true, cancellationToken);

            _logger.LogDebug("Flushed {Count} messages, acked up to {DeliveryTag}", count, lastTag);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch flush failed ({Count} messages), nacking with requeue", count);

            if (_channel is not null)
                await _channel.BasicNackAsync(lastTag, multiple: true, requeue: true, cancellationToken);

            throw;
        }
        finally
        {
            batch.Clear();
            deliveryTags.Clear();
        }
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

        GC.SuppressFinalize(this);
    }
}
