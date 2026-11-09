# Observability

Start the optional local stack with `docker compose --profile observability up --build`. The OpenTelemetry Collector is the single OTLP destination. It batches telemetry, exports traces to Jaeger, exposes metrics for Prometheus, and writes diagnostic logs. Grafana uses Prometheus for operational dashboards and links to Jaeger for trace investigation.

The React dashboard serves operator-facing domain state: sites, OPC UA connectivity, latest values, quality, and service instances. Grafana/Jaeger serve engineering diagnostics. Keeping those responsibilities separate avoids duplicating a general metrics explorer in the product UI.
