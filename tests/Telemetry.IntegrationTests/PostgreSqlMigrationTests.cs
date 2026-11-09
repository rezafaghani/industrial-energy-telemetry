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
        await using var dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        await DatabaseMigrator.MigrateAsync(dataSource, CancellationToken.None);
        await DatabaseMigrator.MigrateAsync(dataSource, CancellationToken.None);

        var eventId = Guid.NewGuid();
        await using var first = dataSource.CreateCommand("INSERT INTO telemetry_event_receipts(event_id,kafka_topic,kafka_partition,kafka_offset) VALUES($1,'test',0,1) ON CONFLICT DO NOTHING");
        first.Parameters.AddWithValue(eventId);
        Assert.Equal(1, await first.ExecuteNonQueryAsync());
        await using var replay = dataSource.CreateCommand("INSERT INTO telemetry_event_receipts(event_id,kafka_topic,kafka_partition,kafka_offset) VALUES($1,'test',0,2) ON CONFLICT DO NOTHING");
        replay.Parameters.AddWithValue(eventId);
        Assert.Equal(0, await replay.ExecuteNonQueryAsync());
    }
}
