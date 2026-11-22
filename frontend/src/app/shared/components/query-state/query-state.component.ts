import { Component, Input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import type { RemoteState } from '../../../core/models/telemetry';

@Component({
  selector: 'app-query-state',
  standalone: true,
  imports: [MatCardModule, MatProgressBarModule],
  template: `
    @if (state?.kind === 'loading') {
      <mat-progress-bar mode="indeterminate" aria-label="Loading data" />
    } @else if (state?.kind === 'error') {
      <mat-card class="query-error" role="alert">{{ state.message }}</mat-card>
    }
  `,
})
export class QueryStateComponent {
  @Input() state: RemoteState<unknown> | null = null;
}
