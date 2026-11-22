using Ietp.Telemetry.Api;
using Ietp.Telemetry.Inserter;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ietp.Telemetry.IntegrationTests;

[Trait("Category", "Integration")]
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
        var assetId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        await using (var seedSite = dataSource.CreateCommand("""
            INSERT INTO sites(id,site_key,display_name,country_code,time_zone) VALUES($1,'site-1','Site 1','DK','UTC')
            """))
        {
            seedSite.Parameters.AddWithValue(siteId);
            await seedSite.ExecuteNonQueryAsync();
        }
        await using (var seedServer = dataSource.CreateCommand("""
            INSERT INTO opcua_servers(id,server_key,endpoint,namespace_uri,display_name,site_id)
              VALUES($1,'endpoint-1','opc.tcp://endpoint-1:4840','urn:test','Endpoint 1',$2)
            """))
        {
            seedServer.Parameters.AddWithValue(endpointId);
            seedServer.Parameters.AddWithValue(siteId);
            await seedServer.ExecuteNonQueryAsync();
        }
        await using (var seedAsset = dataSource.CreateCommand("""
            INSERT INTO assets(id,site_id,asset_key,display_name,asset_type)
              VALUES($1,$2,'asset-1','Asset 1','Test asset')
            """))
        {
            seedAsset.Parameters.AddWithValue(assetId);
            seedAsset.Parameters.AddWithValue(siteId);
            await seedAsset.ExecuteNonQueryAsync();
        }
        await using (var seedSeries = dataSource.CreateCommand("""
            INSERT INTO series_definitions(
              id,series_key,display_name,site_id,asset_id,measurement_id,measurement_type,unit,
              opcua_server_id,namespace_uri,node_id,data_type,description,tags,active)
            VALUES($1,'dk.test.site-1.asset-1.power','Power',$2,$3,'power','Active power','kW',
              $4,'urn:test','ns=2;s=power','Double','Test measurement','{}',true)
            """))
        {
            seedSeries.Parameters.AddWithValue(seriesId);
            seedSeries.Parameters.AddWithValue(siteId);
            seedSeries.Parameters.AddWithValue(assetId);
            seedSeries.Parameters.AddWithValue(endpointId);
            await seedSeries.ExecuteNonQueryAsync();
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

    [Fact]
    public async Task CollectorWaitsWhenNoConfiguredServerExists()
    {
        var connection = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            SearchPath = "control_plane,telemetry,operations,public"
        };
        await using var dataSource = NpgsqlDataSource.Create(connection.ConnectionString);
        await DatabaseMigrator.MigrateAsync(dataSource, CancellationToken.None);
        await using (var seedSite = dataSource.CreateCommand("""
            INSERT INTO sites(id,site_key,display_name,country_code,time_zone) VALUES(gen_random_uuid(),'empty','Empty','DK','UTC')
            """))
            await seedSite.ExecuteNonQueryAsync();
        await using (var seedServer = dataSource.CreateCommand("""
            INSERT INTO opcua_servers(id,server_key,endpoint,namespace_uri,display_name,site_id)
              SELECT gen_random_uuid(),'empty','opc.tcp://empty:4840','urn:empty','Empty',id FROM sites WHERE site_key='empty'
            """))
            await seedServer.ExecuteNonQueryAsync();

        var coordinator = new CollectorCoordinator(dataSource);
        await coordinator.RegisterAsync(new("collector", null, 1, null), CancellationToken.None);
        await coordinator.ReportHealthAsync("collector", new("Healthy", 0, 0, 0, null), CancellationToken.None);

        Assert.Empty(await coordinator.AcquireAsync("collector", 30, CancellationToken.None));
    }
}
