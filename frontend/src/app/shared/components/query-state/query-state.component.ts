import { Component, input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import type { RemoteState } from '../../../core/models/telemetry';

@Component({
  selector: 'app-query-state',
  imports: [MatCardModule, MatProgressBarModule],
  template: `
    @let currentState = state();
    @if (currentState?.kind === 'loading') {
      <mat-progress-bar mode="indeterminate" aria-label="Loading data" />
    } @else if (currentState?.kind === 'error') {
      <mat-card class="query-error" role="alert">{{ currentState.message }}</mat-card>
    }
  `,
})
export class QueryStateComponent {
  readonly state = input<RemoteState<unknown> | null>(null);
}
