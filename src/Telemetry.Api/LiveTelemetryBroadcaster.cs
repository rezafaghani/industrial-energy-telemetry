using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace Ietp.Telemetry.Api;

public sealed class LiveTelemetryBroadcaster(NpgsqlDataSource dataSource, IHubContext<TelemetryHub> hub) : BackgroundService
{
    private DateTimeOffset _cursor = DateTimeOffset.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await using var command = dataSource.CreateCommand("""
                SELECT m.event_id,m.series_id,s.series_key,COALESCE(m.numeric_value::text,m.boolean_value::text,m.text_value),s.unit,m.quality,m.source_timestamp,m.ingestion_timestamp
                FROM telemetry_measurements m JOIN series_definitions s ON s.id=m.series_id
                WHERE m.ingestion_timestamp > $1 ORDER BY m.ingestion_timestamp LIMIT 1000
                """);
            command.Parameters.AddWithValue(_cursor);
            await using var reader = await command.ExecuteReaderAsync(stoppingToken).ConfigureAwait(false);
            while (await reader.ReadAsync(stoppingToken).ConfigureAwait(false))
            {
                var ingestionTimestamp = reader.GetFieldValue<DateTimeOffset>(7);
                await hub.Clients.All.SendAsync("telemetry", new
                {
                    eventId = reader.GetGuid(0), seriesId = reader.GetGuid(1), seriesKey = reader.GetString(2),
                    value = reader.GetString(3), unit = reader.GetString(4), quality = reader.GetString(5),
                    sourceTimestamp = reader.GetFieldValue<DateTimeOffset>(6)
                }, stoppingToken).ConfigureAwait(false);
                if (ingestionTimestamp > _cursor) _cursor = ingestionTimestamp;
            }
        }
    }
}
