namespace Ietp.Telemetry.Domain.Simulation;

public sealed record SimulationOptions(
    SimulationProfile Profile,
    int Seed = 1,
    TimeSpan? UpdateInterval = null,
    double AnomalyProbability = 0.002,
    int? FailAfterTicks = null,
    int FailureDurationTicks = 0)
{
    public TimeSpan Interval { get; } = UpdateInterval ?? TimeSpan.FromSeconds(1);
}
