import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { catchError, map, merge, of, shareReplay, startWith, Subject, switchMap, timer, type Observable } from 'rxjs';
import { errorMessage } from '../utils/presentation';
import type { Measurement, Overview, RemoteState, Row, Series, ServerRegistration } from '../models/telemetry';
import { TelemetryLiveService } from './telemetry-live.service';

@Injectable({ providedIn: 'root' })
export class TelemetryApiService {
  private readonly http = inject(HttpClient);
  private readonly live = inject(TelemetryLiveService);
  private readonly manualRefresh = new Subject<void>();

  watch<T>(request: () => Observable<T>, pollingMilliseconds = 5000): Observable<RemoteState<T>> {
    const timer$ = pollingMilliseconds > 0 ? timer(0, pollingMilliseconds) : of(undefined);
    return merge(timer$, this.live.updates$, this.manualRefresh).pipe(
      switchMap(() => request().pipe(
        map(data => ({ kind: 'success', data }) as RemoteState<T>),
        catchError(error => of({ kind: 'error', message: errorMessage(error) } as RemoteState<T>)),
      )),
      startWith({ kind: 'loading' } as RemoteState<T>),
      shareReplay({ bufferSize: 1, refCount: true }),
    );
  }

  refresh(): void {
    this.manualRefresh.next();
  }

  overview(): Observable<Overview> {
    return this.http.get<Overview>('/api/overview');
  }

  servers(): Observable<Row[]> {
    return this.http.get<Row[]>('/api/servers');
  }

  registerServer(server: ServerRegistration): Observable<void> {
    return this.http.post<void>('/api/servers', server);
  }

  clients(): Observable<Row[]> {
    return this.http.get<Row[]>('/api/clients');
  }

  services(): Observable<Row[]> {
    return this.http.get<Row[]>('/api/services');
  }

  collectors(): Observable<Row[]> {
    return this.http.get<Row[]>('/api/control-plane/collectors');
  }

  assignments(): Observable<Row[]> {
    return this.http.get<Row[]>('/api/control-plane/assignments');
  }

  latest(): Observable<Row[]> {
    return this.http.get<Row[]>('/api/telemetry/latest');
  }

  series(search = ''): Observable<Series[]> {
    const query = new URLSearchParams({ search, pageSize: '200' });
    return this.http.get<Series[]>(`/api/series?${query}`);
  }

  history(id: string, hours: number): Observable<Measurement[]> {
    const to = new Date();
    const from = new Date(to.getTime() - hours * 60 * 60 * 1000);
    const query = new URLSearchParams({ from: from.toISOString(), to: to.toISOString(), limit: '1000' });
    return this.http.get<Measurement[]>(`/api/series/${encodeURIComponent(id)}/history?${query}`);
  }
}
