using System.Text.Json;
using Confluent.Kafka;
using Ietp.Telemetry.Contracts;
using Ietp.Telemetry.Domain;
using Ietp.BuildingBlocks;
using Observability = Ietp.BuildingBlocks.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ietp.Telemetry.Processor;

public sealed partial class ProcessorWorker(IOptions<ProcessorOptions> options, ILogger<ProcessorWorker> logger) : BackgroundService
{
    private readonly ProcessorOptions _options = options.Value;
    private readonly string _instanceId = Environment.GetEnvironmentVariable("HOSTNAME") ?? Guid.NewGuid().ToString("N");
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private long _events;
    private long _errors;
    private double _lastLatency;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
        };
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            MessageSendMaxRetries = 8,
            MessageTimeoutMs = 30_000
        };
        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        using var producer = new ProducerBuilder<string, string>(producerConfig).Build();
        consumer.Subscribe(_options.RawTopic);
        var heartbeatTask = PublishHeartbeatsAsync(producer, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string> consumed;
            try
            {
                consumed = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            var telemetry = Deserialize(consumed.Message.Value, out var error);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            using var activity = StartConsumerActivity(consumed.Message.Headers);
            activity?.SetTag("messaging.kafka.offset", consumed.Offset.Value);
            if (telemetry is not null) error = TelemetryValidator.Validate(telemetry);

            if (telemetry is null || error is not null)
            {
                var deadLetter = new DeadLetterEvent(
                    telemetry?.EventId.ToString("D"), consumed.Message.Value, error ?? "Invalid payload.",
                    consumed.Topic, consumed.Partition.Value, consumed.Offset.Value, DateTimeOffset.UtcNow);
                await producer.ProduceAsync(_options.DeadLetterTopic, new Message<string, string>
                {
                    Key = consumed.Message.Key,
                    Value = JsonSerializer.Serialize(deadLetter)
                }, stoppingToken).ConfigureAwait(false);
                DeadLettered(logger, consumed.TopicPartitionOffset, deadLetter.Error);
                Interlocked.Increment(ref _errors);
                Observability.DeadLettered.Add(1);
            }
            else
            {
                var measurementType = telemetry.SeriesKey[(telemetry.SeriesKey.LastIndexOf('.') + 1)..];
                var processed = new ProcessedTelemetryEvent(telemetry, measurementType, DateTimeOffset.UtcNow);
                await producer.ProduceAsync(_options.ProcessedTopic, new Message<string, string>
                {
                    Key = telemetry.SeriesId.ToString("D"),
                    Value = JsonSerializer.Serialize(processed),
                    Headers = consumed.Message.Headers
                }, stoppingToken).ConfigureAwait(false);
                Observability.Processed.Add(1, new KeyValuePair<string, object?>("quality", telemetry.Quality));
            }

            Interlocked.Increment(ref _events);
            _lastLatency = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Observability.ProcessingLatency.Record(_lastLatency);

            consumer.StoreOffset(consumed);
            consumer.Commit(consumed);
        }

        consumer.Close();
        try { await heartbeatTask.ConfigureAwait(false); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        producer.Flush(stoppingToken);
    }

    private async Task PublishHeartbeatsAsync(IProducer<string, string> producer, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            var heartbeat = new ServiceHeartbeat("processor", _instanceId, "Healthy", _startedAt, DateTimeOffset.UtcNow,
                Interlocked.Read(ref _events), Interlocked.Read(ref _errors), _lastLatency);
            await producer.ProduceAsync(_options.StatusTopic, new Message<string, string>
            {
                Key = heartbeat.InstanceId, Value = JsonSerializer.Serialize(heartbeat)
            }, cancellationToken).ConfigureAwait(false);
        } while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    private static System.Diagnostics.Activity? StartConsumerActivity(Headers headers)
    {
        var traceParent = headers.LastOrDefault(header => header.Key == "traceparent")?.GetValueBytes();
        return traceParent is not null && System.Diagnostics.ActivityContext.TryParse(System.Text.Encoding.UTF8.GetString(traceParent), null, out var parent)
            ? Observability.Activities.StartActivity("kafka.consume telemetry.raw", System.Diagnostics.ActivityKind.Consumer, parent)
            : Observability.Activities.StartActivity("kafka.consume telemetry.raw", System.Diagnostics.ActivityKind.Consumer);
    }

    private static TelemetryEvent? Deserialize(string payload, out string? error)
    {
        try
        {
            var telemetry = JsonSerializer.Deserialize<TelemetryEvent>(payload);
            error = telemetry is null ? "Payload is JSON null." : null;
            return telemetry;
        }
        catch (JsonException exception)
        {
            error = $"Invalid telemetry JSON: {exception.Message}";
            return null;
        }
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Dead-lettered message at {Offset}: {Error}")]
    private static partial void DeadLettered(ILogger logger, TopicPartitionOffset offset, string error);
}
