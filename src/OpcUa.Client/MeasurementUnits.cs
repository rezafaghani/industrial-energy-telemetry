namespace Ietp.OpcUa.Client;

internal static class MeasurementUnits
{
    private static readonly IReadOnlyDictionary<string, string> Units = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["wind_speed"] = "m/s", ["rotor_speed"] = "rpm", ["active_power"] = "kW",
        ["generator_temperature"] = "°C", ["cumulative_energy"] = "kWh",
        ["solar_irradiance"] = "W/m²", ["panel_temperature"] = "°C", ["dc_voltage"] = "V", ["dc_current"] = "A",
        ["grid_frequency"] = "Hz", ["line_voltage"] = "V", ["current"] = "A", ["reactive_power"] = "kVAr", ["power_factor"] = "1",
        ["state_of_charge"] = "%", ["charge_discharge_power"] = "kW", ["battery_voltage"] = "V", ["battery_current"] = "A", ["cell_temperature"] = "°C",
        ["motor_speed"] = "rpm", ["machine_vibration"] = "mm/s", ["motor_temperature"] = "°C", ["energy_consumption"] = "kWh", ["equipment_load"] = "%"
    };

    public static string Get(string measurement) => Units.GetValueOrDefault(measurement, string.Empty);
}
