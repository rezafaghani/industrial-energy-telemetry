using System.Text.Json;
using Confluent.Kafka;
using Ietp.Telemetry.Contracts;
using Ietp.Telemetry.Domain;
using Ietp.BuildingBlocks;
using Observability = Ietp.BuildingBlocks.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Ietp.Telemetry.Inserter;

public sealed class InserterWorker(IOptions<InserterOptions> options) : BackgroundService
{
    private readonly InserterOptions _options = options.Value;
    private readonly string _instanceId = Environment.GetEnvironmentVariable("HOSTNAME") ?? Guid.NewGuid().ToString("N");
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private long _events;
    private double _lastLatency;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(_options.ConnectionString);
        await DatabaseMigrator.MigrateAsync(dataSource, stoppingToken).ConfigureAwait(false);
        await RegistryInitializer.EnsureAsync(dataSource, stoppingToken).ConfigureAwait(false);
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers, EnableIdempotence = true, Acks = Acks.All
        }).Build();
        var statusTask = ConsumeStatusesAsync(dataSource, stoppingToken);
        var heartbeatTask = PublishHeartbeatsAsync(producer, stoppingToken);
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers, GroupId = _options.GroupId,
            EnableAutoCommit = false, EnableAutoOffsetStore = false, AutoOffsetReset = AutoOffsetReset.Earliest,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
        }).Build();
        consumer.Subscribe(_options.Topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string> first;
            try { first = consumer.Consume(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            var messages = new List<ConsumeResult<string, string>>(_options.BatchSize) { first };
            while (messages.Count < _options.BatchSize && consumer.Consume(TimeSpan.Zero) is { } next) messages.Add(next);

            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            await PersistAsync(dataSource, messages, stoppingToken).ConfigureAwait(false);
            Observability.Inserted.Add(messages.Count);
            Interlocked.Add(ref _events, messages.Count);
            _lastLatency = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Observability.InsertionLatency.Record(_lastLatency);
            foreach (var last in messages.GroupBy(message => message.Partition).Select(group => group.MaxBy(message => message.Offset.Value)!))
            {
                consumer.StoreOffset(last);
            }
            consumer.Commit();
        }
        consumer.Close();
        try { await Task.WhenAll(statusTask, heartbeatTask).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task PublishHeartbeatsAsync(IProducer<string, string> producer, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            var heartbeat = new ServiceHeartbeat("inserter", _instanceId, "Healthy", _startedAt, DateTimeOffset.UtcNow,
                Interlocked.Read(ref _events), 0, _lastLatency);
            await producer.ProduceAsync(_options.StatusTopic, new Message<string, string>
            {
                Key = heartbeat.InstanceId, Value = JsonSerializer.Serialize(heartbeat)
            }, cancellationToken).ConfigureAwait(false);
        } while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task ConsumeStatusesAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers, GroupId = "telemetry-status-persister-v1",
            EnableAutoCommit = false, AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();
        consumer.Subscribe(_options.StatusTopic);
        while (!cancellationToken.IsCancellationRequested)
        {
            ConsumeResult<string, string> consumed;
            try { consumed = consumer.Consume(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            var heartbeat = JsonSerializer.Deserialize<ServiceHeartbeat>(consumed.Message.Value)
                ?? throw new InvalidDataException("Service heartbeat is JSON null.");
            await PersistHeartbeatAsync(dataSource, heartbeat, cancellationToken).ConfigureAwait(false);
            consumer.Commit(consumed);
        }
        consumer.Close();
    }

    private static async Task PersistHeartbeatAsync(NpgsqlDataSource dataSource, ServiceHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var instanceCommand = connection.CreateCommand())
        {
            instanceCommand.Transaction = transaction;
            instanceCommand.CommandText = """
            INSERT INTO service_instances(service_name,instance_id,service_type,version,started_at)
            VALUES($1,$2,$3,$4,$5) ON CONFLICT(service_name,instance_id) DO UPDATE SET started_at=excluded.started_at
            """;
            instanceCommand.Parameters.AddWithValue(heartbeat.ServiceType);
            instanceCommand.Parameters.AddWithValue(heartbeat.InstanceId);
            instanceCommand.Parameters.AddWithValue(heartbeat.ServiceType);
            instanceCommand.Parameters.AddWithValue(typeof(InserterWorker).Assembly.GetName().Version?.ToString() ?? "unknown");
            instanceCommand.Parameters.AddWithValue(heartbeat.StartedAt);
            await instanceCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var heartbeatCommand = connection.CreateCommand())
        {
            heartbeatCommand.Transaction = transaction;
            heartbeatCommand.CommandText = """
            INSERT INTO service_heartbeats(service_name,instance_id,observed_at,health,counters,details)
            VALUES($1,$2,$3,$4,jsonb_build_object('events',$5,'errors',$6,'latencyMilliseconds',$7),jsonb_build_object('error',$8::text))
            ON CONFLICT(service_name,instance_id) DO UPDATE SET observed_at=excluded.observed_at,health=excluded.health,counters=excluded.counters,details=excluded.details
            """;
            heartbeatCommand.Parameters.AddWithValue(heartbeat.ServiceType);
            heartbeatCommand.Parameters.AddWithValue(heartbeat.InstanceId);
            heartbeatCommand.Parameters.AddWithValue(heartbeat.ObservedAt);
            heartbeatCommand.Parameters.AddWithValue(heartbeat.Health);
            heartbeatCommand.Parameters.AddWithValue(heartbeat.Events);
            heartbeatCommand.Parameters.AddWithValue(heartbeat.Errors);
            heartbeatCommand.Parameters.AddWithValue(heartbeat.LatencyMilliseconds);
            heartbeatCommand.Parameters.AddWithValue((object?)heartbeat.Error ?? DBNull.Value);
            await heartbeatCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var clientCommand = connection.CreateCommand())
        {
            clientCommand.Transaction = transaction;
            clientCommand.CommandText = """
                UPDATE opcua_clients SET last_status=$1,last_communication=$2,reconnection_attempts=$3 WHERE client_key=$4
                """;
            clientCommand.Parameters.AddWithValue(heartbeat.Health);
            clientCommand.Parameters.AddWithValue(heartbeat.ObservedAt);
            clientCommand.Parameters.AddWithValue(heartbeat.ReconnectionAttempts);
            clientCommand.Parameters.AddWithValue((object?)heartbeat.ClientId ?? DBNull.Value);
            await clientCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var serverCommand = connection.CreateCommand())
        {
            serverCommand.Transaction = transaction;
            serverCommand.CommandText = """
                UPDATE opcua_servers SET last_status=$1,last_communication=$2 WHERE server_key=$3
                """;
            serverCommand.Parameters.AddWithValue(heartbeat.Health);
            serverCommand.Parameters.AddWithValue(heartbeat.ObservedAt);
            serverCommand.Parameters.AddWithValue((object?)heartbeat.ServerId ?? DBNull.Value);
            await serverCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task PersistAsync(NpgsqlDataSource dataSource, IReadOnlyList<ConsumeResult<string, string>> messages, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var message in messages)
        {
            var processed = JsonSerializer.Deserialize<ProcessedTelemetryEvent>(message.Message.Value)
                ?? throw new InvalidDataException("Processed telemetry payload is JSON null.");
            var telemetry = processed.Telemetry;
            await using var command = new NpgsqlCommand("""
                WITH claimed AS (
                  INSERT INTO telemetry_event_receipts(event_id, kafka_topic, kafka_partition, kafka_offset)
                  VALUES ($1, $2, $3, $4) ON CONFLICT DO NOTHING RETURNING event_id
                )
                INSERT INTO telemetry_measurements
                  (source_timestamp,event_id,series_id,numeric_value,boolean_value,text_value,quality,opcua_status_code,server_timestamp,received_timestamp,processing_timestamp)
                SELECT $5,$1,$6,$7,$8,$9,$10,$11,$12,$13,$14 FROM claimed;
                """, connection, transaction);
            command.Parameters.AddWithValue(telemetry.EventId);
            command.Parameters.AddWithValue(message.Topic);
            command.Parameters.AddWithValue(message.Partition.Value);
            command.Parameters.AddWithValue(message.Offset.Value);
            command.Parameters.AddWithValue(telemetry.SourceTimestamp);
            command.Parameters.AddWithValue(telemetry.SeriesId);
            AddNullable(command, telemetry.Value is NumericTelemetryValue numeric ? numeric.Value : null, NpgsqlDbType.Double);
            AddNullable(command, telemetry.Value is BooleanTelemetryValue boolean ? boolean.Value : null, NpgsqlDbType.Boolean);
            AddNullable(command, telemetry.Value is TextTelemetryValue text ? text.Value : null, NpgsqlDbType.Text);
            command.Parameters.AddWithValue(telemetry.Quality);
            command.Parameters.AddWithValue((long)telemetry.OpcUaStatusCode);
            command.Parameters.AddWithValue(telemetry.ServerTimestamp);
            command.Parameters.AddWithValue(telemetry.ReceivedTimestamp);
            command.Parameters.AddWithValue(processed.ProcessingTimestamp);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddNullable(NpgsqlCommand command, object? value, NpgsqlDbType type) =>
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = type, Value = value ?? DBNull.Value });
}
