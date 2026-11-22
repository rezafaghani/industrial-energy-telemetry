import { Component, Input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';

export interface Metric {
  label: string;
  value: string | number;
}

@Component({
  selector: 'app-metric-grid',
  standalone: true,
  imports: [MatCardModule],
  template: `
    <div class="metric-grid">
      @for (metric of metrics; track metric.label) {
        <mat-card>
          <mat-card-content>
            <div class="metric-label">{{ metric.label }}</div>
            <div class="metric-value">{{ metric.value }}</div>
          </mat-card-content>
        </mat-card>
      }
    </div>
  `,
})
export class MetricGridComponent {
  @Input() metrics: Metric[] = [];
}
