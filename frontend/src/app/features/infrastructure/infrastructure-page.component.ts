import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import type { Observable } from 'rxjs';
import type { RemoteState, Row } from '../../core/models/telemetry';
import { TelemetryApiService } from '../../core/services/telemetry-api.service';
import { DataTableComponent } from '../../shared/components/data-table/data-table.component';
import { PanelComponent } from '../../shared/components/panel/panel.component';
import { QueryStateComponent } from '../../shared/components/query-state/query-state.component';
import { StatusCardsComponent } from '../../shared/components/status-cards/status-cards.component';
import { ServerRegistrationComponent } from './server-registration.component';

@Component({
  selector: 'app-infrastructure-page',
  imports: [DataTableComponent, PanelComponent, QueryStateComponent, ServerRegistrationComponent, StatusCardsComponent],
  templateUrl: './infrastructure-page.component.html',
})
export class InfrastructurePageComponent {
  private readonly api = inject(TelemetryApiService);

  readonly servers = this.watch(() => this.api.servers());
  readonly clients = this.watch(() => this.api.clients());
  readonly collectors = this.watch(() => this.api.collectors());
  readonly assignments = this.watch(() => this.api.assignments());

  private watch(request: () => Observable<Row[]>) {
    return toSignal(this.api.watch(request), { initialValue: { kind: 'loading' } as RemoteState<Row[]> });
  }
}
