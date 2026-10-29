import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../auth.service';
import { isAwaitingApproval } from '../api-errors';
import { FeaturesService } from '../features.service';
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
      @if (features.features().passwordReset) {
        <p class="small-text"><a routerLink="/forgot-password">Forgot your password?</a></p>
      }
      <p class="muted">No account? <a routerLink="/register">Create one</a></p>
    </div>
  `
})
export class LoginPage {
  features = inject(FeaturesService);
  private loadFeatures = this.features.ensureLoaded();
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
    } catch (e) {
      // Same message for wrong email, wrong password, or locked account,
      // so attackers can't learn which emails have accounts. Too many attempts is the rate limit, which
      // applies to everyone alike, so saying so reveals nothing and stops people retyping a right password.
      // "NotAllowed" only comes back after a correct password, for an account nobody has approved yet
      this.error.set(e instanceof HttpErrorResponse && e.status === 429
        ? 'Too many attempts. Wait a few minutes and try again.'
        : isAwaitingApproval(e)
        ? "Your account is waiting for approval. You'll be able to log in once it's approved."
        : 'Invalid email or password.');
    } finally {
      this.busy.set(false);
    }
  }
}