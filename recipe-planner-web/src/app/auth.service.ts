import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { clearCachedPersonalData } from './offline.service';

interface UserInfo {
  email: string;
  isEmailConfirmed: boolean;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private loading: Promise<void> | null = null;

  readonly email = signal<string | null>(null);
  /** False until the user follows the link in their confirmation email (AI features need it) */
  readonly emailConfirmed = signal(false);
  readonly isLoggedIn = computed(() => this.email() !== null);

  /** Checks once whether a session cookie is already valid. */
  ensureLoaded(): Promise<void> {
    this.loading ??= this.refresh();
    return this.loading;
  }

  async refresh(): Promise<void> {
    try {
      const info = await firstValueFrom(this.http.get<UserInfo>('/api/auth/manage/info'));
      this.email.set(info.email);
      this.emailConfirmed.set(info.isEmailConfirmed);
    } catch {
      this.email.set(null);
      this.emailConfirmed.set(false);
    }
  }

  async login(email: string, password: string) {
    await firstValueFrom(
      this.http.post('/api/auth/login?useCookies=true', { email, password }, { responseType: 'text' })
    );
    await this.refresh();
    this.loading = Promise.resolve();
  }

  async register(email: string, password: string) {
    await firstValueFrom(
      this.http.post('/api/auth/register', { email, password }, { responseType: 'text' })
    );
    await this.login(email, password);
  }

  async logout() {
    try {
      await firstValueFrom(this.http.post('/api/auth/logout', null));
    } finally {
      this.clear();
    }
  }

  /** Emails a fresh confirmation link to the signed-in user's address */
  async resendConfirmation() {
    const email = this.email();
    if (email) await firstValueFrom(this.http.post('/api/auth/resendConfirmationEmail', { email }));
  }

  clear() {
    this.email.set(null);
    this.emailConfirmed.set(false);
    this.loading = Promise.resolve();
    void clearCachedPersonalData();
  }
}