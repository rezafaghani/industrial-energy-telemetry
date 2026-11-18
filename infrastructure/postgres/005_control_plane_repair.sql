ALTER TABLE opcua_servers
  ADD COLUMN IF NOT EXISTS site_id uuid REFERENCES sites(id),
  ADD COLUMN IF NOT EXISTS application_uri text,
  ADD COLUMN IF NOT EXISTS security_policy text NOT NULL DEFAULT 'http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256',
  ADD COLUMN IF NOT EXISTS security_mode text NOT NULL DEFAULT 'SignAndEncrypt',
  ADD COLUMN IF NOT EXISTS enabled boolean NOT NULL DEFAULT true,
  ADD COLUMN IF NOT EXISTS expected_certificate_identity text,
  ADD COLUMN IF NOT EXISTS certificate_trust_reference text,
  ADD COLUMN IF NOT EXISTS configuration_version bigint NOT NULL DEFAULT 1;

ALTER TABLE series_definitions
  ADD COLUMN IF NOT EXISTS sampling_interval_ms integer NOT NULL DEFAULT 1000 CHECK (sampling_interval_ms > 0),
  ADD COLUMN IF NOT EXISTS publishing_interval_ms integer NOT NULL DEFAULT 1000 CHECK (publishing_interval_ms > 0),
  ADD COLUMN IF NOT EXISTS queue_size integer NOT NULL DEFAULT 10 CHECK (queue_size > 0),
  ADD COLUMN IF NOT EXISTS deadband_type text NOT NULL DEFAULT 'None',
  ADD COLUMN IF NOT EXISTS deadband_value double precision,
  ADD COLUMN IF NOT EXISTS physical_min double precision,
  ADD COLUMN IF NOT EXISTS physical_max double precision,
  ADD COLUMN IF NOT EXISTS configuration_version bigint NOT NULL DEFAULT 1;

CREATE TABLE IF NOT EXISTS collectors (
  id text PRIMARY KEY,
  site_key text,
  capacity integer NOT NULL CHECK (capacity > 0),
  draining boolean NOT NULL DEFAULT false,
  registered_at timestamptz NOT NULL DEFAULT now(),
  last_heartbeat_at timestamptz NOT NULL DEFAULT now(),
  metadata jsonb NOT NULL DEFAULT '{}'
);

CREATE TABLE IF NOT EXISTS collector_health (
  collector_id text PRIMARY KEY REFERENCES collectors(id) ON DELETE CASCADE,
  observed_at timestamptz NOT NULL DEFAULT now(),
  health text NOT NULL,
  active_sessions integer NOT NULL DEFAULT 0 CHECK (active_sessions >= 0),
  buffer_items bigint NOT NULL DEFAULT 0 CHECK (buffer_items >= 0),
  buffer_bytes bigint NOT NULL DEFAULT 0 CHECK (buffer_bytes >= 0),
  last_error text
);

CREATE TABLE IF NOT EXISTS certificate_metadata (
  id uuid PRIMARY KEY,
  identity_reference text NOT NULL UNIQUE,
  subject text NOT NULL,
  thumbprint text NOT NULL,
  not_before timestamptz NOT NULL,
  not_after timestamptz NOT NULL,
  trust_reference text NOT NULL,
  revoked boolean NOT NULL DEFAULT false,
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS collector_assignments (
  endpoint_id uuid PRIMARY KEY REFERENCES opcua_servers(id) ON DELETE CASCADE,
  collector_id text NOT NULL REFERENCES collectors(id) ON DELETE CASCADE,
  lease_epoch bigint NOT NULL CHECK (lease_epoch > 0),
  lease_acquired_at timestamptz NOT NULL,
  lease_expires_at timestamptz NOT NULL,
  configuration_version bigint NOT NULL,
  CHECK (lease_expires_at > lease_acquired_at)
);
CREATE INDEX IF NOT EXISTS ix_collector_assignments_collector_expiry
  ON collector_assignments(collector_id,lease_expires_at);
