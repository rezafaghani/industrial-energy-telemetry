using Npgsql;

namespace Ietp.Telemetry.Inserter;

public static class DatabaseMigrator
{
    public static async Task MigrateAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var initialize = new NpgsqlCommand("SELECT pg_advisory_xact_lock(4937291); CREATE TABLE IF NOT EXISTS schema_migrations(version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());", connection, transaction))
        {
            await initialize.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var assembly = typeof(DatabaseMigrator).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".sql", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var version = resource[(resource.LastIndexOf('.', resource.Length - 5) + 1)..^4];
            await using var exists = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE version=$1)", connection, transaction);
            exists.Parameters.AddWithValue(version);
            if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true) continue;
            await using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"Missing migration {resource}.");
            using var reader = new StreamReader(stream);
            await using var migration = new NpgsqlCommand(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false), connection, transaction);
            await migration.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using var record = new NpgsqlCommand("INSERT INTO schema_migrations(version) VALUES($1)", connection, transaction);
            record.Parameters.AddWithValue(version);
            await record.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
