# Implementation checklist

Actual status only; an unchecked item is not implemented or has not been verified.

## Phase 1 — Foundation

- [x] Architecture decision and repository proposal
- [x] Service responsibility matrix
- [x] Topic/partition and delivery strategy
- [x] PostgreSQL schema proposal
- [x] Stable series identity specification
- [x] Versioned typed telemetry contract
- [x] Foundation build verified (zero warnings/errors)
- [ ] Unit tests executed (runner blocked by environment socket policy on 2026-11-09)

## Phase 2 — OPC UA

- [ ] Real configurable OPC UA server with five simulation profiles
- [x] Correlated simulation, anomalies, failures, and deterministic seeds
- [ ] Real client session, discovery, subscription, and reconnect
- [ ] Certificates and explicit development trust policy
- [ ] Five server/client instances verified simultaneously

## Phase 3 — Kafka ingestion

- [ ] Idempotent producer and bounded client buffering
- [ ] Topic initialization with partition counts
- [ ] Raw events verified in Kafka

## Phase 4 — Processing

- [ ] Validation, normalization, enrichment, and dead lettering
- [ ] Explicit offset handling and replica scaling verified

## Phase 5 — Persistence

- [ ] Migrations, registry seed, partitioned telemetry schema
- [ ] Batched idempotent inserter and outage recovery

## Phase 6 — API

- [ ] Metadata, health, latest, history, quality, and statistics endpoints
- [ ] Pagination/filtering/OpenAPI/validation
- [ ] SignalR live updates

## Phase 7 — UI

- [ ] Responsive dark monitoring dashboard
- [ ] OPC UA and service monitoring pages
- [ ] Series explorer and compatible-unit charts
- [ ] Live status and telemetry

## Phase 8 — Reliability and observability

- [ ] OpenTelemetry traces, metrics, and structured logs
- [ ] Correct liveness/readiness and failure scenarios
- [ ] Local collector, Prometheus, Grafana, and Jaeger

## Phase 9 — Deployment and CI/CD

- [ ] Complete Compose deployment (5+5, 2 processors, 2 inserters)
- [ ] Kubernetes resources, probes, limits, storage, and lag scaling guidance
- [ ] GitHub Actions build/test/frontend/image/security workflows

## Phase 10 — Verification

- [ ] Unit and real-infrastructure integration tests
- [ ] Five-source end-to-end persistence/API/UI test
- [ ] Reconnect, replay, deduplication, and database recovery verified
- [ ] README matches demonstrated behavior
