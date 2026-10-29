import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AccountApprovals } from './account-approvals';
import { answer, fail, ok } from '../testing/fake-api';

describe('AccountApprovals', () => {
  let http: HttpTestingController;
  const users = [
    { id: 'u2', email: 'mom@example.com', emailConfirmed: true, isApproved: false },
    { id: 'u1', email: 'me@example.com', emailConfirmed: true, isApproved: true }
  ];

  async function setup(reply = ok(users)) {
    TestBed.configureTestingModule({ imports: [AccountApprovals], providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AccountApprovals);
    fixture.detectChanges();
    await answer(http, (m, url) => m === 'GET' && url === '/api/admin/users' ? reply : undefined);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  const button = (el: HTMLElement, text: string) => [...el.querySelectorAll('button')].find(b => b.textContent!.includes(text))!;

  afterEach(() => http.verify());

  it('shows nothing to someone who is not an admin', async () => {
    const { el } = await setup(fail(403));
    expect(el.textContent!.trim()).toBe('');
  });

  it('lists accounts waiting for approval, and approves one', async () => {
    const { fixture, el } = await setup();
    expect(el.textContent).toContain('mom@example.com');
    expect(el.textContent).toContain('1 approved account');

    button(el, 'Approve').click();
    const calls = await answer(http, (m, url) =>
      m === 'POST' && url === '/api/admin/users/u2/approve' ? ok() :
      m === 'GET' && url === '/api/admin/users' ? ok(users.map(u => ({ ...u, isApproved: true }))) : undefined);
    fixture.detectChanges();

    expect(calls[0].url).toBe('/api/admin/users/u2/approve');
    expect(el.textContent).toContain('No accounts are waiting');
  });

  it('declines (deletes) a waiting account only after you confirm', async () => {
    const { fixture, el } = await setup();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);

    button(el, 'Decline').click();
    expect(await answer(http, () => undefined)).toEqual([]);

    button(el, 'Decline').click();
    const calls = await answer(http, (m, url) =>
      m === 'DELETE' && url === '/api/admin/users/u2' ? ok() :
      m === 'GET' && url === '/api/admin/users' ? ok([users[1]]) : undefined);
    fixture.detectChanges();

    expect(confirm).toHaveBeenCalledTimes(2);
    expect(calls[0].method).toBe('DELETE');
    expect(el.textContent).not.toContain('mom@example.com');
  });
});
