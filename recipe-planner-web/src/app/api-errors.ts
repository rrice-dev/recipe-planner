import { HttpErrorResponse } from '@angular/common/http';

/**
 * The API's own explanation of why it refused a request, when it gave one: validation messages
 * ({ errors: { Field: [...] } }), a problem's detail, or a { message }. Otherwise the fallback.
 */
export function apiErrorMessage(e: unknown, fallback: string): string {
  if (!(e instanceof HttpErrorResponse) || !e.error || typeof e.error !== 'object') return fallback;
  const body = e.error as { errors?: Record<string, string[]>; detail?: unknown; message?: unknown };
  if (body.errors && typeof body.errors === 'object') {
    const messages = Object.values(body.errors).flat().filter(m => typeof m === 'string');
    if (messages.length) return messages.join(' ');
  }
  if (typeof body.detail === 'string' && body.detail) return body.detail;
  if (typeof body.message === 'string' && body.message) return body.message;
  return fallback;
}
