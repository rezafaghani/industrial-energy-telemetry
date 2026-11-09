using Ietp.Telemetry.Domain.Simulation;
using Xunit;

namespace Ietp.Telemetry.UnitTests;

public sealed class EnergySimulationTests
{
    [Theory]
    [InlineData(SimulationProfile.WindFarm, 6)]
    [InlineData(SimulationProfile.SolarFarm, 6)]
    [InlineData(SimulationProfile.PowerGrid, 7)]
    [InlineData(SimulationProfile.BatteryStorage, 6)]
    [InlineData(SimulationProfile.IndustrialPlant, 6)]
    public void EveryProfileProducesItsCompleteMeasurementSet(SimulationProfile profile, int expectedCount)
    {
        var snapshot = new EnergySimulation(new(profile, Seed: 42, AnomalyProbability: 0)).Next(DateTimeOffset.UnixEpoch);

        Assert.True(snapshot.Available);
        Assert.False(snapshot.Anomaly);
        Assert.Equal(expectedCount, snapshot.Measurements.Count);
    }

    [Fact]
    public void SeedMakesSimulationRepeatable()
    {
        var first = new EnergySimulation(new(SimulationProfile.WindFarm, Seed: 7, AnomalyProbability: 0));
        var second = new EnergySimulation(new(SimulationProfile.WindFarm, Seed: 7, AnomalyProbability: 0));

        Assert.Equal(first.Next(DateTimeOffset.UnixEpoch).Measurements, second.Next(DateTimeOffset.UnixEpoch).Measurements);
    }

    [Fact]
    public void BatteryStateOfChargeFallsWhileDischarging()
    {
        var simulation = new EnergySimulation(new(SimulationProfile.BatteryStorage, Seed: 1, UpdateInterval: TimeSpan.FromHours(1), AnomalyProbability: 0));
        var first = simulation.Next(DateTimeOffset.UnixEpoch);
        var second = simulation.Next(DateTimeOffset.UnixEpoch.AddHours(1));

        Assert.True((double)second.Measurements["state_of_charge"] < (double)first.Measurements["state_of_charge"]);
        Assert.Equal("Discharging", second.Measurements["operating_mode"]);
    }

    [Fact]
    public void ConfiguredFailureStopsMeasurementsThenRecovers()
    {
        var simulation = new EnergySimulation(new(SimulationProfile.PowerGrid, FailAfterTicks: 2, FailureDurationTicks: 2));

        Assert.True(simulation.Next(DateTimeOffset.UnixEpoch).Available);
        Assert.False(simulation.Next(DateTimeOffset.UnixEpoch).Available);
        Assert.False(simulation.Next(DateTimeOffset.UnixEpoch).Available);
        Assert.True(simulation.Next(DateTimeOffset.UnixEpoch).Available);
    }
}
