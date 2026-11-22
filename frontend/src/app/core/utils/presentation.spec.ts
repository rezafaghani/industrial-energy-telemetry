import { HttpErrorResponse } from '@angular/common/http';
import { errorMessage, formatCell, measurementRow, statusClass } from './presentation';
import type { Measurement, Series } from '../models/telemetry';

describe('telemetry presentation helpers', () => {
  it('formats timestamps, booleans, and missing values', () => {
    expect(formatCell('quality', true)).toBe('Yes');
    expect(formatCell('value', null)).toBe('—');
    expect(formatCell('source_timestamp', 'not-a-date')).toBe('not-a-date');
  });

  it('maps service health to semantic status classes', () => {
    expect(statusClass('Healthy')).toBe('status-good');
    expect(statusClass('Degraded')).toBe('status-warning');
    expect(statusClass('Disconnected')).toBe('status-error');
    expect(statusClass('Unknown')).toBe('status-neutral');
  });

  it('includes the API response body in visible HTTP errors', () => {
    const error = new HttpErrorResponse({
      status: 400,
      statusText: 'Bad Request',
      error: 'invalid server configuration',
    });

    expect(errorMessage(error)).toBe('400 Bad Request: invalid server configuration');
  });

  it('projects a measurement into the history table row shape', () => {
    const series: Series = {
      id: 'series-id',
      series_key: 'dk.demo.site.asset.power',
      display_name: 'Active power',
      site_key: 'site',
      site_name: 'Site',
      asset_key: 'asset',
      asset_name: 'Asset',
      measurement_id: 'active_power',
      measurement_type: 'Power',
      unit: 'kW',
      data_type: 'Double',
      server_key: 'server',
      endpoint: 'opc.tcp://localhost:4840',
      namespace_uri: 'urn:demo',
      node_id: 'ns=2;s=power',
      description: '',
      tags: '{}',
      active: true,
      created_at: '2026-01-01T00:00:00Z',
    };
    const measurement: Measurement = {
      event_id: 'event',
      series_id: 'series-id',
      numeric_value: 42,
      boolean_value: null,
      text_value: null,
      quality: 'Good',
      opcua_status_code: 0,
      source_timestamp: '2026-01-01T00:00:00Z',
      server_timestamp: '2026-01-01T00:00:00Z',
      received_timestamp: '2026-01-01T00:00:01Z',
      processing_timestamp: '2026-01-01T00:00:01Z',
      ingestion_timestamp: '2026-01-01T00:00:02Z',
    };

    expect(measurementRow(series, measurement)).toMatchObject({
      series_key: series.series_key,
      value: 42,
      unit: 'kW',
      quality: 'Good',
    });
  });
});
