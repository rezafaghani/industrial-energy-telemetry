namespace Ietp.Telemetry.Domain.Simulation;

public sealed record SimulationSnapshot(
    DateTimeOffset SourceTimestamp,
    bool Available,
    bool Anomaly,
    IReadOnlyDictionary<string, object> Measurements);
