import { Component, OnInit, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TelemetryLiveService } from './core/services/telemetry-live.service';
import { ThemeService } from './core/services/theme.service';

@Component({
  selector: 'app-root',
  imports: [MatButtonModule, MatChipsModule, MatToolbarModule, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './app-shell.html',
})
export class App implements OnInit {
  readonly live = inject(TelemetryLiveService);
  readonly theme = inject(ThemeService);

  ngOnInit(): void {
    this.theme.initialize();
    void this.live.start();
  }
}
