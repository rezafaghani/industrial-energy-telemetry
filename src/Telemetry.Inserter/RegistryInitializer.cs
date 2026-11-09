using Ietp.Telemetry.Domain;
using Npgsql;

namespace Ietp.Telemetry.Inserter;

internal static class RegistryInitializer
{
    public static async Task EnsureAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var definition in SeriesCatalog.All)
        {
            var siteId = SeriesCatalog.EntityId("site", definition.SiteKey);
            var assetId = SeriesCatalog.EntityId("asset", definition.AssetKey);
            var serverId = SeriesCatalog.EntityId("server", definition.ServerKey);
            var clientId = SeriesCatalog.EntityId("client", definition.ClientKey);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO sites(id,site_key,display_name,country_code,time_zone) VALUES($1,$2,$3,'DK','Europe/Copenhagen')
                ON CONFLICT(id) DO UPDATE SET display_name=excluded.display_name;
                """, cancellationToken, siteId, definition.SiteKey, definition.SiteName).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO assets(id,site_id,asset_key,display_name,asset_type) VALUES($1,$2,$3,$4,$5)
                ON CONFLICT(id) DO UPDATE SET display_name=excluded.display_name,asset_type=excluded.asset_type;
                """, cancellationToken, assetId, siteId, definition.AssetKey, definition.AssetName, definition.AssetType).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO opcua_servers(id,server_key,endpoint,namespace_uri,display_name) VALUES($1,$2,$3,$4,$5)
                ON CONFLICT(id) DO UPDATE SET endpoint=excluded.endpoint,namespace_uri=excluded.namespace_uri;
                """, cancellationToken, serverId, definition.ServerKey, definition.Endpoint, definition.NamespaceUri, definition.SiteName).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO opcua_clients(id,client_key,server_id) VALUES($1,$2,$3)
                ON CONFLICT(id) DO UPDATE SET server_id=excluded.server_id;
                """, cancellationToken, clientId, definition.ClientKey, serverId).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO series_definitions(id,series_key,display_name,site_id,asset_id,measurement_id,measurement_type,unit,opcua_server_id,namespace_uri,node_id,data_type,description,tags,active)
                VALUES($1,$2,$3,$4,$5,$6,$6,$7,$8,$9,$10,$11,$12,'{}',true)
                ON CONFLICT(id) DO UPDATE SET display_name=excluded.display_name,unit=excluded.unit,data_type=excluded.data_type,description=excluded.description,active=true;
                """, cancellationToken, definition.Id, definition.Key, definition.DisplayName, siteId, assetId, definition.Measurement,
                definition.Unit, serverId, definition.NamespaceUri, definition.NodeId, definition.DataType,
                $"{definition.DisplayName} from {definition.AssetName}.").ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
