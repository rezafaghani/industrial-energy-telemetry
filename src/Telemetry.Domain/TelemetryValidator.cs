using Ietp.Telemetry.Contracts;

namespace Ietp.Telemetry.Domain;

public static class TelemetryValidator
{
    public static string? Validate(TelemetryEvent telemetry)
    {
        if (telemetry.SchemaVersion != TelemetryEvent.CurrentSchemaVersion) return "Unsupported schema version.";
        if (telemetry.EventId == Guid.Empty) return "Event ID is required.";
        if (string.IsNullOrWhiteSpace(telemetry.SeriesKey)) return "Series key is required.";

        Guid expectedId;
        try
        {
            expectedId = SeriesIdentity.CreateId(telemetry.SeriesKey);
        }
        catch (ArgumentException)
        {
            return "Series key is not canonical.";
        }

        if (telemetry.SeriesId != expectedId) return "Series ID does not match the canonical series key.";
        var series = SeriesCatalog.All.FirstOrDefault(candidate => candidate.Key == telemetry.SeriesKey);
        if (series is null) return "Series key is not registered.";
        if (telemetry.ServerId != series.ServerKey || telemetry.ClientId != series.ClientKey)
            return "Server or client does not match the registered series.";
        if (telemetry.Unit != series.Unit) return "Unit does not match the registered series.";
        if (series.DataType switch
            {
                "Double" => telemetry.Value is not NumericTelemetryValue,
                "Boolean" => telemetry.Value is not BooleanTelemetryValue,
                "String" => telemetry.Value is not TextTelemetryValue,
                _ => true
            })
            return "Value type does not match the registered series.";
        if (telemetry.SourceTimestamp == default) return "Source timestamp is required.";
        if (telemetry.ReceivedTimestamp < telemetry.SourceTimestamp.AddDays(-1)) return "Received timestamp precedes source timestamp beyond tolerance.";
        if (telemetry.Value is NumericTelemetryValue numeric && !double.IsFinite(numeric.Value)) return "Numeric value must be finite.";
        if (string.IsNullOrWhiteSpace(telemetry.Quality)) return "Quality is required.";
        return null;
    }
}
