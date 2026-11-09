using Ietp.Telemetry.Domain.Simulation;

namespace Ietp.OpcUa.Server;

public sealed class ServerOptions
{
    public string ServerId { get; init; } = "windfarm01";
    public string Endpoint { get; init; } = "opc.tcp://0.0.0.0:4841/ietp";
    public string NamespaceUri { get; init; } = "urn:ietp:dk:demo:windfarm01";
    public string AssetId { get; init; } = "turbine01";
    public SimulationProfile Profile { get; init; } = SimulationProfile.WindFarm;
    public int Seed { get; init; } = 1;
    public int UpdateIntervalMilliseconds { get; init; } = 1000;
    public double AnomalyProbability { get; init; } = 0.002;
    public bool AllowUnsecuredDevelopmentEndpoint { get; init; }
    public string PkiPath { get; init; } = "pki";
    public int? FailAfterTicks { get; init; }
    public int FailureDurationTicks { get; init; }
}
