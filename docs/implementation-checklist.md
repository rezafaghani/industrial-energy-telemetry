# Implementation checklist

`Implemented` means source/configuration exists; `Verified` means the check was executed successfully. Do not interpret implementation as live-system acceptance.

## Foundation

- [x] Architecture decision, service boundaries, typed contracts, stable series identity, and PostgreSQL schema.
- [x] .NET solution build verified (zero warnings/errors).
- [x] Unit tests verified (16 passed).

## OPC UA and ingestion

- [x] Five configurable OPC UA server profiles and real server/client session/subscription workers implemented.
- [x] Correlated simulation, anomalies, failures, deterministic seeds, client retry, and bounded event buffering implemented.
- [ ] Five server/client instances and subscription notifications verified together.
- [ ] Kafka raw-topic publication and full processor/inserter path verified at runtime.

## Processing and persistence

- [x] Processor validation, series metadata checks, dead-letter routing, and explicit offset handling implemented.
- [x] PostgreSQL migrations, stable registry, batched inserts, and event-receipt deduplication implemented.
- [ ] Kafka consumer-group parallelism, database insertion, replay, and outage recovery verified with live infrastructure.

## API and dashboard

- [x] Metadata, health, latest/history, quality/statistics, OpenAPI, and SignalR endpoints implemented.
- [x] React dashboard, series explorer, unit-aware numeric charting, and discrete history implemented.
- [ ] Full five-source data visibility, dashboard status, and SignalR behavior verified end-to-end.
- [ ] Consumer lag and all requested infrastructure/series filters and chart interactions implemented.

## Deployment and verification

- [x] Compose, Helm/Kubernetes templates, CI, and observability configuration exist.
- [x] Compose configuration syntax verified.
- [x] Frontend lockfile install, typecheck, production build, and lint verified.
- [ ] Compose services started and checked healthy; Docker is unavailable in this environment.
- [ ] Testcontainers integration and five-source end-to-end tests passed; Docker is unavailable in this environment.
- [ ] Helm chart rendered/deployed; Helm is unavailable in this environment.

See [IMPLEMENTATION_STATUS.md](../IMPLEMENTATION_STATUS.md) for exact command outcomes and remaining acceptance checks.
- [ ] README matches demonstrated behavior
