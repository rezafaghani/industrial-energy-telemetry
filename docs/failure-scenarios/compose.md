# Failure and recovery scenarios

These controls are development operations against the local Compose environment. No destructive control endpoint is exposed by the production API.

## OPC UA disconnect and reconnect

```bash
docker compose stop opcua-wind
docker compose start opcua-wind
```

Within 15 seconds the UI should show the wind server/client as disconnected. The assigned client retries with bounded exponential backoff and recreates its subscription after the server returns; health and timestamps should recover automatically.

## Processor termination and group rebalance

```bash
docker compose kill processor-1
docker compose up -d processor-1
docker compose up -d --scale processor-1=3
```

The surviving replica continues consuming. New replicas join the same group. Useful concurrency cannot exceed the 12 raw-topic partitions.

## Inserter database outage

```bash
docker compose stop postgres
docker compose start postgres
```

Inserters cannot commit processed offsets while their transaction fails. Kafka retains the backlog; after PostgreSQL recovers, messages are replayed. The event-receipt primary key prevents duplicate measurements.

## Replay and deduplication

Stop the inserter, reset the consumer group, then restart it:

```bash
docker compose stop inserter-1
docker compose exec kafka /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server kafka:9092 --group telemetry-inserters-v1 --topic telemetry.processed --reset-offsets --to-earliest --execute
docker compose start inserter-1
```

Compare `telemetry_event_receipts` and `telemetry_measurements` counts before and after; replayed event IDs do not add rows.

## Invalid measurement and dead letter

```bash
printf '%s\n' '{"schemaVersion":999,"seriesKey":"invalid"}' | docker compose exec -T kafka /opt/kafka/bin/kafka-console-producer.sh --bootstrap-server kafka:9092 --topic telemetry.raw
docker compose exec kafka /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server kafka:9092 --topic telemetry.dead-letter --from-beginning --max-messages 1
```

The processor publishes the raw payload and validation reason to `telemetry.dead-letter` before committing its raw offset. Processor error/dead-letter counters become visible in the dashboard and Prometheus.

## Deterministic simulated failure

Set `OpcUa__FailAfterTicks` and `OpcUa__FailureDurationTicks` on one server and recreate it. Its measurement status changes to `BadNoCommunication` for the configured interval without killing the process.
