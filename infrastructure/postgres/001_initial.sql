CREATE TABLE IF NOT EXISTS sites (
  id uuid PRIMARY KEY, site_key text NOT NULL UNIQUE, display_name text NOT NULL, country_code text NOT NULL, time_zone text NOT NULL
);
CREATE TABLE IF NOT EXISTS assets (
  id uuid PRIMARY KEY, site_id uuid NOT NULL REFERENCES sites(id), asset_key text NOT NULL, display_name text NOT NULL, asset_type text NOT NULL,
  UNIQUE (site_id, asset_key)
);
CREATE TABLE IF NOT EXISTS opcua_servers (
  id uuid PRIMARY KEY, server_key text NOT NULL UNIQUE, endpoint text NOT NULL, namespace_uri text NOT NULL, display_name text NOT NULL,
  last_status text NOT NULL DEFAULT 'Unknown', last_communication timestamptz
);
CREATE TABLE IF NOT EXISTS opcua_clients (
  id uuid PRIMARY KEY, client_key text NOT NULL UNIQUE, server_id uuid NOT NULL REFERENCES opcua_servers(id),
  last_status text NOT NULL DEFAULT 'Unknown', last_communication timestamptz, reconnection_attempts bigint NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS series_definitions (
  id uuid PRIMARY KEY, series_key text NOT NULL UNIQUE, display_name text NOT NULL, site_id uuid NOT NULL REFERENCES sites(id),
  asset_id uuid NOT NULL REFERENCES assets(id), measurement_id text NOT NULL, measurement_type text NOT NULL, unit text NOT NULL,
  opcua_server_id uuid NOT NULL REFERENCES opcua_servers(id), namespace_uri text NOT NULL, node_id text NOT NULL,
  data_type text NOT NULL, description text NOT NULL DEFAULT '', tags jsonb NOT NULL DEFAULT '{}', created_at timestamptz NOT NULL DEFAULT now(), active boolean NOT NULL DEFAULT true,
  UNIQUE (opcua_server_id, namespace_uri, node_id)
);
CREATE TABLE IF NOT EXISTS telemetry_event_receipts (
  event_id uuid PRIMARY KEY, kafka_topic text NOT NULL, kafka_partition integer NOT NULL, kafka_offset bigint NOT NULL, persisted_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS telemetry_measurements (
  source_timestamp timestamptz NOT NULL, event_id uuid NOT NULL, series_id uuid NOT NULL REFERENCES series_definitions(id),
  numeric_value double precision, boolean_value boolean, text_value text, quality text NOT NULL, opcua_status_code bigint NOT NULL,
  server_timestamp timestamptz NOT NULL, received_timestamp timestamptz NOT NULL, processing_timestamp timestamptz NOT NULL, ingestion_timestamp timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT exactly_one_typed_value CHECK (num_nonnulls(numeric_value, boolean_value, text_value) = 1)
) PARTITION BY RANGE (source_timestamp);
CREATE TABLE IF NOT EXISTS telemetry_measurements_default PARTITION OF telemetry_measurements DEFAULT;
CREATE INDEX IF NOT EXISTS ix_telemetry_series_time ON telemetry_measurements (series_id, source_timestamp DESC);
CREATE INDEX IF NOT EXISTS ix_telemetry_time ON telemetry_measurements (source_timestamp DESC);
CREATE TABLE IF NOT EXISTS service_instances (
  service_name text NOT NULL, instance_id text NOT NULL, service_type text NOT NULL, version text NOT NULL, started_at timestamptz NOT NULL,
  PRIMARY KEY (service_name, instance_id)
);
CREATE TABLE IF NOT EXISTS service_heartbeats (
  service_name text NOT NULL, instance_id text NOT NULL, observed_at timestamptz NOT NULL, health text NOT NULL, counters jsonb NOT NULL DEFAULT '{}', details jsonb NOT NULL DEFAULT '{}',
  PRIMARY KEY (service_name, instance_id), FOREIGN KEY (service_name, instance_id) REFERENCES service_instances(service_name, instance_id)
);
