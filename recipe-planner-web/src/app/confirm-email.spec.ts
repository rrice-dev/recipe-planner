import { HttpErrorResponse } from '@angular/common/http';
import { needsConfirmedEmail } from './confirm-email';

describe('needsConfirmedEmail', () => {
  it('spots the API asking for a confirmed email', () => {
    const e = new HttpErrorResponse({ status: 403, error: { title: 'email_not_confirmed' } });
    expect(needsConfirmedEmail(e)).toBe(true);
  });

  it('leaves other errors alone', () => {
    expect(needsConfirmedEmail(new HttpErrorResponse({ status: 403, error: { title: 'Forbidden' } }))).toBe(false);
    expect(needsConfirmedEmail(new HttpErrorResponse({ status: 429, error: { title: 'email_not_confirmed' } }))).toBe(false);
    expect(needsConfirmedEmail(new HttpErrorResponse({ status: 403 }))).toBe(false);
    expect(needsConfirmedEmail(new Error('boom'))).toBe(false);
  });
});
