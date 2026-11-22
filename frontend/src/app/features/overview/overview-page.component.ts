import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import type { Overview, RemoteState, Row } from '../../core/models/telemetry';
import { TelemetryApiService } from '../../core/services/telemetry-api.service';
import { DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { PanelComponent } from '../../shared/components/panel/panel.component';
import { QueryStateComponent } from '../../shared/components/query-state/query-state.component';
import { MetricGridComponent, type Metric } from './metric-grid.component';

@Component({
  selector: 'app-overview-page',
  standalone: true,
  imports: [DataTableComponent, MetricGridComponent, PanelComponent, QueryStateComponent],
  templateUrl: './overview-page.component.html',
})
export class OverviewPageComponent {
  private readonly api = inject(TelemetryApiService);

  readonly overviewState = toSignal(
    this.api.watch(() => this.api.overview()),
    { initialValue: { kind: 'loading' } as RemoteState<Overview> },
  );
  readonly latestState = toSignal(
    this.api.watch(() => this.api.latest()),
    { initialValue: { kind: 'loading' } as RemoteState<Row[]> },
  );
  readonly metrics = computed<Metric[]>(() => {
    const state = this.overviewState();
    if (state.kind !== 'success') return [];
    const data = state.data;
    return [
      { label: 'Servers', value: `${data.connected_servers}/${data.total_servers}` },
      { label: 'Clients', value: `${data.connected_clients}/${data.total_clients}` },
      { label: 'Processors', value: data.active_processors },
      { label: 'Inserters', value: data.active_inserters },
      { label: 'Events inserted/s', value: Number(data.events_inserted_per_second).toFixed(1) },
      { label: 'Active series', value: data.active_series },
      { label: 'Dead letters', value: data.dead_letter_count },
      { label: 'Quality errors (1h)', value: data.quality_errors },
      {
        label: 'Latest telemetry',
        value: data.latest_telemetry_timestamp
          ? new Date(data.latest_telemetry_timestamp).toLocaleTimeString()
          : '—',
      },
    ];
  });
  readonly latestRows = computed(() => {
    const state = this.latestState();
    return state.kind === 'success' ? state.data : [];
  });
}
