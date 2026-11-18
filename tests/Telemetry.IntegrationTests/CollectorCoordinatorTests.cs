using Ietp.Telemetry.Api;
using Ietp.Telemetry.Inserter;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ietp.Telemetry.IntegrationTests;

public sealed class CollectorCoordinatorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ConcurrentAcquisitionIsExclusiveAndReassignmentIncrementsEpoch()
    {
        var connection = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            SearchPath = "control_plane,telemetry,operations,public"
        };
        await using var dataSource = NpgsqlDataSource.Create(connection.ConnectionString);
        await DatabaseMigrator.MigrateAsync(dataSource, CancellationToken.None);
        var endpointId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        await using (var seed = dataSource.CreateCommand("""
            INSERT INTO sites(id,site_key,display_name,country_code,time_zone) VALUES($1,'site-1','Site 1','DK','UTC');
            INSERT INTO opcua_servers(id,server_key,endpoint,namespace_uri,display_name,site_id)
              VALUES($2,'endpoint-1','opc.tcp://endpoint-1:4840','urn:test','Endpoint 1',$1);
            """))
        {
            seed.Parameters.AddWithValue(siteId);
            seed.Parameters.AddWithValue(endpointId);
            await seed.ExecuteNonQueryAsync();
        }

        var coordinator = new CollectorCoordinator(dataSource);
        await coordinator.RegisterAsync(new("collector-a", null, 1, null), CancellationToken.None);
        await coordinator.RegisterAsync(new("collector-b", null, 1, null), CancellationToken.None);
        await coordinator.ReportHealthAsync("collector-a", new("Healthy", 0, 0, 0, null), CancellationToken.None);
        await coordinator.ReportHealthAsync("collector-b", new("Healthy", 0, 0, 0, null), CancellationToken.None);

        var acquisitions = await Task.WhenAll(
            coordinator.AcquireAsync("collector-a", 30, CancellationToken.None),
            coordinator.AcquireAsync("collector-b", 30, CancellationToken.None));
        var first = Assert.Single(acquisitions.SelectMany(value => value));
        var rediscovered = Assert.Single(await coordinator.AcquireAsync(first.CollectorId, 30, CancellationToken.None));
        Assert.Equal(first.LeaseEpoch, rediscovered.LeaseEpoch);

        await coordinator.DeregisterAsync(first.CollectorId, CancellationToken.None);
        var replacementId = first.CollectorId == "collector-a" ? "collector-b" : "collector-a";
        var replacement = Assert.Single(await coordinator.AcquireAsync(replacementId, 30, CancellationToken.None));
        Assert.Equal(endpointId, replacement.EndpointId);
        Assert.Equal(first.LeaseEpoch + 1, replacement.LeaseEpoch);
        Assert.Null(await coordinator.RenewAsync(first.CollectorId, endpointId, first.LeaseEpoch, 30, CancellationToken.None));
    }
}
