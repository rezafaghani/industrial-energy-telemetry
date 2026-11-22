export type Row = Record<string, string | number | boolean | null>;

export interface Overview {
  total_servers: number;
  connected_servers: number;
  total_clients: number;
  connected_clients: number;
  active_processors: number;
  active_inserters: number;
  active_series: number;
  latest_telemetry_timestamp: string | null;
  events_inserted_per_second: number;
  dead_letter_count: number;
  quality_errors: number;
}

export interface Series {
  id: string;
  series_key: string;
  display_name: string;
  site_key: string;
  site_name: string;
  asset_key: string;
  asset_name: string;
  measurement_id: string;
  measurement_type: string;
  unit: string;
  data_type: string;
  server_key: string;
  endpoint: string;
  namespace_uri: string;
  node_id: string;
  description: string;
  tags: string;
  active: boolean;
  created_at: string;
}

export interface Measurement {
  event_id: string;
  series_id: string;
  numeric_value: number | null;
  boolean_value: boolean | null;
  text_value: string | null;
  quality: string;
  opcua_status_code: number;
  source_timestamp: string;
  server_timestamp: string;
  received_timestamp: string;
  processing_timestamp: string;
  ingestion_timestamp: string;
}

export interface ServerRegistration {
  serverKey: string;
  displayName: string;
  endpoint: string;
  namespaceUri: string;
  siteKey: string;
  siteName: string;
  assetKey: string;
  assetName: string;
  measurements: string[];
}

export type RemoteState<T> =
  | { kind: 'loading' }
  | { kind: 'success'; data: T }
  | { kind: 'error'; message: string };
