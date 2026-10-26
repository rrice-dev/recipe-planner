import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface Features { passwordReset: boolean; nutrition: boolean; }

// Optional features that depend on server setup (email, USDA key). Hidden until they're switched on.
@Injectable({ providedIn: 'root' })
export class FeaturesService {
  private http = inject(HttpClient);
  readonly features = signal<Features>({ passwordReset: false, nutrition: false });
  private loading: Promise<Features> | null = null;

  ensureLoaded(): Promise<Features> {
    this.loading ??= firstValueFrom(this.http.get<Features>('/api/features'))
      .then(f => { this.features.set(f); return f; })
      .catch(() => this.features());
    return this.loading;
  }
}
