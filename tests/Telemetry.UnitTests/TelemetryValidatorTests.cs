using Ietp.Telemetry.Contracts;
using Ietp.Telemetry.Domain;
using Xunit;

namespace Ietp.Telemetry.UnitTests;

public sealed class TelemetryValidatorTests
{
    [Fact]
    public void AcceptsCanonicalFiniteEvent()
    {
        var key = "dk.demo.windfarm01.turbine01.active_power";
        var timestamp = DateTimeOffset.UtcNow;
        var telemetry = new TelemetryEvent(Guid.NewGuid(), 1, SeriesIdentity.CreateId(key), key, "windfarm01", "windfarm01-client",
            new NumericTelemetryValue(42), "number", "kW", timestamp, timestamp, timestamp, "Good", 0, "correlation");

        Assert.Null(TelemetryValidator.Validate(telemetry));
    }

    [Fact]
    public void RejectsMismatchedSeriesIdentity()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var telemetry = new TelemetryEvent(Guid.NewGuid(), 1, Guid.NewGuid(), "dk.demo.windfarm01.turbine01.active_power", "server", "client",
            new NumericTelemetryValue(42), "number", "kW", timestamp, timestamp, timestamp, "Good", 0, "correlation");

        Assert.Equal("Series ID does not match the canonical series key.", TelemetryValidator.Validate(telemetry));
    }

    [Fact]
    public void RejectsUnregisteredCanonicalSeries()
    {
        const string key = "dk.demo.windfarm01.turbine01.unregistered";
        var timestamp = DateTimeOffset.UtcNow;
        var telemetry = new TelemetryEvent(Guid.NewGuid(), 1, SeriesIdentity.CreateId(key), key, "windfarm01", "windfarm01-client",
            new NumericTelemetryValue(42), "number", "kW", timestamp, timestamp, timestamp, "Good", 0, "correlation");

        Assert.Equal("Series key is not registered.", TelemetryValidator.Validate(telemetry));
    }

    [Fact]
    public void RejectsUnitThatDoesNotMatchRegisteredSeries()
    {
        const string key = "dk.demo.windfarm01.turbine01.active_power";
        var timestamp = DateTimeOffset.UtcNow;
        var telemetry = new TelemetryEvent(Guid.NewGuid(), 1, SeriesIdentity.CreateId(key), key, "windfarm01", "windfarm01-client",
            new NumericTelemetryValue(42), "number", "MW", timestamp, timestamp, timestamp, "Good", 0, "correlation");

        Assert.Equal("Unit does not match the registered series.", TelemetryValidator.Validate(telemetry));
    }
}
