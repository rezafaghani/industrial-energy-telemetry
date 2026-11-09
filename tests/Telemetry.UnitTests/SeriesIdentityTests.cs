using Ietp.Telemetry.Domain;
using Xunit;

namespace Ietp.Telemetry.UnitTests;

public sealed class SeriesIdentityTests
{
    [Fact]
    public void KeyAndIdAreCanonicalAndStable()
    {
        var key = SeriesIdentity.CreateKey("DK", "Demo", "WindFarm01", "Turbine01", "Active_Power");

        Assert.Equal("dk.demo.windfarm01.turbine01.active_power", key);
        Assert.Equal(SeriesIdentity.CreateId(key), SeriesIdentity.CreateId(key));
    }

    [Fact]
    public void RegistryEntityIdAcceptsClientKeysWithHyphens()
    {
        var id = SeriesCatalog.EntityId("client", "windfarm01-client");

        Assert.NotEqual(Guid.Empty, id);
    }

    [Theory]
    [InlineData("dk.demo.site.asset")]
    [InlineData("dk.demo.site.asset.active-power")]
    [InlineData("DK.demo.site.asset.active_power")]
    public void IdRejectsNonCanonicalKey(string key) =>
        Assert.Throws<ArgumentException>(() => SeriesIdentity.CreateId(key));
}
