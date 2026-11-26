import { Component, computed, input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import type { Measurement, Series } from '../../core/models/telemetry';
import { formatCell, statusClass } from '../../core/utils/presentation';

@Component({
  selector: 'app-series-metadata',
  imports: [MatCardModule, MatChipsModule],
  template: `
    @let latest = latestMeasurement();
    <mat-card>
      <mat-card-header>
        <mat-card-title>{{ series().display_name }}</mat-card-title>
        <mat-card-subtitle>{{ series().description || 'No description provided.' }}</mat-card-subtitle>
        <mat-chip-set>
          <mat-chip [class]="statusClass(latest ? (latest.quality === 'Good' ? 'Healthy' : 'Degraded') : 'Unknown')">
            {{ latest ? 'Latest: ' + formatCell('source_timestamp', latest.source_timestamp) : 'No samples in range' }}
          </mat-chip>
        </mat-chip-set>
      </mat-card-header>
      <mat-card-content>
        <div class="metadata-grid">
          @for (item of metadata(); track item[0]) {
            <div class="metadata-item">
              <span class="metadata-label">{{ item[0] }}</span>
              <span class="metadata-value">{{ displayValue(item[0], item[1]) }}</span>
            </div>
          }
        </div>
      </mat-card-content>
    </mat-card>
  `,
})
export class SeriesMetadataComponent {
  readonly series = input.required<Series>();
  readonly measurements = input<Measurement[]>([]);

  protected readonly latestMeasurement = computed(() => this.measurements()[0]);

  protected readonly metadata = computed<[string, string | number | boolean | null][]>(() => {
    const series = this.series();
    return [
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
  });

  displayValue(label: string, value: unknown): string {
    if (label === 'Tags' && typeof value === 'string') {
      try {
        return JSON.stringify(JSON.parse(value), null, 2);
      } catch {
        return value;
      }
    }
    return formatCell(label, value);
  }

  readonly formatCell = formatCell;
  readonly statusClass = statusClass;
}
