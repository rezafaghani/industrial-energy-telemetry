using Npgsql;

namespace Ietp.Telemetry.Api;

public static class ApiEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/servers", (NpgsqlDataSource db, CancellationToken ct) => Query(db, "SELECT id,server_key,display_name,endpoint,namespace_uri,CASE WHEN last_communication>now()-interval '15 seconds' THEN last_status ELSE 'Disconnected' END AS status,last_communication FROM opcua_servers ORDER BY server_key", ct));
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
