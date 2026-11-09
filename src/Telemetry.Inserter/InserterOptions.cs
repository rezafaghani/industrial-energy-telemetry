namespace Ietp.Telemetry.Inserter;

public sealed class InserterOptions
{
    public string BootstrapServers { get; init; } = "kafka:9092";
    public string GroupId { get; init; } = "telemetry-inserters-v1";
    public string Topic { get; init; } = "telemetry.processed";
    public string StatusTopic { get; init; } = "telemetry.status";
    public string ConnectionString { get; init; } = string.Empty;
    public int BatchSize { get; init; } = 250;
}
