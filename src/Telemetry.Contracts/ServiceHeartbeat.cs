namespace Ietp.Telemetry.Contracts;

public sealed record ServiceHeartbeat(
    string ServiceType,
    string InstanceId,
    string Health,
    DateTimeOffset StartedAt,
    DateTimeOffset ObservedAt,
    long Events,
    long Errors,
    double LatencyMilliseconds,
    string? ServerId = null,
    string? ClientId = null,
    int SubscriptionCount = 0,
    int MonitoredItemCount = 0,
    int ReconnectionAttempts = 0,
    string? Error = null);
