import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth.service';

const strictEncoder = {
  encodeKey: encodeURIComponent, encodeValue: encodeURIComponent,
  decodeKey: decodeURIComponent, decodeValue: decodeURIComponent
};

// Opened from the emailed link: /confirm-email?userId=...&code=...(&changedEmail=...)
// Hands the same values to Identity's own confirm endpoint, which checks the code.
@Component({
  selector: 'app-confirm-email',
  imports: [RouterLink],
  template: `
    <div class="auth card">
      <h1>Confirm your email</h1>
      @switch (state()) {
        @case ('working') { <p role="status">Confirming…</p> }
        @case ('done') {
          <p role="status">Your email is confirmed. The AI features are ready to use.</p>
          <a [routerLink]="auth.isLoggedIn() ? '/fridge' : '/login'" class="button primary">
            {{ auth.isLoggedIn() ? 'What can I make?' : 'Log in' }}
          </a>
        }
        @case ('failed') {
          <p role="alert">That link is invalid or has expired.</p>
          <p class="muted">Log in and use "Send a new link" on the fridge or import page to get a fresh one.</p>
          <a routerLink="/fridge" class="button">Go to the fridge page</a>
        }
      }
    </div>
  `
})
export class ConfirmEmailPage implements OnInit {
  private http = inject(HttpClient);
  private query = inject(ActivatedRoute).snapshot.queryParamMap;
  auth = inject(AuthService);
  state = signal<'working' | 'done' | 'failed'>('working');

  async ngOnInit() {
    const userId = this.query.get('userId');
    const code = this.query.get('code');
    if (!userId || !code) { this.state.set('failed'); return; }

    // Strict encoding: Angular's default leaves "+" as is, which the server would read as a space
    // (and a changed address like name+recipes@gmail.com would then not match)
    let params = new HttpParams({ encoder: strictEncoder }).set('userId', userId).set('code', code);
    const changedEmail = this.query.get('changedEmail');
    if (changedEmail) params = params.set('changedEmail', changedEmail);

    try {
      await firstValueFrom(this.http.get('/api/auth/confirmEmail', { params, responseType: 'text' }));
      this.state.set('done');
      await this.auth.refresh(); // so pages stop asking for confirmation
    } catch {
      this.state.set('failed');
    }
  }
}
