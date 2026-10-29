import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from './auth.service';
import { FeaturesService } from './features.service';

// AI features need a confirmed email (the API answers 403 "email_not_confirmed" otherwise).
// Confirming only exists when the site can send email, so without it nothing here shows.

export const CONFIRM_EMAIL_MESSAGE = 'Confirm your email to use AI features. Use the link we emailed you, or send a new one.';

/** True when an API error means "confirm your email first" */
export function needsConfirmedEmail(e: unknown): boolean {
  return e instanceof HttpErrorResponse && e.status === 403 && e.error?.title === 'email_not_confirmed';
}

/** Shown on pages with AI features, to signed-in users who haven't confirmed their email yet */
@Component({
  selector: 'app-confirm-email-notice',
  template: `
    @if (show()) {
      <div class="callout small-text" role="note">
        <strong>Confirm your email to use the AI features.</strong>
        It only takes the link we email to {{ auth.email() }}.
        @if (sent()) {
          <span role="status">A new link is on its way. Check your spam folder too.</span>
        } @else {
          <button type="button" class="link" (click)="resend()" [disabled]="sending()">
            {{ sending() ? 'Sending…' : 'Send a new link' }}
          </button>
        }
        @if (failed()) { <span class="error" role="alert">Couldn't send it right now. Try again in a few minutes.</span> }
      </div>
    }
  `
})
export class ConfirmEmailNotice {
  auth = inject(AuthService);
  private features = inject(FeaturesService);
  sending = signal(false);
  sent = signal(false);
  failed = signal(false);

  constructor() {
    void this.features.ensureLoaded();
  }

  show = () => this.features.features().passwordReset && this.auth.isLoggedIn() && !this.auth.emailConfirmed();

  async resend() {
    this.sending.set(true);
    this.failed.set(false);
    try {
      await this.auth.resendConfirmation();
      this.sent.set(true);
    } catch {
      this.failed.set(true);
    } finally {
      this.sending.set(false);
    }
  }
}
