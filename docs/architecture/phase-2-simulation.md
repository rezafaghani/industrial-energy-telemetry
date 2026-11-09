# Phase 2 progress: simulation and OPC UA

- One stateful simulation engine selected by configuration; there are no copied server implementations.
- Wind output follows a bounded cubic power curve and drives rotor speed, temperature, and cumulative energy.
- Solar voltage/current/power follow irradiance and panel temperature.
- Grid active/reactive power follow three-phase voltage, current, and power factor.
- Battery power changes state of charge consistently and respects empty/full limits.
- Plant load drives speed, temperature, consumption, and vibration.
- Seeds, update intervals, anomaly probability, and failure/recovery windows are configurable.
- A configurable OPC UA server exposes the profile measurements as typed nodes with timestamps and quality codes.
- The corresponding OPC UA client discovers its configured namespace, subscribes to monitored values, and publishes typed notifications to Kafka.
- Client keep-alive failures trigger reconnect with backoff; subscription creation runs again for each session.

The solution build and 16 unit tests pass in the current workspace. Live OPC UA client/server communication, all five simultaneous instances, and reconnection behavior have not been verified here because a Docker-compatible runtime is unavailable. See [implementation status](../../IMPLEMENTATION_STATUS.md).
