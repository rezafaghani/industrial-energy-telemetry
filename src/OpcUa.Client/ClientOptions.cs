namespace Ietp.OpcUa.Client;

public sealed class ClientOptions
{
    public string ClientId { get; init; } = "windfarm01-client";
    public string ServerId { get; init; } = "windfarm01";
    public string Endpoint { get; init; } = "opc.tcp://opcua-wind:4841/ietp";
    public string NamespaceUri { get; init; } = "urn:ietp:dk:demo:windfarm01";
    public string SiteId { get; init; } = "windfarm01";
    public string AssetId { get; init; } = "turbine01";
    public string[] Measurements { get; init; } = [];
    public string KafkaBootstrapServers { get; init; } = "kafka:9092";
    public string KafkaTopic { get; init; } = "telemetry.raw";
    public string StatusTopic { get; init; } = "telemetry.status";
    public int BufferCapacity { get; init; } = 10_000;
    public int PublishingIntervalMilliseconds { get; init; } = 1000;
    public bool UseSecurity { get; init; } = true;
    public string PkiPath { get; init; } = "pki";
}
