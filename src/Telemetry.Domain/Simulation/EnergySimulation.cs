namespace Ietp.Telemetry.Domain.Simulation;

public sealed class EnergySimulation
{
    private readonly SimulationOptions _options;
    private readonly Random _random;
    private int _tick;
    private double _energyKwh;
    private double _stateOfCharge = 62;

    public EnergySimulation(SimulationOptions options)
    {
        if (options.Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Update interval must be positive.");
        }

        if (options.AnomalyProbability is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Anomaly probability must be between zero and one.");
        }

        _options = options;
        _random = new Random(options.Seed);
    }

    public SimulationSnapshot Next(DateTimeOffset timestamp)
    {
        _tick++;
        var failed = _options.FailAfterTicks is { } start &&
            _tick >= start &&
            _tick < start + _options.FailureDurationTicks;
        var anomaly = !failed && _random.NextDouble() < _options.AnomalyProbability;

        return new SimulationSnapshot(
            timestamp,
            !failed,
            anomaly,
            failed ? new Dictionary<string, object>() : CreateMeasurements(anomaly));
    }

    private Dictionary<string, object> CreateMeasurements(bool anomaly) => _options.Profile switch
    {
        SimulationProfile.WindFarm => Wind(anomaly),
        SimulationProfile.SolarFarm => Solar(anomaly),
        SimulationProfile.PowerGrid => Grid(anomaly),
        SimulationProfile.BatteryStorage => Battery(anomaly),
        SimulationProfile.IndustrialPlant => Plant(anomaly),
        _ => throw new InvalidOperationException($"Unsupported simulation profile: {_options.Profile}.")
    };

    private Dictionary<string, object> Wind(bool anomaly)
    {
        var wind = Clamp(9 + Wave(80, 3.5) + Noise(0.5), 0, 25);
        var activePower = wind < 3 || wind > 22 ? 0 : Math.Min(3000, 0.82 * wind * wind * wind);
        if (anomaly) activePower *= 0.35;
        var rotor = activePower == 0 ? 0 : Clamp(5 + wind * 0.72 + Noise(0.2), 5, 18);
        _energyKwh += activePower * _options.Interval.TotalHours;

        return new()
        {
            ["wind_speed"] = Round(wind),
            ["rotor_speed"] = Round(rotor),
            ["active_power"] = Round(activePower),
            ["generator_temperature"] = Round(35 + activePower / 95 + Noise(0.8)),
            ["operational_status"] = activePower > 0 ? "Running" : "Idle",
            ["cumulative_energy"] = Round(_energyKwh)
        };
    }

    private Dictionary<string, object> Solar(bool anomaly)
    {
        var daylight = Math.Max(0, Math.Sin((_tick % 240) / 240d * Math.PI));
        var irradiance = Clamp(1000 * daylight + Noise(18), 0, 1100);
        var panelTemperature = 18 + irradiance * 0.032 + Noise(0.8);
        var voltage = irradiance < 15 ? 0 : 620 - (panelTemperature - 25) * 1.4;
        var current = voltage == 0 ? 0 : irradiance * 0.78;
        var activePower = voltage * current * 0.965 / 1000;
        if (anomaly) current *= 0.25;

        return new()
        {
            ["solar_irradiance"] = Round(irradiance),
            ["panel_temperature"] = Round(panelTemperature),
            ["dc_voltage"] = Round(voltage),
            ["dc_current"] = Round(current),
            ["active_power"] = Round(voltage * current * 0.965 / 1000),
            ["inverter_status"] = irradiance >= 15 ? "Producing" : "Standby"
        };
    }

    private Dictionary<string, object> Grid(bool anomaly)
    {
        var voltage = 11000 + Wave(45, 90) + Noise(12);
        var current = 185 + Wave(55, 35) + Noise(4);
        var powerFactor = Clamp(0.96 + Wave(110, 0.018), 0.88, 0.999);
        var activePower = Math.Sqrt(3) * voltage * current * powerFactor / 1000;
        var reactivePower = activePower * Math.Tan(Math.Acos(powerFactor));

        return new()
        {
            ["grid_frequency"] = Round(anomaly ? 48.8 : 50 + Wave(25, 0.025) + Noise(0.008), 3),
            ["line_voltage"] = Round(voltage),
            ["current"] = Round(current),
            ["active_power"] = Round(activePower),
            ["reactive_power"] = Round(reactivePower),
            ["power_factor"] = Round(powerFactor, 3),
            ["connection_status"] = !anomaly
        };
    }

    private Dictionary<string, object> Battery(bool anomaly)
    {
        var requestedPower = 450 * Math.Sin(_tick / 45d);
        var power = _stateOfCharge switch
        {
            <= 8 when requestedPower > 0 => 0,
            >= 95 when requestedPower < 0 => 0,
            _ => requestedPower
        };
        if (anomaly) power = 0;
        _stateOfCharge = Clamp(_stateOfCharge - power * _options.Interval.TotalHours / 20, 0, 100);
        var voltage = 720 + _stateOfCharge * 1.4 + Noise(1.5);

        return new()
        {
            ["state_of_charge"] = Round(_stateOfCharge),
            ["charge_discharge_power"] = Round(power),
            ["battery_voltage"] = Round(voltage),
            ["battery_current"] = Round(power * 1000 / voltage),
            ["cell_temperature"] = Round(24 + Math.Abs(power) / 95 + Noise(0.3)),
            ["operating_mode"] = power > 1 ? "Discharging" : power < -1 ? "Charging" : "Idle"
        };
    }

    private Dictionary<string, object> Plant(bool anomaly)
    {
        var load = Clamp(68 + Wave(70, 22) + Noise(2), 15, 100);
        var motorSpeed = anomaly ? 900 : 1485 * load / 100 + Noise(5);
        var consumptionKw = 420 * load / 100;
        _energyKwh += consumptionKw * _options.Interval.TotalHours;

        return new()
        {
            ["motor_speed"] = Round(motorSpeed),
            ["machine_vibration"] = Round(1.1 + load / 55 + (anomaly ? 8 : Noise(0.15))),
            ["motor_temperature"] = Round(31 + load * 0.48 + Noise(0.5)),
            ["energy_consumption"] = Round(_energyKwh),
            ["equipment_load"] = Round(load),
            ["operational_state"] = anomaly ? "Degraded" : "Running"
        };
    }

    private double Wave(double period, double amplitude) => Math.Sin(_tick / period * Math.Tau) * amplitude;
    private double Noise(double amplitude) => (_random.NextDouble() * 2 - 1) * amplitude;
    private static double Clamp(double value, double minimum, double maximum) => Math.Clamp(value, minimum, maximum);
    private static double Round(double value, int digits = 2) => Math.Round(value, digits);
}
