import { Component, computed, input } from '@angular/core';
import { BaseChartDirective } from 'ng2-charts';
import type { ChartData, ChartOptions } from 'chart.js';

@Component({
  selector: 'app-series-chart',
  imports: [BaseChartDirective],
  template: `
    <div class="chart-card">
      <canvas baseChart [type]="'line'" [data]="data()" [options]="chartOptions()"></canvas>
    </div>
  `,
})
export class SeriesChartComponent {
  readonly data = input<ChartData<'line'>>({ labels: [], datasets: [] });
  readonly unit = input('');

  protected readonly chartOptions = computed<ChartOptions<'line'>>(() => ({
    responsive: true,
    maintainAspectRatio: false,
    animation: false,
    spanGaps: true,
    scales: {
      y: { title: { display: Boolean(this.unit()), text: this.unit() } },
    },
  }));
}
