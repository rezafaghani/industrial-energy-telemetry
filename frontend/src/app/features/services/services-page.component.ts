import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import type { RemoteState, Row } from '../../core/models/telemetry';
import { TelemetryApiService } from '../../core/services/telemetry-api.service';
import { PanelComponent } from '../../shared/components/panel/panel.component';
import { QueryStateComponent } from '../../shared/components/query-state/query-state.component';
import { StatusCardsComponent } from '../../shared/components/status-cards/status-cards.component';

@Component({
  selector: 'app-services-page',
  standalone: true,
  imports: [PanelComponent, QueryStateComponent, StatusCardsComponent],
  templateUrl: './services-page.component.html',
})
export class ServicesPageComponent {
  private readonly api = inject(TelemetryApiService);
  readonly state = toSignal(this.api.watch(() => this.api.services()), {
    initialValue: { kind: 'loading' } as RemoteState<Row[]>,
  });
}
