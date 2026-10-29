import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../auth.service';
import { isAwaitingApproval } from '../api-errors';
import { FeaturesService } from '../features.service';

@Component({
  selector: 'app-register',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="auth card">
      <h1>Create account</h1>
      @if (pending()) {
        <p role="status">Thanks! Your account is waiting for approval. You'll be able to log in once it's approved.</p>
        @if (features.features().passwordReset) {
          <p class="small-text muted">We've also emailed you a link to confirm your address. Check your spam folder if it doesn't arrive.</p>
        }
        <p><a routerLink="/login">Back to log in</a></p>
      } @else {
      @if (features.features().signUpsNeedApproval) {
        <p class="small-text muted">This is a private site: the owner approves each new account before it can log in.</p>
      }
      <form (ngSubmit)="submit()">
        <label>Email
          <input type="email" name="email" [(ngModel)]="email" autocomplete="username" required />
        </label>
        <label>Password <span class="muted">(at least 12 characters)</span>
          <input type="password" name="password" [(ngModel)]="password" autocomplete="new-password" required />
        </label>
        <label>Confirm password
          <input type="password" name="confirm" [(ngModel)]="confirm" autocomplete="new-password" required />
        </label>
        @if (error()) {
          <p class="error">{{ error() }}</p>
        }
        <button class="primary" type="submit" [disabled]="busy()">
          {{ busy() ? 'Creating…' : 'Create account' }}
        </button>
      </form>
      <p class="muted">Already have an account? <a routerLink="/login">Log in</a></p>
      }
    </div>
  `
})
export class RegisterPage {
  private auth = inject(AuthService);
  private router = inject(Router);
  features = inject(FeaturesService);
  private loadFeatures = this.features.ensureLoaded();
  pending = signal(false);

  email = signal('');
  password = signal('');
  confirm = signal('');
  busy = signal(false);
  error = signal<string | null>(null);

  async submit() {
    this.error.set(null);

    if (this.password().length < 12) {
      this.error.set('Password must be at least 12 characters.');
      return;
    }
    if (this.password() !== this.confirm()) {
      this.error.set('Passwords do not match.');
      return;
    }

    this.busy.set(true);
    try {
      await this.auth.register(this.email().trim(), this.password());
      this.router.navigateByUrl('/recipes');
    } catch (e) {
      if (isAwaitingApproval(e)) {
        // The account was created; it just can't log in until it's approved
        this.pending.set(true);
      } else if (e instanceof HttpErrorResponse && e.status === 429) {
        this.error.set('Too many attempts from your network. Try again later.');
      } else if (e instanceof HttpErrorResponse && e.status === 400 && e.error?.errors) {
        const messages = Object.values(e.error.errors as Record<string, string[]>).flat();
        this.error.set(messages.join(' '));
      } else {
        this.error.set('Could not create the account. Please try again.');
      }
    } finally {
      this.busy.set(false);
    }
  }
}