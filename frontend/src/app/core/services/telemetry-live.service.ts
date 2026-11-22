import { Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { Subject } from 'rxjs';

export type LiveConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

@Injectable({ providedIn: 'root' })
export class TelemetryLiveService {
  private readonly updatesSubject = new Subject<void>();
  private connection?: HubConnection;
  private startRequested = false;

  readonly updates$ = this.updatesSubject.asObservable();
  readonly status = signal<LiveConnectionStatus>('connecting');

  async start(): Promise<void> {
    if (this.startRequested) return;
    this.startRequested = true;
    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/telemetry')
      .withAutomaticReconnect()
      .build();

    this.connection.on('telemetry', () => this.updatesSubject.next());
    this.connection.onreconnecting(() => this.status.set('reconnecting'));
    this.connection.onreconnected(() => this.status.set('connected'));
    this.connection.onclose(() => this.status.set('disconnected'));

    try {
      await this.connection.start();
      this.status.set('connected');
    } catch (error) {
      this.status.set('disconnected');
      console.error('Telemetry live connection failed.', error);
    }
  }
}
