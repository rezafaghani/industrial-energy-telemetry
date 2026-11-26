import { HttpErrorResponse } from '@angular/common/http';
import type { Measurement, Row, Series } from '../models/telemetry';

export function formatCell(key: string, value: unknown): string {
  if (value === null || value === undefined || value === '') return '—';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  if (typeof value === 'string' && /(timestamp|created|started)/i.test(key)) {
    const date = new Date(value);
    if (!Number.isNaN(date.getTime())) return date.toLocaleString();
  }
  return String(value);
}

export function statusClass(status: unknown): string {
  if (status === 'Healthy' || status === 'Connected') return 'status-good';
  if (status === 'Degraded') return 'status-warning';
  if (status === 'Disconnected' || status === 'Unavailable' || status === 'Unhealthy') return 'status-error';
  return 'status-neutral';
}

export function measurementRow(series: Series, measurement: Measurement): Row {
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

export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const body = typeof error.error === 'string'
      ? error.error
      : error.error && typeof error.error === 'object' ? JSON.stringify(error.error) : '';
    const summary = error.status === 0 ? 'Network request failed' : `HTTP ${error.status}`;
    return body ? `${summary}: ${body}` : summary;
  }
  return error instanceof Error ? error.message : 'An unexpected error occurred.';
}
