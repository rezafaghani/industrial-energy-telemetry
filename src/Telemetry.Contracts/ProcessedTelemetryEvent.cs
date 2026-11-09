namespace Ietp.Telemetry.Contracts;

public sealed record ProcessedTelemetryEvent(
    TelemetryEvent Telemetry,
    string MeasurementType,
    DateTimeOffset ProcessingTimestamp);

public sealed record DeadLetterEvent(
    string? EventId,
    string RawPayload,
    string Error,
    string SourceTopic,
    int SourcePartition,
    long SourceOffset,
    DateTimeOffset FailedTimestamp);
