# ADR 0001: Platform foundation

- Status: Accepted
- Date: 2026-11-09

## Context and decision

IETP must preserve industrial source semantics while scaling ingestion, processing, persistence, and querying independently. It will use independently deployable .NET 10 processes, Kafka for durable hand-off, PostgreSQL for authoritative metadata and measurements, and a browser-facing API with SignalR. Delivery is at least once; stable event IDs and a database uniqueness boundary make replays safe.

One configurable server executable and one configurable client executable will provide the five instances without copied code. The initial deployment owns each server/client assignment statically. HA client ownership is deferred until the core path works; a lease or leader election can later enforce one active subscriber per assignment.

JSON is the initial versioned event encoding because it is inspectable and supports explicit typed values. Consumers reject unsupported schema versions. A schema registry/Avro migration is justified only when cross-team contract volume makes JSON governance inadequate.

## Repository structure

```text
src/
  Telemetry.Contracts/   versioned wire contracts
  Telemetry.Domain/      series rules and simulation model
  OpcUa.Server/          configurable simulator host (implemented)
  OpcUa.Client/          subscription/Kafka publisher (implemented)
  Telemetry.Processor/   raw consumer and processed producer (implemented)
  Telemetry.Inserter/    processed consumer and PostgreSQL writer (implemented)
  Telemetry.Api/         queries, health, SignalR (implemented)
frontend/                React/Vite UI (implemented)
tests/                   unit, integration, and end-to-end suites
infrastructure/          Compose, Kafka, PostgreSQL, observability, Kubernetes
docs/                    architecture and operations
```

Implementation does not imply live-system acceptance: see [implementation status](../../IMPLEMENTATION_STATUS.md) for executed verification and remaining runtime checks.

## Service responsibility matrix

| Service | Owns | Reads | Writes | Scale boundary |
|---|---|---|---|---|
| OPC UA Server | simulation state and address space | instance config | OPC UA subscriptions | one process per simulated site |
| OPC UA Client | session, subscription, bounded buffer | one OPC UA server; series registry | `telemetry.raw`; heartbeat | exactly one active instance per assignment initially |
| Processor | validation, normalization, enrichment | `telemetry.raw`; series metadata | processed or dead-letter topic | consumer group, bounded by raw partitions |
| Inserter | batching and idempotent persistence | `telemetry.processed` | PostgreSQL | consumer group, bounded by processed partitions |
| API | query validation and live fan-out | PostgreSQL | HTTP/SignalR responses | stateless replicas |
| Frontend | operator presentation | API/SignalR | none | static assets |

## Kafka topics and partitioning

| Topic | Partitions | Key | Retention | Purpose |
|---|---:|---|---|---|
| `telemetry.raw` | 12 | `seriesId` | 7 days | replayable source notifications |
| `telemetry.processed` | 12 | `seriesId` | 7 days | validated/enriched events |
| `telemetry.dead-letter` | 6 | original `seriesId` when known | 30 days | invalid payload plus diagnostic context |

Twelve partitions permit up to twelve useful consumers per group while retaining per-series ordering. Producers use acknowledgements `all`, idempotence, bounded delivery timeout, and retries. Consumers disable auto-commit. A processor publishes (or durably dead-letters) before committing raw offsets. A crash after publish and before commit can duplicate output; the unchanged event ID lets the inserter deduplicate it. Kafka transactions are deliberately deferred because database persistence still requires idempotency.

## PostgreSQL schema proposal

| Table | Key and notable columns |
|---|---|
| `sites` | `id uuid`, unique `site_key`, name, country/time zone |
| `assets` | `id uuid`, `site_id`, unique `(site_id, asset_key)`, type/name |
| `opcua_servers` | `id uuid`, unique `server_key`, endpoint, namespace URI, status |
| `opcua_clients` | `id uuid`, unique `client_key`, `server_id`, status/counters |
| `series_definitions` | `id uuid`, unique `series_key`, unique `(opcua_server_id, namespace_uri, node_id)`, metadata/tags/active |
| `telemetry_event_receipts` | `event_id uuid` primary key, source/partition/offset, persisted timestamp |
| `telemetry_measurements` | source timestamp, event/series IDs, typed value columns, quality/status, ingestion/processing timestamps |
| `service_instances` | `(service_name, instance_id)`, type/version/start time |
| `service_heartbeats` | `(service_name, instance_id)`, observed time, health/counters/details |

Measurements are range-partitioned monthly by `source_timestamp` for pruning and retention. Global deduplication lives in the non-partitioned `telemetry_event_receipts` table because PostgreSQL cannot enforce an event-only unique constraint across time partitions. An inserter transaction first claims receipt IDs with `ON CONFLICT DO NOTHING`, then inserts only claimed measurements. Indexes start with `(series_id, source_timestamp DESC)` and `(source_timestamp DESC)`; more are added from measured query plans, not speculation.

## Series identification specification

- Canonical key: lowercase dot-separated `{country}.{environment}.{site}.{asset}.{measurement}`.
- Segments contain ASCII `a-z`, `0-9`, or `_`, start with alphanumeric, and are stable business identifiers—not display names.
- Internal ID: UUID v5 derived from the canonical key in the fixed IETP namespace `a107c86a-a438-5db7-8d7c-766a1a6d7f31`.
- The registry persists and uniquely constrains both key and OPC UA identity. Startup upserts metadata idempotently and never replaces an ID.
- Events carry both UUID and key; consumers verify the pair against the registry.
- Node identity is `(server ID, namespace URI, expanded NodeId)`, never a runtime namespace index.

Example: `dk.demo.windfarm01.turbine01.active_power` deterministically maps to one UUID on every process and restart.

## Consequences and risks

Static client assignment is operationally simple but does not provide automatic client failover. JSON costs more bandwidth than binary encodings. Database receipt rows add one small write per event but provide correct global replay deduplication. Monthly partitions require scheduled creation and retention operations.
