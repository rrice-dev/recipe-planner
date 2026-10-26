import { Injectable, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './auth.service';

export type Theme = 'system' | 'light' | 'dark';
export type Diet = 'AIP' | 'Vegetarian' | null;
export type Units = 'us' | 'metric';

export interface Settings {
  theme: Theme;
  diet: Diet;
  defaultServings: number;
  units: Units;
}

export const DEFAULT_SETTINGS: Settings = { theme: 'system', diet: null, defaultServings: 4, units: 'us' };

// The theme is also remembered in this browser, so the page opens in the right colors
// before the settings request finishes (and on the login page). index.html reads it first.
const THEME_KEY = 'rp-theme';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private http = inject(HttpClient);
  private auth = inject(AuthService);

  readonly settings = signal<Settings>({ ...DEFAULT_SETTINGS, theme: readStoredTheme() });

  constructor() {
    // Load your saved settings whenever you sign in; fall back to defaults when you sign out
    effect(() => {
      this.loading = null;
      if (this.auth.isLoggedIn()) void this.ensureLoaded();
      else this.settings.update(s => ({ ...DEFAULT_SETTINGS, theme: s.theme }));
    });
    effect(() => applyTheme(this.settings().theme));
  }

  private loading: Promise<Settings> | null = null;

  /** Your saved settings, loaded once per sign-in. Pages that start from a setting await this. */
  ensureLoaded(): Promise<Settings> {
    this.loading ??= this.load();
    return this.loading;
  }

  async load(): Promise<Settings> {
    try {
      await this.auth.ensureLoaded();
      if (this.auth.isLoggedIn()) this.settings.set(await firstValueFrom(this.http.get<Settings>('/api/settings')));
    } catch {
      // Keep what we have; the page still works with defaults
    }
    return this.settings();
  }

  async save(next: Settings) {
    const saved = await firstValueFrom(this.http.put<Settings>('/api/settings', next));
    this.settings.set(saved);
    return saved;
  }

  /** Try a theme right away, before saving */
  preview(theme: Theme) {
    this.settings.update(s => ({ ...s, theme }));
  }
}

function readStoredTheme(): Theme {
  try {
    const t = localStorage.getItem(THEME_KEY);
    return t === 'light' || t === 'dark' ? t : 'system';
  } catch {
    return 'system';
  }
}

export function applyTheme(theme: Theme) {
  const root = document.documentElement;
  if (theme === 'system') root.removeAttribute('data-theme');
  else root.setAttribute('data-theme', theme);
  try {
    if (theme === 'system') localStorage.removeItem(THEME_KEY);
    else localStorage.setItem(THEME_KEY, theme);
  } catch {
    // Private browsing can block storage; the theme still applies for this visit
  }
}
