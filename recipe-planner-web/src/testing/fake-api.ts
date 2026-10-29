import { HttpTestingController } from '@angular/common/http/testing';

// A stand-in for the API in page tests. Pages make several requests one after another (each waits for the
// last), so this keeps answering whatever is pending until the page goes quiet, and records every call.

export interface Call { method: string; url: string; body: any }
export interface Reply { status?: number; body?: unknown }
export type Routes = (method: string, url: string, body: any) => Reply | undefined;

export const ok = (body?: unknown): Reply => ({ status: 200, body });
export const fail = (status: number, body?: unknown): Reply => ({ status, body });

/** Answers every request the page makes until it has made none for a few rounds. Unknown requests fail the test. */
export async function answer(http: HttpTestingController, routes: Routes): Promise<Call[]> {
  const calls: Call[] = [];
  let quiet = 0;
  while (quiet < 5) {
    await new Promise(resolve => setTimeout(resolve));
    const pending = http.match(() => true);
    if (!pending.length) { quiet++; continue; }
    quiet = 0;
    for (const req of pending) {
      const { method, urlWithParams: url, body } = req.request;
      calls.push({ method, url, body });
      const reply = routes(method, url, body);
      if (!reply) throw new Error(`The page made a request this test doesn't expect: ${method} ${url}`);
      const status = reply.status ?? 200;
      if (status >= 400) req.flush(reply.body ?? null, { status, statusText: 'Error' });
      else req.flush(reply.body ?? null, { status, statusText: 'OK' });
    }
  }
  return calls;
}

/** Sets a text box's value the way typing does, so Angular's form bindings see it */
export function type(input: HTMLInputElement | HTMLTextAreaElement, value: string) {
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

/** Picks an option in a select by its visible text */
export function choose(select: HTMLSelectElement, text: string) {
  const index = [...select.options].findIndex(o => o.textContent!.trim() === text);
  if (index < 0) throw new Error(`No option "${text}"`);
  select.selectedIndex = index;
  select.dispatchEvent(new Event('change'));
}
