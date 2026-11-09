using Npgsql;

namespace Ietp.Telemetry.Api;

public sealed class ApiHeartbeatWorker(NpgsqlDataSource dataSource) : BackgroundService
{
    private readonly string _instanceId = Environment.GetEnvironmentVariable("HOSTNAME") ?? Guid.NewGuid().ToString("N");
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            await using var connection = await dataSource.OpenConnectionAsync(stoppingToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(stoppingToken).ConfigureAwait(false);
            await using (var instanceCommand = connection.CreateCommand())
            {
                instanceCommand.Transaction = transaction;
                instanceCommand.CommandText = """
                    INSERT INTO service_instances(service_name,instance_id,service_type,version,started_at)
                    VALUES('api',$1,'api',$2,$3) ON CONFLICT(service_name,instance_id) DO UPDATE SET started_at=excluded.started_at
                    """;
                instanceCommand.Parameters.AddWithValue(_instanceId);
                instanceCommand.Parameters.AddWithValue(typeof(ApiHeartbeatWorker).Assembly.GetName().Version?.ToString() ?? "unknown");
                instanceCommand.Parameters.AddWithValue(_startedAt);
                await instanceCommand.ExecuteNonQueryAsync(stoppingToken).ConfigureAwait(false);
            }

            await using (var heartbeatCommand = connection.CreateCommand())
            {
                heartbeatCommand.Transaction = transaction;
                heartbeatCommand.CommandText = """
                    INSERT INTO service_heartbeats(service_name,instance_id,observed_at,health,counters,details)
                    VALUES('api',$1,now(),'Healthy','{}','{}')
                    ON CONFLICT(service_name,instance_id) DO UPDATE SET observed_at=excluded.observed_at,health=excluded.health
                    """;
                heartbeatCommand.Parameters.AddWithValue(_instanceId);
                await heartbeatCommand.ExecuteNonQueryAsync(stoppingToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(stoppingToken).ConfigureAwait(false);
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
