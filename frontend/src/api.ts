export type Row = Record<string, string | number | boolean | null>;
export type Overview = {
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
};
export type Series = {
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
};
export type Measurement = {
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
};

async function get<T>(path: string): Promise<T> {
  const response = await fetch(path);
  if (!response.ok) {
    const body = await response.text();
    throw new Error(`${response.status} ${response.statusText}${body ? `: ${body}` : ''}`);
  }
  return response.json() as Promise<T>;
}
export const api = {
  overview: () => get<Overview>('/api/overview'),
  servers: () => get<Row[]>('/api/servers'),
  clients: () => get<Row[]>('/api/clients'),
  services: () => get<Row[]>('/api/services'),
  latest: () => get<Row[]>('/api/telemetry/latest'),
  series: (search = '') => get<Series[]>(`/api/series?search=${encodeURIComponent(search)}&pageSize=200`),
  history: (id: string, hours: number) => {
    const to = new Date();
    const from = new Date(to.getTime() - hours * 60 * 60 * 1000);
    const query = new URLSearchParams({ from: from.toISOString(), to: to.toISOString(), limit: '1000' });
    return get<Measurement[]>(`/api/series/${id}/history?${query}`);
  }
};
