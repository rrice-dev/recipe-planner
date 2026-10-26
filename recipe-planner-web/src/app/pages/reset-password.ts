import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

// Opened from the emailed link: /reset-password?email=...&code=...
@Component({
  selector: 'app-reset-password',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="auth card">
      <h1>Choose a new password</h1>
      @if (done()) {
        <p role="status">Your password has been changed.</p>
        <a routerLink="/login" class="button primary">Log in</a>
      } @else if (!code) {
        <p>This page needs the link from your reset email. <a routerLink="/forgot-password">Ask for a new link</a>.</p>
      } @else {
        <p class="muted">For {{ email }}</p>
        <form (ngSubmit)="reset()" class="form">
          <label>New password
            <input type="password" name="password" [(ngModel)]="password" autocomplete="new-password" minlength="12" required />
            <span class="small-text muted">At least 12 characters. A few random words works well.</span>
          </label>
          @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
          <button type="submit" class="primary" [disabled]="busy()">{{ busy() ? 'Saving…' : 'Save new password' }}</button>
        </form>
        @if (expired()) { <p><a routerLink="/forgot-password">Ask for a new link</a></p> }
      }
    </div>
  `
})
export class ResetPasswordPage {
  private http = inject(HttpClient);
  private query = inject(ActivatedRoute).snapshot.queryParamMap;
  email = this.query.get('email') ?? '';
  code = this.query.get('code') ?? '';
  password = '';
  busy = signal(false);
  done = signal(false);
  expired = signal(false);
  error = signal<string | null>(null);

  async reset() {
    if (this.password.length < 12) { this.error.set('Use at least 12 characters.'); return; }
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.http.post('/api/account/reset-password', { email: this.email, code: this.code, newPassword: this.password }));
      this.done.set(true);
    } catch (e) {
      const err = e instanceof HttpErrorResponse ? e : null;
      const problems = err?.error?.errors as Record<string, string[]> | undefined;
      if (problems?.['NewPassword']) this.error.set(problems['NewPassword'].join(' '));
      else if (err?.status === 429) this.error.set('Too many attempts. Wait a few minutes and try again.');
      else { this.error.set('That reset link is invalid or has expired.'); this.expired.set(true); }
    } finally {
      this.busy.set(false);
    }
  }
}
