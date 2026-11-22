import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatOptionModule } from '@angular/material/core';
import { MatSelectModule } from '@angular/material/select';
import type { ChartData } from 'chart.js';
import { catchError, forkJoin, map, of, switchMap, timer } from 'rxjs';
import type { Measurement, RemoteState, Row, Series } from '../../core/models/telemetry';
import { TelemetryApiService } from '../../core/services/telemetry-api.service';
import { errorMessage, measurementRow } from '../../core/utils/presentation';
import { DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { PanelComponent } from '../../shared/components/panel/panel.component';
import { QueryStateComponent } from '../../shared/components/query-state/query-state.component';
import { SeriesChartComponent } from './series-chart.component';
import { SeriesMetadataComponent } from './series-metadata.component';

interface SeriesHistory {
  series: Series;
  measurements: Measurement[];
}

const chartColors = ['#36d399', '#60a5fa', '#f59e0b', '#f472b6'];

@Component({
  selector: 'app-series-explorer-page',
  standalone: true,
  imports: [
    DataTableComponent,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatOptionModule,
    MatSelectModule,
    PanelComponent,
    QueryStateComponent,
    SeriesChartComponent,
    SeriesMetadataComponent,
  ],
  templateUrl: './series-explorer-page.component.html',
})
export class SeriesExplorerPageComponent {
  private readonly api = inject(TelemetryApiService);

  readonly search = signal('');
  readonly selectedIds = signal<string[]>([]);
  readonly hours = signal(1);
  readonly seriesState = toSignal(this.api.watch(() => this.api.series(), 0), {
    initialValue: { kind: 'loading' } as RemoteState<Series[]>,
  });
  readonly allSeries = computed(() => {
    const state = this.seriesState();
    return state.kind === 'success' ? state.data : [];
  });
  readonly filteredSeries = computed(() => {
    const term = this.search().trim().toLocaleLowerCase();
    return term
      ? this.allSeries().filter(item =>
        `${item.series_key} ${item.display_name} ${item.site_name} ${item.asset_name}`
          .toLocaleLowerCase().includes(term))
      : this.allSeries();
  });
  readonly chosen = computed(() =>
    this.selectedIds().map(id => this.allSeries().find(item => item.id === id))
      .filter((item): item is Series => item !== undefined),
  );
  readonly selectedUnit = computed(() => this.chosen()[0]?.unit);
  readonly historyState = signal<RemoteState<SeriesHistory[]>>({ kind: 'success', data: [] });
  readonly historyRows = computed<Row[]>(() => {
    const state = this.historyState();
    if (state.kind !== 'success') return [];
    return state.data.flatMap(entry =>
      entry.measurements.map(measurement => measurementRow(entry.series, measurement)));
  });
  readonly numeric = computed(() =>
    this.chosen().length > 0 && this.chosen().every(item => item.data_type === 'Double'),
  );
  readonly chartData = computed<ChartData<'line'>>(() => {
    const state = this.historyState();
    const chosen = this.chosen();
    if (state.kind !== 'success') return { labels: [], datasets: [] };

    const points = new Map<string, { time: string; values: (number | null)[] }>();
    state.data.forEach((entry, seriesIndex) => {
      for (const measurement of entry.measurements) {
        if (measurement.numeric_value === null) continue;
        const timestamp = measurement.source_timestamp;
        const point = points.get(timestamp) ?? {
          time: new Date(timestamp).toLocaleTimeString(),
          values: chosen.map(() => null),
        };
        point.values[seriesIndex] = measurement.numeric_value;
        points.set(timestamp, point);
      }
    });
    const orderedPoints = [...points.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([, point]) => point);
    return {
      labels: orderedPoints.map(point => point.time),
      datasets: chosen.map((item, index) => ({
        label: item.display_name,
        data: orderedPoints.map(point => point.values[index]),
        borderColor: chartColors[index % chartColors.length],
        pointRadius: 0,
        tension: 0.25,
      })),
    };
  });

  constructor() {
    effect(onCleanup => {
      const selected = this.chosen();
      const range = this.hours();
      if (selected.length === 0) {
        this.historyState.set({ kind: 'success', data: [] });
        return;
      }

      this.historyState.set({ kind: 'loading' });
      const subscription = timer(0, 10000).pipe(
        switchMap(() => forkJoin(selected.map(series => this.api.history(series.id, range))).pipe(
          map(measurements => ({
            kind: 'success',
            data: selected.map((series, index) => ({ series, measurements: measurements[index] })),
          }) as RemoteState<SeriesHistory[]>),
          catchError(error => of({ kind: 'error', message: errorMessage(error) } as RemoteState<SeriesHistory[]>)),
        )),
      ).subscribe(state => this.historyState.set(state));
      onCleanup(() => subscription.unsubscribe());
    });
  }

  onSearchChange(value: string): void {
    this.search.set(value);
  }

  onSelectionChange(ids: string[]): void {
    const selected = ids.slice(0, 4);
    if (selected.length > 0) {
      const units = new Set(selected.map(id => this.allSeries().find(item => item.id === id)?.unit));
      if (units.size > 1) return;
    }
    this.selectedIds.set(selected);
  }

  historyFor(index: number): Measurement[] {
    const state = this.historyState();
    return state.kind === 'success' ? state.data[index]?.measurements ?? [] : [];
  }
}
