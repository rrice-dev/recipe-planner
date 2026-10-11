import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../auth.service';

@Component({
  selector: 'app-register',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="auth card">
      <h1>Create account</h1>
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
    </div>
  `
})
export class RegisterPage {
  private auth = inject(AuthService);
  private router = inject(Router);

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
      if (e instanceof HttpErrorResponse && e.status === 400 && e.error?.errors) {
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