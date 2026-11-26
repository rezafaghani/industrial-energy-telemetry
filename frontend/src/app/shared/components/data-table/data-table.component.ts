import { Component, computed, input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import type { Row } from '../../../core/models/telemetry';
import { formatCell } from '../../../core/utils/presentation';

@Component({
  selector: 'app-data-table',
  imports: [MatCardModule, MatTableModule],
  template: `
    @if (rows().length === 0) {
      <mat-card class="empty-state">{{ emptyMessage() }}</mat-card>
    } @else {
      <div class="table-wrap">
        <table mat-table [dataSource]="rows()">
          @for (column of columns(); track column) {
            <ng-container [matColumnDef]="column">
              <th mat-header-cell *matHeaderCellDef>{{ column.replaceAll('_', ' ') }}</th>
              <td mat-cell *matCellDef="let row">{{ formatCell(column, row[column]) }}</td>
            </ng-container>
          }
          <tr mat-header-row *matHeaderRowDef="columns()"></tr>
          <tr mat-row *matRowDef="let row; columns: columns()"></tr>
        </table>
      </div>
    }
  `,
})
export class DataTableComponent {
  readonly emptyMessage = input('No data is available.');
  readonly rows = input([], { transform: (value: Row[] | null | undefined) => value ?? [] });

  protected readonly columns = computed(() => Object.keys(this.rows()[0] ?? {}));
  protected readonly formatCell = formatCell;
}
