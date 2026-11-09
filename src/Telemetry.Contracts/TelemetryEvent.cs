namespace Ietp.Telemetry.Contracts;

public sealed record TelemetryEvent(
    Guid EventId,
    int SchemaVersion,
    Guid SeriesId,
    string SeriesKey,
    string ServerId,
    string ClientId,
    TelemetryValue Value,
    string ValueType,
    string Unit,
    DateTimeOffset SourceTimestamp,
    DateTimeOffset ServerTimestamp,
    DateTimeOffset ReceivedTimestamp,
    string Quality,
    uint OpcUaStatusCode,
    string CorrelationId)
{
    public const int CurrentSchemaVersion = 1;
}
