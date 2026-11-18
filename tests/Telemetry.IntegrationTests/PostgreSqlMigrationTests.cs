using Ietp.Telemetry.Inserter;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ietp.Telemetry.IntegrationTests;

public sealed class PostgreSqlMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task MigrationsAreIdempotentAndEventReceiptsDeduplicate()
    {
        var connection = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            SearchPath = "control_plane,telemetry,operations,public"
        };
        await using var dataSource = NpgsqlDataSource.Create(connection.ConnectionString);
        await DatabaseMigrator.MigrateAsync(dataSource, CancellationToken.None);
        await DatabaseMigrator.MigrateAsync(dataSource, CancellationToken.None);

        var eventId = Guid.NewGuid();
        await using var first = dataSource.CreateCommand("INSERT INTO telemetry_event_receipts(event_id,kafka_topic,kafka_partition,kafka_offset) VALUES($1,'test',0,1) ON CONFLICT DO NOTHING");
        first.Parameters.AddWithValue(eventId);
        Assert.Equal(1, await first.ExecuteNonQueryAsync());
        await using var replay = dataSource.CreateCommand("INSERT INTO telemetry_event_receipts(event_id,kafka_topic,kafka_partition,kafka_offset) VALUES($1,'test',0,2) ON CONFLICT DO NOTHING");
        replay.Parameters.AddWithValue(eventId);
        Assert.Equal(0, await replay.ExecuteNonQueryAsync());

        await using var schemas = dataSource.CreateCommand("""
            SELECT table_schema,table_name FROM information_schema.tables
            WHERE table_schema IN ('public','control_plane','telemetry','operations')
            ORDER BY table_schema,table_name
            """);
        await using var reader = await schemas.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync()) tables.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        Assert.Contains("control_plane.collector_assignments", tables);
        Assert.Contains("control_plane.opcua_servers", tables);
        Assert.Contains("telemetry.telemetry_measurements", tables);
        Assert.Contains("operations.service_heartbeats", tables);
        Assert.DoesNotContain(tables, table => table.StartsWith("public.", StringComparison.Ordinal) && table != "public.schema_migrations");
    }
}
