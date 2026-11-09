export type Row = Record<string, string | number | boolean | null>;
export type Overview = { total_servers: number; connected_servers: number; total_clients: number; connected_clients: number; active_processors: number; active_inserters: number; active_series: number; latest_telemetry_timestamp: string | null; events_inserted_per_second: number; dead_letter_count: number; quality_errors: number };
export type Series = { id: string; series_key: string; display_name: string; site_key: string; asset_key: string; measurement_type: string; unit: string; data_type: string; namespace_uri: string; node_id: string; active: boolean };
export type Measurement = { event_id: string; series_id: string; numeric_value: number | null; boolean_value: boolean | null; text_value: string | null; quality: string; source_timestamp: string };

async function get<T>(path: string): Promise<T> {
  const response = await fetch(path);
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  return response.json() as Promise<T>;
}
export const api = {
  overview: () => get<Overview>('/api/overview'), servers: () => get<Row[]>('/api/servers'), clients: () => get<Row[]>('/api/clients'),
  services: () => get<Row[]>('/api/services'), latest: () => get<Row[]>('/api/telemetry/latest'),
  series: (search = '') => get<Series[]>(`/api/series?search=${encodeURIComponent(search)}&pageSize=200`),
  history: (id: string, hours: number) => get<Measurement[]>(`/api/series/${id}/history?from=${encodeURIComponent(new Date(Date.now() - hours * 3600000).toISOString())}&to=${encodeURIComponent(new Date().toISOString())}`)
};
