# Phase 2 progress: simulation domain

Implemented on 2026-11-09:

- One stateful simulation engine selected by configuration; there are no copied server implementations.
- Wind output follows a bounded cubic power curve and drives rotor speed, temperature, and cumulative energy.
- Solar voltage/current/power follow irradiance and panel temperature.
- Grid active/reactive power follow three-phase voltage, current, and power factor.
- Battery power changes state of charge consistently and respects empty/full limits.
- Plant load drives speed, temperature, consumption, and vibration.
- Seeds, update intervals, anomaly probability, and failure/recovery windows are configurable.

Build result: `dotnet build IndustrialEnergyTelemetry.slnx --no-restore -m:1 /nodeReuse:false` succeeded with zero warnings and errors.

Unit tests were added for all profiles, repeatability, battery behavior, and failure recovery. They compile, but VSTest execution remains blocked because this environment denies the runner's local TCP socket.

The official OPC Foundation `OPCFoundation.NetStandard.Opc.Ua.Server` 2.0.1 package was selected from the project's stable release. Package integration and the real address-space host remain incomplete.
