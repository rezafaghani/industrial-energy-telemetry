import { Component, Input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import type { Measurement, Series } from '../../core/models/telemetry';
import { formatCell, statusClass } from '../../core/utils/presentation';

@Component({
  selector: 'app-series-metadata',
  standalone: true,
  imports: [MatCardModule, MatChipsModule],
  template: `
    <mat-card>
      <mat-card-header>
        <mat-card-title>{{ series.display_name }}</mat-card-title>
        <mat-card-subtitle>{{ series.description || 'No description provided.' }}</mat-card-subtitle>
        <mat-chip-set>
          <mat-chip [class]="statusClass(latest ? (latest.quality === 'Good' ? 'Healthy' : 'Degraded') : 'Unknown')">
            {{ latest ? 'Latest: ' + formatCell('source_timestamp', latest.source_timestamp) : 'No samples in range' }}
          </mat-chip>
        </mat-chip-set>
      </mat-card-header>
      <mat-card-content>
        <div class="metadata-grid">
          @for (item of metadata; track item[0]) {
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
  @Input({ required: true }) series!: Series;
  @Input() measurements: Measurement[] = [];

  get latest(): Measurement | undefined {
    return this.measurements[0];
  }

  get metadata(): [string, string | number | boolean | null][] {
    return [
      ['Series key', this.series.series_key],
      ['Measurement', this.series.measurement_id],
      ['Site', `${this.series.site_name} (${this.series.site_key})`],
      ['Asset', `${this.series.asset_name} (${this.series.asset_key})`],
      ['Data type', this.series.data_type],
      ['Measurement type', this.series.measurement_type],
      ['Unit', this.series.unit || 'Discrete'],
      ['OPC UA server', this.series.server_key],
      ['Endpoint', this.series.endpoint],
      ['Namespace URI', this.series.namespace_uri],
      ['Node ID', this.series.node_id],
      ['Active', this.series.active],
      ['Tags', this.series.tags],
      ['Created', this.series.created_at],
    ];
  }

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
