# Implementation status

**Last checked:** 2026-11-18
**Overall:** Core services and dashboard are implemented in source. The complete live platform is not yet verified; do not treat deployment or end-to-end acceptance criteria as passed.

## Verified in this environment

- .NET 11 solution builds with zero warnings and errors.
- Unit tests pass: 17 passed, 0 failed.
- Frontend dependency installation from the lockfile, TypeScript typecheck, production build, and ESLint pass.
- `docker-compose config --quiet` accepts the Compose configuration.

## Implemented, but not verified as a running system

- Five configuration-driven OPC UA server profiles and corresponding real OPC UA client workers with subscriptions, retry, bounded buffering, and Kafka producers.
- Kafka raw/processed/dead-letter/status topics, processor and inserter consumer groups, explicit telemetry offset commits, and PostgreSQL receipt-based deduplication.
- PostgreSQL migrations, stable series catalog, API queries, SignalR broadcaster, and React dashboard.
- Compose, Kubernetes Helm templates, and optional OpenTelemetry/Prometheus/Grafana/Jaeger configuration.
- Additive control-plane schema, PostgreSQL-backed collector registration/health and fenced endpoint leases, coordinator API, and operator views.

The client now reports a bad keep-alive as disconnected and retries with backoff; buffer saturation is counted, logged, and reflected as degraded health. The processor rejects unregistered series and mismatched server/client, unit, or value type before forwarding telemetry. Overview server/client counts require a fresh heartbeat.

## Blocked verification and remaining gaps

- This environment has no Docker executable or daemon/socket. The coordinator Testcontainers test compiles but fails before setup with `DockerUnavailableException`; the end-to-end test has not been run. No live lease, five-server-to-API, or outage/recovery scenario is claimed as verified.
- Dynamic multi-endpoint collection, ingestion fencing, durable edge buffering, alarm evaluation, and collector authentication are not implemented yet; static clients remain the active acquisition path.
- Compose syntax validation is not a Compose startup test.
- Helm is unavailable, so the Kubernetes chart was not rendered or deployed.
- There is no dedicated OPC UA server/client integration test yet.
- Consumer lag is not currently exposed by the API/dashboard. Series filtering and chart interactions are narrower than the full requested specification.
- No production authentication, Kafka/PostgreSQL transport security, or multi-replica SignalR backplane is configured; see README limitations.

## Next acceptance checks

1. Start Docker Compose on a Docker-compatible host, then run Kafka/PostgreSQL integration tests and the five-source end-to-end test.
2. Verify OPC UA subscriptions, disconnect/reconnect, Kafka replay, database outage recovery, and duplicate-event handling against running services.
3. Render the Helm chart with `helm template`, then validate probes, secrets, scaling, and runtime connectivity in a Kubernetes cluster.
4. Add the remaining API/UI coverage for consumer lag, richer infrastructure telemetry, and requested series filters/chart interactions.
