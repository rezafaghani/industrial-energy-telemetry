namespace Ietp.Telemetry.Processor;

public sealed class ProcessorOptions
{
    public string BootstrapServers { get; init; } = "kafka:9092";
    public string GroupId { get; init; } = "telemetry-processors-v1";
    public string RawTopic { get; init; } = "telemetry.raw";
    public string ProcessedTopic { get; init; } = "telemetry.processed";
    public string DeadLetterTopic { get; init; } = "telemetry.dead-letter";
    public string StatusTopic { get; init; } = "telemetry.status";
}
