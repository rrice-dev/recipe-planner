import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../auth.service';
import { safeReturnUrl } from '../safe-url';

@Component({
  selector: 'app-login',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="auth card">
      <h1>Log in</h1>
      @if (deleted) { <p class="list-note" role="status">Your account and everything in it has been deleted.</p> }
      <form (ngSubmit)="submit()">
        <label>Email
          <input type="email" name="email" [(ngModel)]="email" autocomplete="username" required />
        </label>
        <label>Password
          <input type="password" name="password" [(ngModel)]="password" autocomplete="current-password" required />
        </label>
        @if (error()) {
          <p class="error">{{ error() }}</p>
        }
        <button class="primary" type="submit" [disabled]="busy()">
          {{ busy() ? 'Logging in…' : 'Log in' }}
        </button>
      </form>
      <p class="muted">No account? <a routerLink="/register">Create one</a></p>
    </div>
  `
})
export class LoginPage {
  deleted = inject(ActivatedRoute).snapshot.queryParamMap.has('deleted');
  private auth = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  email = signal('');
  password = signal('');
  busy = signal(false);
  error = signal<string | null>(null);

  async submit() {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.login(this.email().trim(), this.password());
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
      this.router.navigateByUrl(safeReturnUrl(returnUrl));
    } catch {
      // Same message for wrong email, wrong password, or locked account,
      // so attackers can't learn which emails have accounts
      this.error.set('Invalid email or password.');
    } finally {
      this.busy.set(false);
    }
  }
}