using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Ietp.Telemetry.Api;

public sealed record RegisterCollectorRequest(string CollectorId, string? SiteKey, int Capacity, Dictionary<string, string>? Metadata);
public sealed record CollectorHealthRequest(string Health, int ActiveSessions, long BufferItems, long BufferBytes, string? LastError);
public sealed record RenewLeaseRequest(long LeaseEpoch, int LeaseSeconds = 30);
public sealed record CollectorAssignment(Guid EndpointId, string EndpointKey, string CollectorId, long LeaseEpoch,
    DateTimeOffset LeaseExpiresAt, long ConfigurationVersion);
public sealed record EndpointTag(string Measurement, string NodeId, string Unit);
public sealed record EndpointConfiguration(Guid EndpointId, string EndpointKey, string Endpoint, string NamespaceUri,
    string SiteKey, string AssetKey, string SecurityPolicy, string SecurityMode, long ConfigurationVersion,
    IReadOnlyList<EndpointTag> Tags);
public sealed record RegisterServerRequest(string ServerKey, string DisplayName, string Endpoint, string NamespaceUri,
    string SiteKey, string SiteName, string AssetKey, string AssetName, string[] Measurements);

public sealed class CollectorCoordinator(NpgsqlDataSource dataSource)
{
    public async Task RegisterAsync(RegisterCollectorRequest request, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO collectors(id,site_key,capacity,draining,registered_at,last_heartbeat_at,metadata)
            VALUES($1,$2,$3,false,now(),now(),$4)
            ON CONFLICT(id) DO UPDATE SET site_key=excluded.site_key,capacity=excluded.capacity,
              draining=false,last_heartbeat_at=now(),metadata=excluded.metadata
            """);
        command.Parameters.AddWithValue(request.CollectorId);
        command.Parameters.AddWithValue((object?)request.SiteKey ?? DBNull.Value);
        command.Parameters.AddWithValue(request.Capacity);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = JsonSerializer.Serialize(request.Metadata ?? []) });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReportHealthAsync(string collectorId, CollectorHealthRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var heartbeat = new NpgsqlCommand("UPDATE collectors SET last_heartbeat_at=now() WHERE id=$1 AND NOT draining", connection, transaction);
        heartbeat.Parameters.AddWithValue(collectorId);
        if (await heartbeat.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
            throw new KeyNotFoundException($"Collector '{collectorId}' is not registered or is draining.");

        await using var health = new NpgsqlCommand("""
            INSERT INTO collector_health(collector_id,observed_at,health,active_sessions,buffer_items,buffer_bytes,last_error)
            VALUES($1,now(),$2,$3,$4,$5,$6)
            ON CONFLICT(collector_id) DO UPDATE SET observed_at=now(),health=excluded.health,
              active_sessions=excluded.active_sessions,buffer_items=excluded.buffer_items,
              buffer_bytes=excluded.buffer_bytes,last_error=excluded.last_error
            """, connection, transaction);
        health.Parameters.AddWithValue(collectorId);
        health.Parameters.AddWithValue(request.Health);
        health.Parameters.AddWithValue(request.ActiveSessions);
        health.Parameters.AddWithValue(request.BufferItems);
        health.Parameters.AddWithValue(request.BufferBytes);
        health.Parameters.AddWithValue((object?)request.LastError ?? DBNull.Value);
        await health.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CollectorAssignment>> AcquireAsync(string collectorId, int leaseSeconds, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("""
            WITH owner AS (
              SELECT id,site_key,capacity,
                capacity-(SELECT count(*) FROM collector_assignments a WHERE a.collector_id=c.id AND a.lease_expires_at>now()) AS slots
              FROM collectors c WHERE id=$1 AND NOT draining AND last_heartbeat_at>now()-interval '30 seconds'
              FOR UPDATE
            ), candidates AS (
              SELECT s.id,s.configuration_version
              FROM opcua_servers s CROSS JOIN owner o JOIN sites site ON site.id=s.site_id
              LEFT JOIN collector_assignments a ON a.endpoint_id=s.id
              WHERE s.enabled AND EXISTS (SELECT 1 FROM series_definitions d WHERE d.opcua_server_id=s.id AND d.active)
                AND (o.site_key IS NULL OR o.site_key=site.site_key)
                AND (a.endpoint_id IS NULL OR a.lease_expires_at<=now())
              ORDER BY a.lease_expires_at NULLS FIRST,s.server_key
              FOR UPDATE OF s SKIP LOCKED
              LIMIT (SELECT greatest(slots,0) FROM owner)
            ), leased AS (
              INSERT INTO collector_assignments(endpoint_id,collector_id,lease_epoch,lease_acquired_at,lease_expires_at,configuration_version)
              SELECT id,$1,1,now(),now()+$2*interval '1 second',configuration_version FROM candidates
              ON CONFLICT(endpoint_id) DO UPDATE SET
                collector_id=excluded.collector_id,
                lease_epoch=CASE WHEN collector_assignments.collector_id=excluded.collector_id
                    AND collector_assignments.lease_expires_at>now()
                  THEN collector_assignments.lease_epoch ELSE collector_assignments.lease_epoch+1 END,
                lease_acquired_at=CASE WHEN collector_assignments.collector_id=excluded.collector_id
                    AND collector_assignments.lease_expires_at>now()
                  THEN collector_assignments.lease_acquired_at ELSE now() END,
                lease_expires_at=excluded.lease_expires_at,
                configuration_version=excluded.configuration_version
              WHERE collector_assignments.collector_id=excluded.collector_id OR collector_assignments.lease_expires_at<=now()
              RETURNING endpoint_id,collector_id,lease_epoch,lease_expires_at,configuration_version
            )
            SELECT a.endpoint_id,s.server_key,a.collector_id,a.lease_epoch,a.lease_expires_at,a.configuration_version
            FROM collector_assignments a JOIN opcua_servers s ON s.id=a.endpoint_id
            WHERE a.collector_id=$1 AND a.lease_expires_at>now()
              AND (EXISTS (SELECT 1 FROM leased) OR EXISTS (SELECT 1 FROM owner))
            ORDER BY s.server_key
            """, connection, transaction);
        command.Parameters.AddWithValue(collectorId);
        command.Parameters.AddWithValue(leaseSeconds);
        var assignments = await ReadAssignmentsAsync(command, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return assignments;
    }

    public async Task<CollectorAssignment?> RenewAsync(string collectorId, Guid endpointId, long leaseEpoch, int leaseSeconds, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE collector_assignments a SET lease_expires_at=now()+$4*interval '1 second'
            FROM collectors c,opcua_servers s
            WHERE a.endpoint_id=$1 AND a.collector_id=$2 AND a.lease_epoch=$3 AND a.lease_expires_at>now()
              AND c.id=a.collector_id AND NOT c.draining AND c.last_heartbeat_at>now()-interval '30 seconds'
              AND s.id=a.endpoint_id
            RETURNING a.endpoint_id,s.server_key,a.collector_id,a.lease_epoch,a.lease_expires_at,a.configuration_version
            """);
        command.Parameters.AddWithValue(endpointId);
        command.Parameters.AddWithValue(collectorId);
        command.Parameters.AddWithValue(leaseEpoch);
        command.Parameters.AddWithValue(leaseSeconds);
        return (await ReadAssignmentsAsync(command, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<EndpointConfiguration?> GetConfigurationAsync(string collectorId, Guid endpointId, long leaseEpoch, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT s.id,s.server_key,s.endpoint,s.namespace_uri,site.site_key,asset.asset_key,
              s.security_policy,s.security_mode,s.configuration_version,
              d.measurement_id,d.node_id,d.unit
            FROM collector_assignments assignment
            JOIN opcua_servers s ON s.id=assignment.endpoint_id
            JOIN sites site ON site.id=s.site_id
            JOIN series_definitions d ON d.opcua_server_id=s.id AND d.active
            JOIN assets asset ON asset.id=d.asset_id
            WHERE assignment.endpoint_id=$1 AND assignment.collector_id=$2 AND assignment.lease_epoch=$3
              AND assignment.lease_expires_at>now() AND s.enabled
            ORDER BY d.measurement_id
            """);
        command.Parameters.AddWithValue(endpointId);
        command.Parameters.AddWithValue(collectorId);
        command.Parameters.AddWithValue(leaseEpoch);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        EndpointConfiguration? configuration = null;
        var tags = new List<EndpointTag>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tags.Add(new(reader.GetString(9), reader.GetString(10), reader.GetString(11)));
            configuration ??= new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetInt64(8), tags);
        }
        return configuration;
    }

    public async Task DeregisterAsync(string collectorId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("""
            UPDATE collectors SET draining=true,last_heartbeat_at=now() WHERE id=$1;
            UPDATE collector_assignments SET lease_expires_at=greatest(now(),lease_acquired_at+interval '1 microsecond')
            WHERE collector_id=$1;
            """, connection, transaction);
        command.Parameters.AddWithValue(collectorId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<CollectorAssignment>> ReadAssignmentsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var result = new List<CollectorAssignment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3),
                reader.GetFieldValue<DateTimeOffset>(4), reader.GetInt64(5)));
        return result;
    }
}
