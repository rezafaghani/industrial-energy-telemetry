# Production SCADA evolution assessment

## Current architecture

IETP is a working at-least-once telemetry pipeline. One configurable OPC UA server executable supplies five domain profiles (wind, solar, grid, battery, and plant). Compose and Helm currently deploy one statically configured client per server. Each client creates a secure OPC UA application identity, resolves the configured namespace URI, opens one session, creates one subscription, and sends callbacks through a bounded in-memory channel to an idempotent Kafka producer.

Kafka separates raw acquisition, validation, persistence, and status reporting. The processor validates versioned JSON events and publishes processed or dead-letter records before committing offsets. The inserter batches consumed records, claims stable event IDs in PostgreSQL, and inserts only newly claimed measurements. PostgreSQL also stores sites, assets, server/client metadata, series definitions, and service health. The API queries PostgreSQL and polls new measurements into a global SignalR feed. React renders overview, OPC UA health, services, series metadata, latest values, and historical trends. OpenTelemetry, Compose, Helm, KEDA, CI, Testcontainers, and failure runbooks already exist.

Stateful components are the simulators, OPC UA sessions/subscriptions, Kafka, PostgreSQL, and each client's in-memory channel. Processor/API instances are otherwise stateless; inserter correctness relies on PostgreSQL receipts and Kafka offsets.

## Strengths to preserve

- Stable canonical series keys and deterministic UUID-v5 IDs.
- Namespace-URI plus NodeId identity rather than namespace indexes.
- Official OPC Foundation SDK subscriptions with quality and timestamps.
- Idempotent Kafka producers, explicit consumer commits, stable event IDs, and PostgreSQL deduplication.
- Typed values, bad-quality preservation, replayable Kafka boundaries, and isolated simulation profiles.
- Existing API/UI behavior, deployment profiles, observability, and focused test layers.

## Target architecture

PostgreSQL becomes the control-plane authority for endpoints, tags, collectors, configuration versions, health, certificates, assignments, and fenced ownership leases. The existing API initially hosts coordinator endpoints and assignment logic. Collectors dynamically acquire several endpoints and run an independent session state machine per assignment. Notifications enter a bounded asynchronous path backed by a durable local buffer before Kafka. Live acquisition carries configuration version and ownership epoch; a trusted ingestion boundary rejects stale live epochs while allowing replay of events already durably accepted.

The data plane remains OPC UA → collector → raw Kafka → validation/normalization → normalized Kafka → historian, with a later persisted alarm evaluator beside the historian. Registry/coordinator operations never enter the per-measurement path.

## Gap analysis

| Priority | Gap | Consequence | Incremental response |
|---|---|---|---|
| P0 | Static one-client/one-server ownership; no leases or fencing | Split brain and no automatic failover | PostgreSQL transactional leases using database time and increasing epochs |
| P0 | In-memory client buffer drops when full and is lost on restart | Silent or permanent telemetry loss during Kafka outages | Bounded durable SQLite outbox; delete only after Kafka acknowledgement |
| P0 | Client is a single session worker | Cannot scale collectors across endpoints | Assignment-driven collector with isolated endpoint session tasks |
| P0 | Events lack endpoint, collector, epoch, config version, and replay fields | No trusted stale-owner rejection | Backward-compatible v2 contract and staged topic migration |
| P1 | Registry is seeded from an in-code catalog and lacks endpoint/tag operational settings | Configuration cannot be versioned or coordinated | Extend schema while retaining stable catalog IDs and bootstrap data |
| P1 | Certificate stores exist but rotation/expiry/provider contracts do not | Failover trust and rotation are operationally incomplete | Certificate provider abstraction; metadata only in PostgreSQL |
| P1 | Session health is coarse heartbeat data | Operators cannot diagnose channel/session/subscription state | Persist endpoint session state and revised settings |
| P1 | SignalR broadcasts every new measurement to all clients | Replica duplication and browser overload | Series groups, selective subscriptions, throttled batches |
| P1 | No alarm model or engine | No persisted operational alarm lifecycle | Add after normalized contract and historian fencing stabilize |
| P2 | No consumer-lag collection, Grafana dashboards, or aggregation API | Limited capacity and long-range visibility | Add bounded-cardinality metrics and measured query optimizations |
| P2 | Kubernetes clients are static Deployments without durable edge volumes | Rescheduling can lose buffered data | Collector StatefulSet/PVC or site-local durable storage policy |

## Prioritized implementation plan

1. Align builds and images to .NET 11; keep package behavior unchanged.
2. Add control-plane tables and atomic coordinator operations, then integration-test exclusivity, renewal, expiry, and restart persistence.
3. Introduce backward-compatible coordinator APIs and expose collectors, assignments, and leases in the dashboard.
4. Refactor the existing client into a multi-endpoint collector while retaining the current static mode during migration.
5. Add certificate-provider and per-endpoint session-state abstractions around existing SDK code.
6. Add a durable SQLite outbox and recovery tests; then extend events with ownership/replay metadata and enforce fencing before normalization.
7. Evolve topic names with dual-read/write or configurable aliases; improve validation and historian batches without assuming cross-system exactly-once delivery.
8. Add persisted alarms, selective SignalR subscriptions, metrics, Compose scaling, collector storage in Helm, and failure tests.

## Migration and compatibility

Database changes must be additive embedded SQL migrations guarded by the existing advisory lock. Existing IDs, tables, measurements, and receipts remain valid. New non-null fields require defaults or backfill before constraints. Topic names remain configurable and retain current defaults until a documented cutover; existing JSON schema v1 remains readable during a v2 transition.

Compose must keep the default wind-only profile and `full-demo` behavior while adding coordinator-backed collectors as an opt-in transition. Kubernetes needs graceful termination, lease release, readiness based on assignment processing, and persistent edge-buffer storage tied to collector identity. Production certificate material must be mounted or externally supplied; only metadata and trust references belong in PostgreSQL.

## Breaking-change risks

- Upgrading to .NET 11 requires matching ASP.NET packages, CI SDKs, and container tags.
- Changing event constructors or rejecting missing epochs would break existing producers and retained Kafka records.
- Replacing static clients in one step could interrupt all acquisition.
- Lease TTLs shorter than real reconnect or network latency can cause churn; overly long TTLs slow failover.
- A fencing check that ignores replay state can strand already accepted edge records.
- Multiple API replicas must coordinate exclusively through PostgreSQL, never local coordinator state.
- Collector rescheduling without its durable volume can orphan unflushed events.
