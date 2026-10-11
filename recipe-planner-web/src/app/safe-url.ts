/**
 * Only allow redirects to paths inside this app.
 * Prevents open-redirect attacks like /login?returnUrl=https://evil.example
 */
export function safeReturnUrl(url: string | null): string {
  if (url && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/\\')) {
    return url;
  }
  return '/recipes';
}