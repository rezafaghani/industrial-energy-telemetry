import { Component, Input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';

@Component({
  selector: 'app-panel',
  standalone: true,
  imports: [MatCardModule],
  template: `
    <mat-card>
      <mat-card-header><mat-card-title>{{ title }}</mat-card-title></mat-card-header>
      <mat-card-content><ng-content /></mat-card-content>
    </mat-card>
  `,
})
export class PanelComponent {
  @Input({ required: true }) title = '';
}
