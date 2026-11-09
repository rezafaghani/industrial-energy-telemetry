using Ietp.Telemetry.Domain.Simulation;

namespace Ietp.OpcUa.Server;

internal static class MeasurementCatalog
{
    public static IReadOnlyDictionary<string, object> For(SimulationProfile profile) => profile switch
    {
        SimulationProfile.WindFarm => Numeric("wind_speed", "rotor_speed", "active_power", "generator_temperature", "cumulative_energy")
            .With("operational_status", "Starting"),
        SimulationProfile.SolarFarm => Numeric("solar_irradiance", "panel_temperature", "dc_voltage", "dc_current", "active_power")
            .With("inverter_status", "Starting"),
        SimulationProfile.PowerGrid => Numeric("grid_frequency", "line_voltage", "current", "active_power", "reactive_power", "power_factor")
            .With("connection_status", false),
        SimulationProfile.BatteryStorage => Numeric("state_of_charge", "charge_discharge_power", "battery_voltage", "battery_current", "cell_temperature")
            .With("operating_mode", "Starting"),
        SimulationProfile.IndustrialPlant => Numeric("motor_speed", "machine_vibration", "motor_temperature", "energy_consumption", "equipment_load")
            .With("operational_state", "Starting"),
        _ => throw new InvalidOperationException($"Unsupported simulation profile: {profile}.")
    };

    private static Dictionary<string, object> Numeric(params string[] names) => names.ToDictionary(name => name, _ => (object)0d);

    private static Dictionary<string, object> With(this Dictionary<string, object> values, string name, object value)
    {
        values.Add(name, value);
        return values;
    }
}
