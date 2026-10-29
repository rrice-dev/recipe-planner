import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-forgot-password',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="auth card">
      <h1>Reset your password</h1>
      @if (sent()) {
        <p role="status">If there's an account for <strong>{{ email }}</strong>, we've emailed it a link to choose a new password.
          The link works for 1 hour. Check your spam folder if it doesn't arrive in a few minutes.
          We send at most one link every 15 minutes, so if you asked a moment ago, use that one.</p>
        <p><a routerLink="/login">Back to log in</a></p>
      } @else {
        <p class="muted">Enter the email you signed up with and we'll send you a link.</p>
        <form (ngSubmit)="send()" class="form">
          <label>Email <input type="email" name="email" [(ngModel)]="email" autocomplete="username" required /></label>
          @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
          <button type="submit" class="primary" [disabled]="busy() || !email.trim()">{{ busy() ? 'Sending…' : 'Email me a link' }}</button>
        </form>
        <p class="muted"><a routerLink="/login">Back to log in</a></p>
      }
    </div>
  `
})
export class ForgotPasswordPage {
  private http = inject(HttpClient);
  email = '';
  busy = signal(false);
  sent = signal(false);
  error = signal<string | null>(null);

  async send() {
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.http.post('/api/account/forgot-password', { email: this.email.trim() }));
      this.sent.set(true);
    } catch (e) {
      this.error.set(e instanceof HttpErrorResponse && e.status === 429 ? 'Too many attempts. Try again later.'
        : "Couldn't send the email. Please try again.");
    } finally {
      this.busy.set(false);
    }
  }
}
