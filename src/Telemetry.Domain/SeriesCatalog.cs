namespace Ietp.Telemetry.Domain;

public sealed record SeriesDefinition(
    Guid Id, string Key, string SiteKey, string SiteName, string AssetKey, string AssetName, string AssetType,
    string Measurement, string DisplayName, string Unit, string DataType, string ServerKey, string ClientKey,
    string Endpoint, string NamespaceUri, string NodeId);

public static class SeriesCatalog
{
    public static IReadOnlyList<SeriesDefinition> All { get; } =
    [
        .. Site("windfarm01", "Wind Farm 01", "turbine01", "Turbine 01", "WindTurbine", "windfarm01", "windfarm01-client", "opc.tcp://opcua-wind:4841/ietp", "urn:ietp:dk:demo:windfarm01",
            ("wind_speed", "Wind speed", "m/s", "Double"), ("rotor_speed", "Rotor speed", "rpm", "Double"), ("active_power", "Active power", "kW", "Double"),
            ("generator_temperature", "Generator temperature", "°C", "Double"), ("operational_status", "Operational status", "", "String"), ("cumulative_energy", "Cumulative energy", "kWh", "Double")),
        .. Site("solarfarm01", "Solar Farm 01", "inverter01", "Inverter 01", "SolarInverter", "solarfarm01", "solarfarm01-client", "opc.tcp://opcua-solar:4842/ietp", "urn:ietp:dk:demo:solarfarm01",
            ("solar_irradiance", "Solar irradiance", "W/m²", "Double"), ("panel_temperature", "Panel temperature", "°C", "Double"), ("dc_voltage", "DC voltage", "V", "Double"),
            ("dc_current", "DC current", "A", "Double"), ("active_power", "Active power", "kW", "Double"), ("inverter_status", "Inverter status", "", "String")),
        .. Site("grid01", "Power Grid 01", "feeder01", "Feeder 01", "GridFeeder", "grid01", "grid01-client", "opc.tcp://opcua-grid:4843/ietp", "urn:ietp:dk:demo:grid01",
            ("grid_frequency", "Grid frequency", "Hz", "Double"), ("line_voltage", "Line voltage", "V", "Double"), ("current", "Current", "A", "Double"),
            ("active_power", "Active power", "kW", "Double"), ("reactive_power", "Reactive power", "kVAr", "Double"), ("power_factor", "Power factor", "1", "Double"), ("connection_status", "Connection status", "", "Boolean")),
        .. Site("battery01", "Battery 01", "bess01", "BESS 01", "BatteryStorage", "battery01", "battery01-client", "opc.tcp://opcua-battery:4844/ietp", "urn:ietp:dk:demo:battery01",
            ("state_of_charge", "State of charge", "%", "Double"), ("charge_discharge_power", "Charge/discharge power", "kW", "Double"), ("battery_voltage", "Battery voltage", "V", "Double"),
            ("battery_current", "Battery current", "A", "Double"), ("cell_temperature", "Cell temperature", "°C", "Double"), ("operating_mode", "Operating mode", "", "String")),
        .. Site("plant01", "Industrial Plant 01", "motor01", "Motor 01", "IndustrialMotor", "plant01", "plant01-client", "opc.tcp://opcua-plant:4845/ietp", "urn:ietp:dk:demo:plant01",
            ("motor_speed", "Motor speed", "rpm", "Double"), ("machine_vibration", "Machine vibration", "mm/s", "Double"), ("motor_temperature", "Motor temperature", "°C", "Double"),
            ("energy_consumption", "Energy consumption", "kWh", "Double"), ("equipment_load", "Equipment load", "%", "Double"), ("operational_state", "Operational state", "", "String"))
    ];

    public static Guid EntityId(string kind, string key) => SeriesIdentity.CreateId($"dk.demo.registry.{kind}.{key}");

    private static IEnumerable<SeriesDefinition> Site(
        string siteKey, string siteName, string assetKey, string assetName, string assetType,
        string serverKey, string clientKey, string endpoint, string namespaceUri,
        params (string Name, string DisplayName, string Unit, string DataType)[] measurements)
    {
        foreach (var measurement in measurements)
        {
            var key = SeriesIdentity.CreateKey("dk", "demo", siteKey, assetKey, measurement.Name);
            yield return new SeriesDefinition(SeriesIdentity.CreateId(key), key, siteKey, siteName, assetKey, assetName, assetType,
                measurement.Name, measurement.DisplayName, measurement.Unit, measurement.DataType, serverKey, clientKey,
                endpoint, namespaceUri, $"asset/{assetKey}/{measurement.Name}");
        }
    }
}
