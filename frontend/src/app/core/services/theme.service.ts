import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly isDark = signal(true);

  toggle(): void {
    this.isDark.update(value => !value);
    document.documentElement.classList.toggle('dark-theme', this.isDark());
  }

  initialize(): void {
    document.documentElement.classList.toggle('dark-theme', this.isDark());
  }
}
