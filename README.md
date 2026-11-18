# Industrial Energy Telemetry Platform

IETP is an open-source .NET 11 and React platform that moves real OPC UA subscription data through Kafka, validates it in horizontally scalable consumer groups, persists it idempotently in PostgreSQL, and exposes live and historical operations data through ASP.NET Core, SignalR, and an industrial monitoring dashboard.

The project is a portfolio-grade reference for industrial software, power systems, and energy-data engineering. Running services use real OPC UA, Kafka, and PostgreSQL integrations; mocks are limited to isolated tests.

## Architecture

```mermaid
flowchart LR
  S[5 OPC UA servers] --> C[5 assigned OPC UA clients]
  C --> R[(Kafka telemetry.raw)]
  R --> P[Processor group 1..12]
  P --> X[(telemetry.processed)]
  P --> D[(telemetry.dead-letter)]
  X --> I[Inserter group 1..12]
  I --> DB[(PostgreSQL)]
  DB --> A[ASP.NET Core API]
  A -->|HTTP + SignalR| U[React dashboard]
  C & P & I --> H[(telemetry.status)]
  H --> I
```

[ADR 0001](docs/architecture/0001-platform-foundation.md) contains the responsibility matrix, data model, identity rules, and delivery guarantees.

## Stack

- .NET 11, ASP.NET Core, Worker Services
- Official OPC Foundation UA .NET libraries
- Confluent.Kafka and Apache Kafka/KRaft
- PostgreSQL 18 and Npgsql
- React 19, TypeScript, Vite, Material UI, TanStack Query, Recharts, SignalR
- OpenTelemetry Collector, Prometheus, Grafana, Jaeger
- Docker Compose, Helm/Kubernetes, KEDA
- xUnit and Testcontainers

## Industrial simulation

One configurable executable supplies five independently deployed servers:

| Server | Asset | Measurements |
|---|---|---|
| Wind farm | Turbine | wind/rotor speed, power, temperature, status, cumulative energy |
| Solar farm | Inverter | irradiance, temperature, DC voltage/current, power, status |
| Power grid | Feeder | frequency, voltage/current, active/reactive power, power factor, connection |
| Battery | BESS | state of charge, bidirectional power, voltage/current, temperature, mode |
| Plant | Motor | speed, vibration, temperature, energy, load, state |

Seeds, update interval, anomalies, and failure windows are configurable. Solar output follows irradiance and temperature; battery state of charge integrates power; grid power follows voltage, current, and power factor.

OPC UA is used because it preserves typed industrial address spaces, quality/status codes, source timestamps, subscriptions, certificates, and interoperable security policies.

## Delivery and identity

Clients retain typed values, quality, OPC UA status, and source/server/receive timestamps. Idempotent producers key JSON events by stable series UUID. Processors publish processed or dead-letter records before committing raw offsets. Inserters commit processed offsets only after their PostgreSQL transaction succeeds.

Delivery is at least once. A processor crash after publication and before offset commit can duplicate output; the unchanged event ID is claimed in the non-partitioned `telemetry_event_receipts` table before the partitioned measurement insert, making replay harmless.

Canonical keys use `{country}.{environment}.{site}.{asset}.{measurement}`:

```text
dk.demo.windfarm01.turbine01.active_power
dk.demo.solarfarm01.inverter01.dc_voltage
dk.demo.grid01.feeder01.grid_frequency
dk.demo.battery01.bess01.state_of_charge
dk.demo.plant01.motor01.machine_vibration
```

## Control plane

PostgreSQL stores collector registration, health, capacity, site-aware endpoint assignments, configuration versions, certificate metadata, and exclusive ownership leases. The existing API hosts the coordinator logic; all lease decisions use database time and reassignment advances a fencing epoch. OPC UA clients register with the coordinator, report health, acquire one eligible endpoint, fetch its server/tag configuration, and renew ownership periodically. The dashboard shows registered collectors and endpoint leases; see the [assessment](docs/architecture/assessment.md) and [lease design](docs/architecture/coordinator-and-leases.md).

A fixed UUID-v5 namespace maps each key to the same UUID across services and restarts. The idempotent registry also stores site, asset, unit, type, server, namespace URI, NodeId, description, tags, creation time, and active state.

```text
nsu=urn:ietp:dk:demo:windfarm01;s=asset/turbine01/active_power
→ readable series key → stable UUID/Kafka key
→ PostgreSQL receipt + measurement
→ API history → labelled kW chart
```

## Kafka topics

| Topic | Partitions | Purpose |
|---|---:|---|
| `telemetry.raw` | 12 | source notifications and replay |
| `telemetry.processed` | 12 | validated/enriched telemetry |
| `telemetry.dead-letter` | 6 | invalid payload plus diagnostics |
| `telemetry.status` | 6 | operational heartbeat evidence |

Ordering is retained per series. More consumer replicas than partitions do not add parallelism.

## PostgreSQL lifecycle

Embedded migrations are serialized with a PostgreSQL advisory lock and recorded in `schema_migrations`. Storage includes sites, assets, servers/clients, series definitions, partitioned measurements, global event receipts, service instances, and heartbeats.

Application tables are separated by responsibility: `control_plane` contains registry, endpoint, tag, collector, lease, certificate, and configuration metadata; `telemetry` contains event receipts and measurements; `operations` contains service and compatibility health state. Only the migration ledger remains in `public`.

Retention defaults to 30 days in `platform_settings`. Schedule `SELECT purge_expired_telemetry();` during maintenance. Large installations should create monthly partitions ahead of time and drop expired partitions instead of deleting rows.

## Local execution

With Docker Compose or Podman Compose:

```bash
# Docker
docker compose up --build

# Podman
podman compose up --build
```

The default stack runs the wind site with one processor and inserter. Enable
`full-demo` for all five simulated sites, or `scale-out` to start the second
processor and inserter:

```bash
docker compose --profile full-demo --profile scale-out up --build
```

Scale collectors independently; every replica registers under its container hostname. PostgreSQL leases ensure only one replica owns a given endpoint, while idle replicas remain available for failover:

```bash
docker compose --profile full-demo up --build -d --scale client-wind=3
```

Collectors report health and renew their 30-second leases every 10 seconds. On graceful shutdown they release ownership immediately; after a crash another healthy replica acquires the endpoint after lease expiry.

- Dashboard: <http://localhost:3000>
- API/OpenAPI: <http://localhost:8080/openapi/v1.json>
- Liveness/readiness: `/health/live`, `/health/ready`

The PostgreSQL initialization script is mounted read-only and uses a private SELinux label (`:Z`) for Podman on enforcing hosts.

Compose explicitly enables unsecured OPC UA only under `DOTNET_ENVIRONMENT=Development`. Application defaults require `Basic256Sha256`, signing/encryption, explicit trust, SHA-1 rejection, and 2048-bit keys. Production must provision and mount certificates/trust lists.

Optional engineering observability:

```bash
docker compose --profile observability up --build
```

Grafana is on port 3001, Jaeger on 16686, and Prometheus on 9090. The React UI presents operator/domain state; Grafana and Jaeger present engineering metrics and traces.

## Dashboard

- Overview health, active replicas, throughput, latest timestamp, quality/dead-letter counts
- OPC UA endpoints, evidence-based status, communication time, subscriptions/items, retries/errors
- Service uptime, heartbeat, counters, errors, and latency
- Searchable series metadata, including site, asset, server, tags, and NodeId mappings
- Numeric time-series charts and measurement history with source/server/receive/processing/ingestion timestamps, quality, and OPC UA status
- Compatible-unit comparison for up to four series, plus tabular discrete-state history
- Live SignalR refresh, visible loading/errors, and dark/light responsive layout

## Kubernetes

The Helm chart under `infrastructure/kubernetes/ietp` generates all five server/client pairs and independently scales processors/inserters. KEDA uses consumer lag and caps each group at the 12-partition concurrency limit. See [deployment guidance](docs/deployment/kubernetes.md).

## Testing

```bash
dotnet restore IndustrialEnergyTelemetry.slnx
dotnet build IndustrialEnergyTelemetry.slnx --no-restore
dotnet test IndustrialEnergyTelemetry.slnx --no-build --filter "Category!=EndToEnd"

cd frontend
npm ci
npm run typecheck
npm run build
npm run lint
```

The Testcontainers integration tests require a working Docker-compatible daemon. After Compose is healthy, run:

```bash
docker compose --profile full-demo up --build -d
IETP_BASE_URL=http://localhost:3000 dotnet test tests/Telemetry.EndToEndTests
```

Unit tests cover identity, validation, and correlated simulation. Testcontainers integration tests use real Kafka and PostgreSQL. The E2E test requires all five sources to become persisted/queryable and verifies API/frontend visibility. See [implementation status](IMPLEMENTATION_STATUS.md) for what has actually been verified in this workspace.

See [failure scenarios](docs/failure-scenarios/compose.md) for disconnect/reconnect, consumer replacement, database outage/recovery, replay/deduplication, invalid-message dead lettering, and deterministic simulation failure.

## Dependency maintenance

The current baseline uses .NET 11, React 19, OpenTelemetry 1.21, Testcontainers 4.17, Vite 7, and ESLint 10. Exact frontend versions are locked in `frontend/package-lock.json`; NuGet versions are pinned in each project file. Check for updates and advisories with:

```bash
dotnet package list --project IndustrialEnergyTelemetry.slnx --outdated --include-transitive
dotnet package list --project IndustrialEnergyTelemetry.slnx --vulnerable --include-transitive
cd frontend
npm outdated
npm audit
```

Prefer compatible updates with a clean build and test run. Treat OPC UA SDK, Material UI, SignalR, Vite, TypeScript, and test-runner major upgrades as explicit migrations rather than automatic lockfile churn. The current production npm dependency graph and NuGet graph have no known advisories; npm still reports a development-only `picomatch` advisory for build/lint tooling for which no patched published version is currently available.

## Security and limitations

- No real secrets are committed; deployments supply database credentials.
- The Kubernetes Secret is a template and must be replaced by secret management.
- Enable Kafka/PostgreSQL authentication and transport encryption in production.
- The demonstration API has no end-user authentication and must not be internet-exposed as-is.
- Compose Kafka/PostgreSQL are single-node development infrastructure.
- PostgreSQL-backed ownership leases exist, but dynamic multi-endpoint collectors, telemetry fencing enforcement, and a SignalR scale-out backplane remain future work.
- Consumer lag is used by KEDA but is not yet surfaced in the API/dashboard; some requested infrastructure detail and chart interactions remain outstanding.
- The default telemetry partition is suitable for development; automate monthly partitions in production.

## License

[MIT](LICENSE)
