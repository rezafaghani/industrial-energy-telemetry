import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TelemetryApiService } from '../../core/services/telemetry-api.service';
import type { ServerRegistration } from '../../core/models/telemetry';
import { errorMessage } from '../../core/utils/presentation';

type ServerForm = Omit<ServerRegistration, 'measurements'> & { measurements: string };

const emptyServer: ServerForm = {
  serverKey: '',
  displayName: '',
  endpoint: '',
  namespaceUri: '',
  siteKey: '',
  siteName: '',
  assetKey: '',
  assetName: '',
  measurements: '',
};

@Component({
  selector: 'app-server-registration',
  standalone: true,
  imports: [FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatProgressBarModule],
  templateUrl: './server-registration.component.html',
})
export class ServerRegistrationComponent {
  private readonly api = inject(TelemetryApiService);

  server: ServerForm = { ...emptyServer };
  readonly pending = signal(false);
  readonly error = signal<string | null>(null);
  readonly fields: { key: keyof ServerForm; label: string }[] = [
    { key: 'serverKey', label: 'Server key' },
    { key: 'displayName', label: 'Display name' },
    { key: 'endpoint', label: 'Endpoint' },
    { key: 'namespaceUri', label: 'Namespace URI' },
    { key: 'siteKey', label: 'Site key' },
    { key: 'siteName', label: 'Site name' },
    { key: 'assetKey', label: 'Asset key' },
    { key: 'assetName', label: 'Asset name' },
    { key: 'measurements', label: 'Numeric measurements (comma separated)' },
  ];

  submit(): void {
    this.error.set(null);
    this.pending.set(true);
    const request: ServerRegistration = {
      ...this.server,
      measurements: this.server.measurements.split(',').map(value => value.trim()).filter(Boolean),
    };

    this.api.registerServer(request).subscribe({
      next: () => {
        this.server = { ...emptyServer };
        this.pending.set(false);
        this.api.refresh();
      },
      error: error => {
        this.error.set(errorMessage(error));
        this.pending.set(false);
      },
    });
  }
}
