import { Component, Input } from '@angular/core';
import { BaseChartDirective } from 'ng2-charts';
import type { ChartData, ChartOptions } from 'chart.js';

@Component({
  selector: 'app-series-chart',
  standalone: true,
  imports: [BaseChartDirective],
  template: `
    <div class="chart-card">
      <canvas baseChart [type]="'line'" [data]="data" [options]="chartOptions"></canvas>
    </div>
  `,
})
export class SeriesChartComponent {
  @Input() data: ChartData<'line'> = { labels: [], datasets: [] };
  @Input() unit = '';

  get chartOptions(): ChartOptions<'line'> {
    return {
      responsive: true,
      maintainAspectRatio: false,
      animation: false,
      spanGaps: true,
      scales: {
        y: { title: { display: Boolean(this.unit), text: this.unit } },
      },
    };
  }
}
