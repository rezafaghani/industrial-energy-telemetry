CREATE TABLE IF NOT EXISTS platform_settings (
  setting_key text PRIMARY KEY,
  setting_value text NOT NULL,
  updated_at timestamptz NOT NULL DEFAULT now()
);
INSERT INTO platform_settings(setting_key,setting_value) VALUES('telemetry_retention_days','30') ON CONFLICT(setting_key) DO NOTHING;

CREATE OR REPLACE FUNCTION purge_expired_telemetry() RETURNS bigint LANGUAGE plpgsql AS $$
DECLARE deleted_rows bigint;
DECLARE retention_days integer;
BEGIN
  SELECT setting_value::integer INTO retention_days FROM platform_settings WHERE setting_key='telemetry_retention_days';
  DELETE FROM telemetry_measurements WHERE source_timestamp < now() - make_interval(days => retention_days);
  GET DIAGNOSTICS deleted_rows = ROW_COUNT;
  DELETE FROM telemetry_event_receipts r WHERE NOT EXISTS (SELECT 1 FROM telemetry_measurements m WHERE m.event_id=r.event_id) AND r.persisted_at < now() - make_interval(days => retention_days);
  RETURN deleted_rows;
END $$;
