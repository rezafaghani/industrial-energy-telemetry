# Copilot instructions for Industrial Energy Telemetry Platform

## Build, test, and lint commands

Use the repository's real test and build flows rather than ad hoc commands.

### .NET solution

```bash
dotnet restore IndustrialEnergyTelemetry.slnx
dotnet build IndustrialEnergyTelemetry.slnx --no-restore

dotnet test IndustrialEnergyTelemetry.slnx --no-build
```

Run a single .NET test by project and test name filter:

```bash
dotnet test tests/Telemetry.UnitTests/Telemetry.UnitTests.csproj --filter "FullyQualifiedName~SeriesIdentityTests"
dotnet test tests/Telemetry.IntegrationTests/Telemetry.IntegrationTests.csproj --filter "Category=Integration&FullyQualifiedName~KafkaRoundTripTests"
```

The end-to-end suite requires a healthy local stack and Docker-compatible runtime:

```bash
IETP_BASE_URL=http://localhost:3000 dotnet test tests/Telemetry.EndToEndTests --filter "Category=EndToEnd"
```

### Frontend

```bash
cd frontend
npm ci
npm run typecheck
npm run build
npm run lint
```

### Local stack startup

```bash
docker compose up --build
# or
podman compose up --build
```

The app expects Kafka, PostgreSQL, and OPC UA services to be running for integration and E2E checks.

## High-level architecture

This repository is a distributed industrial telemetry platform built around a real data flow, not a single app:

- `src/OpcUa.Server/` hosts the simulated industrial assets and exposes OPC UA address spaces.
- `src/OpcUa.Client/` subscribes to OPC UA data and publishes raw messages to Kafka.
- `src/Telemetry.Processor/` validates and normalizes telemetry from `telemetry.raw`, then writes processed or dead-letter records.
- `src/Telemetry.Inserter/` consumes processed telemetry and writes idempotently to PostgreSQL.
- `src/Telemetry.Api/` serves queries and live updates over HTTP + SignalR.
- `src/Telemetry.Domain/` contains canonical series identity rules, validation logic, and simulation behavior.
- `src/Telemetry.Contracts/` defines typed event models shared across services.
- `frontend/` is a separate Angular 22 + Angular Material single-page dashboard that consumes the API and SignalR stream.

The big-picture data path is:

OPC UA sources -> Kafka `telemetry.raw` -> Processor -> Kafka `telemetry.processed` -> Inserter -> PostgreSQL -> API + SignalR -> Angular dashboard.

This codebase intentionally separates responsibilities by service and scaling boundary; changes that cross raw/processed/data-layer boundaries should be read with the surrounding services in mind.

## Key conventions

### Canonical telemetry identity

Series identity is stable and business-driven rather than runtime-generated. Keys follow the canonical pattern:

```text
{country}.{environment}.{site}.{asset}.{measurement}
```

Example:

```text
dk.demo.windfarm01.turbine01.active_power
```

The key is normalized to lowercase and mapped to a deterministic UUID v5. `SeriesIdentity` and `SeriesCatalog` are the authority for canonicalization and registry behavior. Preserve this pattern in any new series metadata or tests.

### Idempotency and replay safety

This project treats replay as a normal operating condition. Raw-to-processed handoff and processed-to-database writes are designed to tolerate duplicates. The implementation relies on stable event IDs and PostgreSQL receipt tracking (`telemetry_event_receipts`) before final measurement inserts.

When changing ingestion or persistence code, preserve replay-safe semantics rather than assuming single delivery.

### Worker/service composition

Most .NET services follow the same pattern:

- `Program.cs` calls `Host.CreateApplicationBuilder(args)`
- `AddOptions<T>()` binds strongly typed config
- `AddHostedService<...Worker>()` registers background workers
- config validation runs at startup via `.ValidateOnStart()`

This is the expected shape for new services or background workers.

### Test patterns

Tests are organized by concern:

- `tests/Telemetry.UnitTests/` for identity, validation, and deterministic simulation rules
- `tests/Telemetry.IntegrationTests/` for Kafka/PostgreSQL integration paths
- `tests/Telemetry.EndToEndTests/` for the full stack path

The project uses xUnit, with most behaviors expressed as `[Fact]` and `[Theory]` tests. Keep assertions focused on the domain invariants rather than incidental implementation details.

### Frontend conventions

The dashboard is an Angular SPA with TypeScript, Angular Material, Angular Router, HttpClient/RxJS, ng2-charts/Chart.js, and SignalR. Split features into standalone page and reusable components, use Material controls/widgets, and keep API contracts aligned with the backend.

### Operational assumptions

The repository is explicitly designed for Docker Compose and Kubernetes deployments and assumes real infrastructure behavior: Kafka, PostgreSQL, and OPC UA are first-class dependencies. Avoid writing code that assumes a purely in-memory or single-process execution path unless the change is intentionally isolated to a unit test.
