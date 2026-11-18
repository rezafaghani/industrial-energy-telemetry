using Npgsql;

namespace Ietp.Telemetry.Api;

public static class ApiEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/servers", (NpgsqlDataSource db, CancellationToken ct) => Query(db, "SELECT id,server_key,display_name,endpoint,namespace_uri,CASE WHEN last_communication>now()-interval '15 seconds' THEN last_status ELSE 'Disconnected' END AS status,last_communication FROM opcua_servers ORDER BY server_key", ct));
        api.MapPost("/servers", RegisterServer);
        api.MapGet("/clients", (NpgsqlDataSource db, CancellationToken ct) => Query(db, "SELECT c.id,c.client_key,s.server_key,CASE WHEN c.last_communication>now()-interval '15 seconds' THEN c.last_status ELSE 'Disconnected' END AS status,c.last_communication,c.reconnection_attempts FROM opcua_clients c JOIN opcua_servers s ON s.id=c.server_id ORDER BY c.client_key", ct));
        api.MapGet("/sites", (NpgsqlDataSource db, CancellationToken ct) => Query(db, "SELECT id,site_key,display_name,country_code,time_zone FROM sites ORDER BY site_key", ct));
        api.MapGet("/assets", (NpgsqlDataSource db, CancellationToken ct) => Query(db, "SELECT a.id,a.asset_key,a.display_name,a.asset_type,s.site_key FROM assets a JOIN sites s ON s.id=a.site_id ORDER BY a.asset_key", ct));
        api.MapGet("/services", (NpgsqlDataSource db, CancellationToken ct) => Query(db, "SELECT i.service_name,i.instance_id,i.service_type,i.version,i.started_at,h.observed_at,CASE WHEN h.observed_at>now()-interval '15 seconds' THEN h.health ELSE 'Unavailable' END AS health,h.counters,h.details FROM service_instances i LEFT JOIN service_heartbeats h USING(service_name,instance_id) ORDER BY i.service_type,i.instance_id", ct));
        api.MapGet("/series", SearchSeries);
        api.MapGet("/series/{id:guid}", GetSeries);
        api.MapGet("/series/{id:guid}/history", GetHistory);
        api.MapGet("/telemetry/latest", Latest);
        api.MapGet("/telemetry/quality", (NpgsqlDataSource db, CancellationToken ct) => Query(db, "SELECT quality,count(*) AS count FROM telemetry_measurements WHERE source_timestamp > now()-interval '24 hours' GROUP BY quality", ct));
        api.MapGet("/overview", Overview);
        api.MapGet("/statistics", Statistics);

        var control = api.MapGroup("/control-plane");
        control.MapGet("/collectors", (NpgsqlDataSource db, CancellationToken ct) => Query(db, """
            SELECT c.id,c.site_key,c.capacity,c.draining,c.registered_at,c.last_heartbeat_at,
              CASE WHEN c.last_heartbeat_at>now()-interval '30 seconds' AND NOT c.draining THEN 'Available' ELSE 'Unavailable' END AS status,
              h.health,h.active_sessions,h.buffer_items,h.buffer_bytes,h.last_error
            FROM collectors c LEFT JOIN collector_health h ON h.collector_id=c.id ORDER BY c.id
            """, ct));
        control.MapGet("/assignments", (NpgsqlDataSource db, CancellationToken ct) => Query(db, """
            SELECT a.endpoint_id,s.server_key,a.collector_id,a.lease_epoch,a.lease_acquired_at,a.lease_expires_at,
              a.configuration_version,a.lease_expires_at>now() AS active
            FROM collector_assignments a JOIN opcua_servers s ON s.id=a.endpoint_id ORDER BY s.server_key
            """, ct));
        control.MapGet("/configuration-versions", (NpgsqlDataSource db, CancellationToken ct) => Query(db,
            "SELECT version,created_at,created_by,description FROM configuration_versions ORDER BY version DESC", ct));
        control.MapGet("/certificates", (NpgsqlDataSource db, CancellationToken ct) => Query(db, """
            SELECT id,identity_reference,subject,thumbprint,not_before,not_after,trust_reference,revoked,updated_at,
              CASE WHEN revoked THEN 'Revoked' WHEN not_after<=now() THEN 'Expired'
                WHEN not_after<=now()+interval '30 days' THEN 'Expiring' ELSE 'Valid' END AS status
            FROM certificate_metadata ORDER BY not_after
            """, ct));
        control.MapPost("/collectors", RegisterCollector);
        control.MapPut("/collectors/{collectorId}/health", ReportCollectorHealth);
        control.MapPost("/collectors/{collectorId}/assignments", AcquireAssignments);
        control.MapPut("/collectors/{collectorId}/assignments/{endpointId:guid}", RenewAssignment);
        control.MapGet("/collectors/{collectorId}/assignments/{endpointId:guid}/configuration", GetAssignmentConfiguration);
        control.MapDelete("/collectors/{collectorId}", DeregisterCollector);
    }

    private static async Task<IResult> RegisterServer(RegisterServerRequest request, NpgsqlDataSource db, CancellationToken ct)
    {
        var measurements = (request.Measurements ?? []).Where(value => value is not null).Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (new[] { request.ServerKey, request.DisplayName, request.Endpoint, request.NamespaceUri, request.SiteKey, request.SiteName, request.AssetKey, request.AssetName }.Any(string.IsNullOrWhiteSpace)
            || measurements.Length == 0 || !Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "opc.tcp")
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["server"] = ["All fields and at least one measurement are required; endpoint must be an absolute opc.tcp URI."] });
        if (measurements.Any(value => value.Length > 200) || new[] { request.ServerKey, request.SiteKey, request.AssetKey }.Any(value => value.Length > 200))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["server"] = ["Keys and measurements cannot exceed 200 characters."] });

        await using var connection = await db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var siteId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var serverId = Guid.NewGuid();
            await using var command = new NpgsqlCommand("""
                INSERT INTO sites(id,site_key,display_name,country_code,time_zone) VALUES($1,$2,$3,'XX','UTC')
                  ON CONFLICT(site_key) DO UPDATE SET display_name=excluded.display_name RETURNING id;
                """, connection, transaction);
            command.Parameters.AddWithValue(siteId); command.Parameters.AddWithValue(request.SiteKey); command.Parameters.AddWithValue(request.SiteName);
            siteId = (Guid)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;

            command.CommandText = """
                INSERT INTO assets(id,site_id,asset_key,display_name,asset_type) VALUES($1,$2,$3,$4,'OPC UA asset')
                  ON CONFLICT(site_id,asset_key) DO UPDATE SET display_name=excluded.display_name RETURNING id;
                """;
            command.Parameters.Clear(); command.Parameters.AddWithValue(assetId); command.Parameters.AddWithValue(siteId); command.Parameters.AddWithValue(request.AssetKey); command.Parameters.AddWithValue(request.AssetName);
            assetId = (Guid)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;

            command.CommandText = """
                INSERT INTO opcua_servers(id,server_key,endpoint,namespace_uri,display_name,site_id,security_policy,security_mode)
                VALUES($1,$2,$3,$4,$5,$6,'http://opcfoundation.org/UA/SecurityPolicy#None','None');
                """;
            command.Parameters.Clear(); command.Parameters.AddWithValue(serverId); command.Parameters.AddWithValue(request.ServerKey); command.Parameters.AddWithValue(request.Endpoint);
            command.Parameters.AddWithValue(request.NamespaceUri); command.Parameters.AddWithValue(request.DisplayName); command.Parameters.AddWithValue(siteId);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            foreach (var measurement in measurements)
            {
                // shortcut: UI registration currently creates numeric series, add typed measurement fields when non-numeric devices are registered here.
                command.CommandText = """
                    INSERT INTO series_definitions(id,series_key,display_name,site_id,asset_id,measurement_id,measurement_type,unit,
                      opcua_server_id,namespace_uri,node_id,data_type,description)
                    VALUES($1,$2,$3,$4,$5,$6,$6,'',$7,$8,$9,'Double','Registered from the control room.');
                    """;
                command.Parameters.Clear(); command.Parameters.AddWithValue(Guid.NewGuid()); command.Parameters.AddWithValue($"{request.SiteKey}.{request.AssetKey}.{measurement}");
                command.Parameters.AddWithValue(measurement); command.Parameters.AddWithValue(siteId); command.Parameters.AddWithValue(assetId); command.Parameters.AddWithValue(measurement);
                command.Parameters.AddWithValue(serverId); command.Parameters.AddWithValue(request.NamespaceUri); command.Parameters.AddWithValue($"asset/{request.AssetKey}/{measurement}");
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return Results.Created($"/api/servers/{serverId}", new { id = serverId });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return Results.Conflict(new { error = "A server or series with the same key is already registered." });
        }
    }

    private static async Task<IResult> RegisterCollector(RegisterCollectorRequest request, CollectorCoordinator coordinator, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CollectorId) || request.CollectorId.Length > 200 || request.Capacity is < 1 or > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["collector"] = ["collectorId is required (maximum 200 characters) and capacity must be between 1 and 100."] });
        await coordinator.RegisterAsync(request, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ReportCollectorHealth(string collectorId, CollectorHealthRequest request, CollectorCoordinator coordinator, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Health) || request.ActiveSessions < 0 || request.BufferItems < 0 || request.BufferBytes < 0)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["health"] = ["health is required and counters cannot be negative."] });
        try
        {
            await coordinator.ReportHealthAsync(collectorId, request, ct).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> AcquireAssignments(string collectorId, CollectorCoordinator coordinator, int leaseSeconds = 30, CancellationToken ct = default)
    {
        if (leaseSeconds is < 5 or > 300)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["leaseSeconds"] = ["leaseSeconds must be between 5 and 300."] });
        return Results.Ok(await coordinator.AcquireAsync(collectorId, leaseSeconds, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> RenewAssignment(string collectorId, Guid endpointId, RenewLeaseRequest request, CollectorCoordinator coordinator, CancellationToken ct)
    {
        if (request.LeaseEpoch < 1 || request.LeaseSeconds is < 5 or > 300)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["lease"] = ["leaseEpoch must be positive and leaseSeconds must be between 5 and 300."] });
        var assignment = await coordinator.RenewAsync(collectorId, endpointId, request.LeaseEpoch, request.LeaseSeconds, ct).ConfigureAwait(false);
        return assignment is null ? Results.Conflict(new { error = "The lease is expired, stale, or owned by another collector." }) : Results.Ok(assignment);
    }

    private static async Task<IResult> DeregisterCollector(string collectorId, CollectorCoordinator coordinator, CancellationToken ct)
    {
        await coordinator.DeregisterAsync(collectorId, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> GetAssignmentConfiguration(string collectorId, Guid endpointId, long leaseEpoch, CollectorCoordinator coordinator, CancellationToken ct)
    {
        if (leaseEpoch < 1) return Results.BadRequest();
        var configuration = await coordinator.GetConfigurationAsync(collectorId, endpointId, leaseEpoch, ct).ConfigureAwait(false);
        return configuration is null ? Results.Conflict(new { error = "The lease is expired, stale, or owned by another collector." }) : Results.Ok(configuration);
    }

    private static async Task<IResult> SearchSeries(NpgsqlDataSource db, string? search, string? site, string? asset, string? measurementType, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 200) return Results.ValidationProblem(new Dictionary<string, string[]> { ["pagination"] = ["page must be positive and pageSize must be between 1 and 200."] });
        await using var command = db.CreateCommand("""
            SELECT d.id,d.series_key,d.display_name,s.site_key,s.display_name AS site_name,a.asset_key,a.display_name AS asset_name,
              d.measurement_id,d.measurement_type,d.unit,d.data_type,srv.server_key,srv.endpoint,d.namespace_uri,d.node_id,d.description,d.tags,d.active,d.created_at
            FROM series_definitions d JOIN sites s ON s.id=d.site_id JOIN assets a ON a.id=d.asset_id JOIN opcua_servers srv ON srv.id=d.opcua_server_id
            WHERE ($1::text IS NULL OR d.series_key ILIKE '%'||$1||'%' OR d.display_name ILIKE '%'||$1||'%')
              AND ($2::text IS NULL OR s.site_key=$2) AND ($3::text IS NULL OR a.asset_key=$3) AND ($4::text IS NULL OR d.measurement_type=$4)
            ORDER BY d.series_key LIMIT $5 OFFSET $6
            """);
        AddNullable(command, search); AddNullable(command, site); AddNullable(command, asset); AddNullable(command, measurementType);
        command.Parameters.AddWithValue(pageSize); command.Parameters.AddWithValue((page - 1) * pageSize);
        return Results.Ok(await ReadRows(command, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> GetSeries(Guid id, NpgsqlDataSource db, CancellationToken ct)
    {
        await using var command = db.CreateCommand("SELECT id,series_key,display_name,measurement_type,unit,data_type,namespace_uri,node_id,description,tags,active FROM series_definitions WHERE id=$1");
        command.Parameters.AddWithValue(id);
        var rows = await ReadRows(command, ct).ConfigureAwait(false);
        return rows.Count == 0 ? Results.NotFound() : Results.Ok(rows[0]);
    }

    private static async Task<IResult> GetHistory(Guid id, DateTimeOffset from, DateTimeOffset to, NpgsqlDataSource db, string? quality = null, int limit = 5000, CancellationToken ct = default)
    {
        if (from >= to || limit is < 1 or > 20_000) return Results.ValidationProblem(new Dictionary<string, string[]> { ["range"] = ["from must precede to and limit must be between 1 and 20000."] });
        await using var command = db.CreateCommand("""
            SELECT event_id,series_id,numeric_value,boolean_value,text_value,quality,opcua_status_code,source_timestamp,server_timestamp,received_timestamp,processing_timestamp,ingestion_timestamp
            FROM telemetry_measurements WHERE series_id=$1 AND source_timestamp >= $2 AND source_timestamp < $3 AND ($4::text IS NULL OR quality=$4)
            ORDER BY source_timestamp DESC LIMIT $5
            """);
        command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(from); command.Parameters.AddWithValue(to); AddNullable(command, quality); command.Parameters.AddWithValue(limit);
        return Results.Ok(await ReadRows(command, ct).ConfigureAwait(false));
    }

    private static Task<List<Dictionary<string, object?>>> Latest(NpgsqlDataSource db, CancellationToken ct) => Query(db, """
        SELECT DISTINCT ON (m.series_id) m.series_id,s.series_key,s.display_name,site.site_key,asset.asset_key,s.unit,
          m.numeric_value,m.boolean_value,m.text_value,m.quality,m.opcua_status_code,m.source_timestamp,
          m.server_timestamp,m.received_timestamp,m.processing_timestamp,m.ingestion_timestamp
        FROM telemetry_measurements m JOIN series_definitions s ON s.id=m.series_id
          JOIN sites site ON site.id=s.site_id JOIN assets asset ON asset.id=s.asset_id
        ORDER BY m.series_id,m.source_timestamp DESC
        """, ct);

    private static async Task<IResult> Overview(NpgsqlDataSource db, CancellationToken ct)
    {
        var rows = await Query(db, """
            SELECT (SELECT count(*) FROM opcua_servers) AS total_servers,
              (SELECT count(*) FROM opcua_servers WHERE last_status='Healthy' AND last_communication>now()-interval '15 seconds') AS connected_servers,
              (SELECT count(*) FROM opcua_clients) AS total_clients,
              (SELECT count(*) FROM opcua_clients WHERE last_status='Healthy' AND last_communication>now()-interval '15 seconds') AS connected_clients,
              (SELECT count(*) FROM service_heartbeats WHERE service_name='processor' AND observed_at>now()-interval '15 seconds') AS active_processors,
              (SELECT count(*) FROM service_heartbeats WHERE service_name='inserter' AND observed_at>now()-interval '15 seconds') AS active_inserters,
              (SELECT count(*) FROM series_definitions WHERE active) AS active_series,
              (SELECT max(source_timestamp) FROM telemetry_measurements) AS latest_telemetry_timestamp,
              (SELECT count(*) FROM telemetry_measurements WHERE ingestion_timestamp>now()-interval '1 minute')/60.0 AS events_inserted_per_second,
              (SELECT COALESCE(sum((counters->>'errors')::bigint),0) FROM service_heartbeats WHERE service_name='processor' AND observed_at>now()-interval '15 seconds') AS dead_letter_count,
              (SELECT count(*) FROM telemetry_measurements WHERE quality<>'Good' AND source_timestamp>now()-interval '1 hour') AS quality_errors
            """, ct).ConfigureAwait(false);
        return Results.Ok(rows[0]);
    }

    private static Task<List<Dictionary<string, object?>>> Statistics(NpgsqlDataSource db, CancellationToken ct) => Query(db, """
        SELECT date_trunc('minute',ingestion_timestamp) AS bucket,count(*) AS inserted,
          avg(extract(epoch FROM (ingestion_timestamp-source_timestamp))*1000) AS end_to_end_latency_ms
        FROM telemetry_measurements WHERE ingestion_timestamp>now()-interval '1 hour'
        GROUP BY bucket ORDER BY bucket
        """, ct);

    private static async Task<List<Dictionary<string, object?>>> Query(NpgsqlDataSource db, string sql, CancellationToken ct)
    {
        await using var command = db.CreateCommand(sql);
        return await ReadRows(command, ct).ConfigureAwait(false);
    }

    private static async Task<List<Dictionary<string, object?>>> ReadRows(NpgsqlCommand command, CancellationToken ct)
    {
        var result = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++) row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            result.Add(row);
        }
        return result;
    }

    private static void AddNullable(NpgsqlCommand command, string? value) => command.Parameters.AddWithValue((object?)value ?? DBNull.Value);
}
