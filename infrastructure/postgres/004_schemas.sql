CREATE SCHEMA IF NOT EXISTS control_plane;
CREATE SCHEMA IF NOT EXISTS telemetry;
CREATE SCHEMA IF NOT EXISTS operations;

ALTER TABLE IF EXISTS public.sites SET SCHEMA control_plane;
ALTER TABLE IF EXISTS public.assets SET SCHEMA control_plane;
ALTER TABLE IF EXISTS public.opcua_servers SET SCHEMA control_plane;
ALTER TABLE IF EXISTS public.series_definitions SET SCHEMA control_plane;
ALTER TABLE IF EXISTS public.collectors SET SCHEMA control_plane;
ALTER TABLE IF EXISTS public.collector_health SET SCHEMA control_plane;
ALTER TABLE IF EXISTS public.collector_assignments SET SCHEMA control_plane;
ALTER TABLE IF EXISTS public.certificate_metadata SET SCHEMA control_plane;
ALTER TABLE IF EXISTS public.configuration_versions SET SCHEMA control_plane;

ALTER TABLE IF EXISTS public.telemetry_event_receipts SET SCHEMA telemetry;
ALTER TABLE IF EXISTS public.telemetry_measurements SET SCHEMA telemetry;
ALTER TABLE IF EXISTS public.telemetry_measurements_default SET SCHEMA telemetry;

ALTER TABLE IF EXISTS public.opcua_clients SET SCHEMA operations;
ALTER TABLE IF EXISTS public.service_instances SET SCHEMA operations;
ALTER TABLE IF EXISTS public.service_heartbeats SET SCHEMA operations;
ALTER TABLE IF EXISTS public.platform_settings SET SCHEMA operations;
DO $$
BEGIN
  IF to_regprocedure('public.purge_expired_telemetry()') IS NOT NULL THEN
    ALTER FUNCTION public.purge_expired_telemetry() SET SCHEMA telemetry;
  END IF;
END $$;
