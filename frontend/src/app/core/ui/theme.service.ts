import { Injectable, signal } from '@angular/core';

export type AppTheme = 'dark' | 'blue' | 'white';

const STORAGE_KEY = 'oa_theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal<AppTheme>(this.readInitial());

  readonly options: { id: AppTheme; label: string; swatch: string }[] = [
    { id: 'dark', label: 'Dark', swatch: '#0b1118' },
    { id: 'blue', label: 'Blue', swatch: '#152240' },
    { id: 'white', label: 'White', swatch: '#f4f6f9' }
  ];

  constructor() {
    this.apply(this.theme());
  }

  setTheme(theme: AppTheme): void {
    this.theme.set(theme);
    this.apply(theme);
    try {
      localStorage.setItem(STORAGE_KEY, theme);
    } catch {
      /* ignore */
    }
  }

  private readInitial(): AppTheme {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (stored === 'dark' || stored === 'blue' || stored === 'white') {
        return stored;
      }
    } catch {
      /* ignore */
    }
    return 'dark';
  }

  private apply(theme: AppTheme): void {
    if (typeof document === 'undefined') return;
    document.documentElement.setAttribute('data-theme', theme);
  }
}
