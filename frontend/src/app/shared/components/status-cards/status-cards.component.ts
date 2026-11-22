import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import type { Row } from '../../../core/models/telemetry';
import { formatCell, statusClass } from '../../../core/utils/presentation';

@Component({
  selector: 'app-status-cards',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatChipsModule],
  template: `
    @if (rows.length === 0) {
      <mat-card class="empty-state">{{ emptyMessage }}</mat-card>
    } @else {
      <div class="status-grid">
        @for (row of rows; track row['id'] ?? row['instance_id'] ?? $index) {
          <mat-card>
            <mat-card-header>
              <mat-card-title>{{ labelFor(row) }}</mat-card-title>
              <mat-chip-set>
                <mat-chip [class]="statusClass(statusFor(row))">{{ statusFor(row) }}</mat-chip>
              </mat-chip-set>
            </mat-card-header>
            <mat-card-content>
              @for (entry of detailEntries(row); track entry[0]) {
                <p class="status-detail">
                  <strong>{{ entry[0].replaceAll('_', ' ') }}:</strong> {{ formatCell(entry[0], entry[1]) }}
                </p>
              }
            </mat-card-content>
          </mat-card>
        }
      </div>
    }
  `,
})
export class StatusCardsComponent {
  @Input() rows: Row[] = [];
  @Input() emptyMessage = 'No records are available.';

  statusFor(row: Row): unknown {
    return row['status'] ?? row['last_status'] ?? row['health'] ?? 'Unknown';
  }

  labelFor(row: Row): string {
    return String(row['display_name'] ?? row['client_key'] ?? row['instance_id'] ?? row['service_name'] ?? 'Instance');
  }

  detailEntries(row: Row): [string, unknown][] {
    return Object.entries(row).filter(([key]) => !['id', 'status', 'last_status', 'health'].includes(key));
  }

  readonly formatCell = formatCell;
  readonly statusClass = statusClass;
}
