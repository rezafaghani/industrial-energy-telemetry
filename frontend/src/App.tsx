import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  AppBar,
  Box,
  Card,
  CardContent,
  Chip,
  CssBaseline,
  FormControl,
  FormHelperText,
  InputLabel,
  MenuItem,
  Select,
  Stack,
  Tab,
  Tabs,
  TextField,
  ThemeProvider,
  Typography,
  createTheme,
} from '@mui/material';
import {
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { api, type Measurement, type Row, type Series } from './api';

const chartColors = ['#36d399', '#60a5fa', '#f59e0b', '#f472b6'];
const emptySeries: Series[] = [];

function statusColor(status: unknown): 'success' | 'warning' | 'error' | 'default' {
  if (status === 'Healthy' || status === 'Connected') return 'success';
  if (status === 'Degraded') return 'warning';
  if (status === 'Disconnected' || status === 'Unavailable' || status === 'Unhealthy') return 'error';
  return 'default';
}

export default function App() {
  const [page, setPage] = useState(0);
  const [dark, setDark] = useState(true);
  const queryClient = useQueryClient();

  useEffect(() => {
    let active = true;
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/telemetry')
      .withAutomaticReconnect()
      .build();
    connection.on('telemetry', () => {
      void queryClient.invalidateQueries({ queryKey: ['overview'] });
      void queryClient.invalidateQueries({ queryKey: ['latest'] });
      void queryClient.invalidateQueries({ queryKey: ['history'] });
    });
    void connection.start().catch(error => {
      if (active) console.error('Telemetry live connection failed.', error);
    });
    return () => {
      active = false;
      void connection.stop();
    };
  }, [queryClient]);

  const theme = useMemo(() => createTheme({
    palette: {
      mode: dark ? 'dark' : 'light',
      primary: { main: '#36d399' },
      background: { default: dark ? '#07111c' : '#eef3f7', paper: dark ? '#101e2c' : '#fff' },
    },
    typography: { fontFamily: 'Inter, system-ui, sans-serif' },
    shape: { borderRadius: 10 },
  }), [dark]);

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <AppBar position="sticky" color="transparent" elevation={0}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" px={{ xs: 2, md: 3 }} py={1}>
          <Box>
            <Typography variant="overline" color="primary">IETP Control Room</Typography>
            <Typography variant="h5">Industrial Energy Telemetry</Typography>
          </Box>
          <Chip
            onClick={() => setDark(!dark)}
            label={dark ? 'Dark' : 'Light'}
            variant="outlined"
            aria-label="Toggle color theme"
          />
        </Stack>
        <Tabs value={page} onChange={(_, value: number) => setPage(value)} variant="scrollable">
          <Tab label="Overview" />
          <Tab label="OPC UA" />
          <Tab label="Services" />
          <Tab label="Series Explorer" />
        </Tabs>
      </AppBar>
      <Box component="main" p={{ xs: 2, md: 3 }}>
        {page === 0 && <Overview />}
        {page === 1 && <Infrastructure />}
        {page === 2 && <Services />}
        {page === 3 && <Explorer />}
      </Box>
    </ThemeProvider>
  );
}

function Overview() {
  const overview = useQuery({ queryKey: ['overview'], queryFn: api.overview, refetchInterval: 5000 });
  const latest = useQuery({ queryKey: ['latest'], queryFn: api.latest, refetchInterval: 5000 });
  const data = overview.data;
  const metrics = data ? [
    ['Servers', `${data.connected_servers}/${data.total_servers}`],
    ['Clients', `${data.connected_clients}/${data.total_clients}`],
    ['Processors', data.active_processors],
    ['Inserters', data.active_inserters],
    ['Events inserted/s', Number(data.events_inserted_per_second).toFixed(1)],
    ['Active series', data.active_series],
    ['Dead letters', data.dead_letter_count],
    ['Quality errors (1h)', data.quality_errors],
    ['Latest telemetry', data.latest_telemetry_timestamp
      ? new Date(data.latest_telemetry_timestamp).toLocaleTimeString()
      : '—'],
  ] : [];

  return (
    <Stack spacing={3}>
      <QueryNotice loading={overview.isLoading} error={overview.error} />
      {metrics.length > 0 && (
        <Box className="metric-grid">
          {metrics.map(([label, value]) => (
            <Card key={String(label)}>
              <CardContent>
                <Typography color="text.secondary" variant="overline">{label}</Typography>
                <Typography variant="h4">{value}</Typography>
              </CardContent>
            </Card>
          ))}
        </Box>
      )}
      <Panel title="Latest measurements">
        <QueryNotice loading={latest.isLoading} error={latest.error} />
        {latest.data && <DataTable rows={latest.data} emptyMessage="No telemetry has been persisted yet." />}
      </Panel>
    </Stack>
  );
}

function Infrastructure() {
  const servers = useQuery({ queryKey: ['servers'], queryFn: api.servers, refetchInterval: 5000 });
  const clients = useQuery({ queryKey: ['clients'], queryFn: api.clients, refetchInterval: 5000 });

  return (
    <Stack spacing={3}>
      <Panel title="OPC UA servers">
        <QueryNotice loading={servers.isLoading} error={servers.error} />
        {servers.data && <StatusCards rows={servers.data} emptyMessage="No OPC UA servers are registered." />}
      </Panel>
      <Panel title="Assigned clients">
        <QueryNotice loading={clients.isLoading} error={clients.error} />
        {clients.data && <StatusCards rows={clients.data} emptyMessage="No OPC UA clients are registered." />}
      </Panel>
    </Stack>
  );
}

function Services() {
  const services = useQuery({ queryKey: ['services'], queryFn: api.services, refetchInterval: 5000 });

  return (
    <Panel title="Service instances">
      <QueryNotice loading={services.isLoading} error={services.error} />
      {services.data && <StatusCards rows={services.data} emptyMessage="No service heartbeats have been received." />}
    </Panel>
  );
}

function Explorer() {
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<string[]>([]);
  const [hours, setHours] = useState(1);
  const seriesQuery = useQuery({ queryKey: ['series'], queryFn: () => api.series() });
  const allSeries = seriesQuery.data ?? emptySeries;
  const filteredSeries = useMemo(() => {
    const term = search.trim().toLocaleLowerCase();
    return term
      ? allSeries.filter(item => `${item.series_key} ${item.display_name} ${item.site_name} ${item.asset_name}`
        .toLocaleLowerCase().includes(term))
      : allSeries;
  }, [allSeries, search]);
  const chosen = useMemo(
    () => selected.map(id => allSeries.find(item => item.id === id)).filter((item): item is Series => !!item),
    [allSeries, selected],
  );
  const histories = useQueries({
    queries: chosen.map(item => ({
      queryKey: ['history', item.id, hours],
      queryFn: () => api.history(item.id, hours),
      refetchInterval: 10000,
    })),
  });
  const selectedUnit = chosen[0]?.unit;
  const numeric = chosen.length > 0 && chosen.every(item => item.data_type === 'Double');

  const chart = useMemo(() => {
    const points = new Map<string, Record<string, string | number | null>>();
    histories.forEach((query, index) => {
      for (const measurement of query.data ?? []) {
        if (measurement.numeric_value === null) continue;
        const timestamp = measurement.source_timestamp;
        const point = points.get(timestamp) ?? {
          timestamp,
          time: new Date(timestamp).toLocaleTimeString(),
        };
        point[`series_${index}`] = measurement.numeric_value;
        points.set(timestamp, point);
      }
    });
    return [...points.values()].sort((a, b) => String(a.timestamp).localeCompare(String(b.timestamp)));
  }, [histories]);

  const historyRows = useMemo(
    () => chosen.flatMap((series, index) => (histories[index]?.data ?? [])
      .map(measurement => measurementRow(series, measurement))),
    [chosen, histories],
  );
  const selectedHistoryError = histories.find(query => query.error)?.error ?? null;
  const selectedHistoryLoading = histories.some(query => query.isLoading);

  const handleSelection = (value: string | string[]) => {
    const next = typeof value === 'string' ? value.split(',') : value;
    if (next.length > 0) {
      const nextUnits = new Set(next.map(id => allSeries.find(item => item.id === id)?.unit));
      if (nextUnits.size > 1) return;
    }
    setSelected(next.slice(0, 4));
  };

  return (
    <Stack spacing={3}>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems={{ md: 'flex-start' }}>
        <TextField
          label="Search series, site, or asset"
          value={search}
          onChange={event => setSearch(event.target.value)}
          fullWidth
        />
        <FormControl fullWidth>
          <InputLabel id="series-select-label">Series (up to 4, same unit)</InputLabel>
          <Select
            labelId="series-select-label"
            multiple
            label="Series (up to 4, same unit)"
            value={selected}
            onChange={event => handleSelection(event.target.value)}
            renderValue={ids => ids
              .map(id => allSeries.find(item => item.id === id)?.series_key ?? id)
              .join(', ')}
          >
            {filteredSeries.map(item => (
              <MenuItem
                key={item.id}
                value={item.id}
                disabled={
                  (selected.length >= 4 && !selected.includes(item.id))
                  || (selected.length > 0 && !selected.includes(item.id) && item.unit !== selectedUnit)
                }
              >
                {item.display_name} · {item.site_key}/{item.asset_key} · {item.unit || 'discrete'}
              </MenuItem>
            ))}
          </Select>
          <FormHelperText>
            Compare up to four series that share a unit. Discrete series show as values rather than a chart.
          </FormHelperText>
        </FormControl>
        <FormControl sx={{ minWidth: { md: 160 } }}>
          <InputLabel id="history-range-label">Range</InputLabel>
          <Select
            labelId="history-range-label"
            label="Range"
            value={hours}
            onChange={event => setHours(Number(event.target.value))}
          >
            <MenuItem value={1}>1 hour</MenuItem>
            <MenuItem value={6}>6 hours</MenuItem>
            <MenuItem value={24}>24 hours</MenuItem>
          </Select>
        </FormControl>
      </Stack>

      <QueryNotice loading={seriesQuery.isLoading} error={seriesQuery.error} />
      {seriesQuery.isSuccess && allSeries.length === 0 && (
        <Alert severity="info">No series are registered yet.</Alert>
      )}
      {seriesQuery.isSuccess && filteredSeries.length === 0 && allSeries.length > 0 && (
        <Alert severity="info">No series match “{search}”.</Alert>
      )}

      {chosen.map((item, index) => (
        <SeriesMetadata key={item.id} series={item} measurements={histories[index]?.data ?? []} />
      ))}

      {chosen.length > 0 && (
        <>
          <Panel title={numeric ? `Time series · ${selectedUnit}` : 'Discrete measurements'}>
            <QueryNotice loading={selectedHistoryLoading} error={selectedHistoryError} />
            {numeric && chart.length > 0 && (
              <Card className="chart-card">
                <CardContent className="chart-content">
                  <ResponsiveContainer width="100%" height="100%">
                    <LineChart data={chart} margin={{ top: 12, right: 20, bottom: 8, left: 8 }}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis dataKey="time" minTickGap={24} />
                      <YAxis label={{ value: selectedUnit ?? '', angle: -90, position: 'insideLeft' }} />
                      <Tooltip />
                      <Legend />
                      {chosen.map((item, index) => (
                        <Line
                          key={item.id}
                          type="monotone"
                          dataKey={`series_${index}`}
                          name={item.display_name}
                          stroke={chartColors[index % chartColors.length]}
                          dot={false}
                          connectNulls
                          isAnimationActive={false}
                        />
                      ))}
                    </LineChart>
                  </ResponsiveContainer>
                </CardContent>
              </Card>
            )}
            {numeric && chart.length === 0 && !selectedHistoryLoading && !selectedHistoryError && (
              <Alert severity="info">No numeric samples were found in this time range.</Alert>
            )}
          </Panel>
          <Panel title="Measurement history">
            <QueryNotice loading={selectedHistoryLoading} error={selectedHistoryError} />
            <DataTable rows={historyRows} emptyMessage="No measurements were found in this time range." />
          </Panel>
        </>
      )}
      {chosen.length === 0 && seriesQuery.isSuccess && (
        <Alert severity="info">Select one or more series to view metadata and measurements.</Alert>
      )}
    </Stack>
  );
}

function measurementRow(series: Series, measurement: Measurement): Row {
  return {
    series_key: series.series_key,
    value: measurement.numeric_value ?? measurement.boolean_value ?? measurement.text_value,
    unit: series.unit,
    quality: measurement.quality,
    opcua_status_code: measurement.opcua_status_code,
    source_timestamp: measurement.source_timestamp,
    server_timestamp: measurement.server_timestamp,
    received_timestamp: measurement.received_timestamp,
    processing_timestamp: measurement.processing_timestamp,
    ingestion_timestamp: measurement.ingestion_timestamp,
  };
}

function SeriesMetadata({ series, measurements }: { series: Series; measurements: Measurement[] }) {
  const latest = measurements[0];
  const metadata: Array<[string, string | number | boolean | null]> = [
    ['Series key', series.series_key],
    ['Measurement', series.measurement_id],
    ['Site', `${series.site_name} (${series.site_key})`],
    ['Asset', `${series.asset_name} (${series.asset_key})`],
    ['Data type', series.data_type],
    ['Measurement type', series.measurement_type],
    ['Unit', series.unit || 'Discrete'],
    ['OPC UA server', series.server_key],
    ['Endpoint', series.endpoint],
    ['Namespace URI', series.namespace_uri],
    ['Node ID', series.node_id],
    ['Active', series.active],
    ['Tags', series.tags],
    ['Created', series.created_at],
  ];

  return (
    <Card>
      <CardContent>
        <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={1}>
          <Box>
            <Typography variant="h6">{series.display_name}</Typography>
            <Typography color="text.secondary">{series.description || 'No description provided.'}</Typography>
          </Box>
          <Chip
            label={latest ? `Latest: ${formatCell('source_timestamp', latest.source_timestamp)}` : 'No samples in range'}
            color={latest ? statusColor(latest.quality === 'Good' ? 'Healthy' : 'Degraded') : 'default'}
          />
        </Stack>
        <Box className="metadata-grid" mt={2}>
          {metadata.map(([label, value]) => (
            <Box key={label} className="metadata-item">
              <Typography variant="caption" color="text.secondary">{label}</Typography>
              <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>
                {label === 'Tags' ? formatJson(value) : formatCell(label, value)}
              </Typography>
            </Box>
          ))}
        </Box>
      </CardContent>
    </Card>
  );
}

function formatJson(value: unknown): string {
  if (typeof value !== 'string') return formatCell('', value);
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

function formatCell(key: string, value: unknown): string {
  if (value === null || value === undefined || value === '') return '—';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  if (typeof value === 'string' && /(timestamp|created|started)/i.test(key)) {
    const date = new Date(value);
    if (!Number.isNaN(date.getTime())) return date.toLocaleString();
  }
  return String(value);
}

function Panel({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Box>
      <Typography variant="h6" mb={2}>{title}</Typography>
      {children}
    </Box>
  );
}

function QueryNotice({ loading, error }: { loading: boolean; error: Error | null }) {
  if (error) return <Alert severity="error">{error.message}</Alert>;
  if (loading) return <Alert severity="info">Loading…</Alert>;
  return null;
}

function StatusCards({ rows, emptyMessage }: { rows: Row[]; emptyMessage: string }) {
  if (rows.length === 0) return <Alert severity="info">{emptyMessage}</Alert>;
  return (
    <Box className="status-grid">
      {rows.map((row, index) => {
        const status = row.status ?? row.last_status ?? row.health ?? 'Unknown';
        const label = row.display_name ?? row.client_key ?? row.instance_id ?? row.service_name ?? 'Instance';
        return (
          <Card key={String(row.id ?? row.instance_id ?? index)}>
            <CardContent>
              <Stack direction="row" justifyContent="space-between" gap={1} mb={1}>
                <Typography variant="h6">{String(label)}</Typography>
                <Chip size="small" label={String(status)} color={statusColor(status)} />
              </Stack>
              {Object.entries(row)
                .filter(([key]) => !['id', 'status', 'last_status', 'health'].includes(key))
                .map(([key, value]) => (
                  <Typography key={key} variant="body2" color="text.secondary" className="status-detail">
                    <b>{key.replaceAll('_', ' ')}:</b> {formatCell(key, value)}
                  </Typography>
                ))}
            </CardContent>
          </Card>
        );
      })}
    </Box>
  );
}

function DataTable({ rows, emptyMessage }: { rows: Row[]; emptyMessage: string }) {
  if (rows.length === 0) return <Alert severity="info">{emptyMessage}</Alert>;
  const keys = Object.keys(rows[0]);
  return (
    <Box className="table-wrap">
      <table>
        <thead>
          <tr>{keys.map(key => <th key={key}>{key.replaceAll('_', ' ')}</th>)}</tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={String(row.event_id ?? row.series_id ?? `${index}`)}>
              {keys.map(key => <td key={key}>{formatCell(key, row[key])}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </Box>
  );
}
