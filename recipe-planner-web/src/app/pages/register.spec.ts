import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { RegisterPage } from './register';
import { answer, fail, ok, Reply, type } from '../../testing/fake-api';

describe('RegisterPage', () => {
  let http: HttpTestingController;
  let navigated: string[];

  async function setup(needApproval = true) {
    TestBed.configureTestingModule({
      imports: [RegisterPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    });
    http = TestBed.inject(HttpTestingController);
    navigated = [];
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockImplementation(url => {
      navigated.push(url.toString());
      return Promise.resolve(true);
    });
    const fixture = TestBed.createComponent(RegisterPage);
    fixture.detectChanges();
    await answer(http, (_m, url) =>
      url === '/api/features' ? ok({ passwordReset: true, nutrition: false, signUpsNeedApproval: needApproval }) : undefined);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  async function signUp(fixture: Awaited<ReturnType<typeof setup>>['fixture'], loginReply: Reply) {
    const el = fixture.nativeElement as HTMLElement;
    type(el.querySelector('input[name=email]')!, 'mom@example.com');
    type(el.querySelector('input[name=password]')!, 'correct horse battery');
    type(el.querySelector('input[name=confirm]')!, 'correct horse battery');
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await answer(http, (method, url) =>
      method === 'POST' && url === '/api/auth/register' ? ok('') :
      method === 'POST' && url === '/api/auth/login?useCookies=true' ? loginReply :
      url === '/api/auth/manage/info' ? ok({ email: 'mom@example.com', isEmailConfirmed: false }) : undefined);
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('says up front that new accounts need approval', async () => {
    const { el } = await setup();
    expect(el.textContent).toContain('approve');
  });

  it("says the account is waiting for approval instead of signing in, when it isn't approved yet", async () => {
    const { fixture, el } = await setup();
    await signUp(fixture, fail(401, { title: 'Unauthorized', detail: 'NotAllowed' }));

    expect(el.querySelector('[role=status]')!.textContent).toContain('waiting for approval');
    expect(el.querySelector('form')).toBeNull();
    expect(navigated).toEqual([]);
  });

  it('signs straight in when approval is off (local development)', async () => {
    const { fixture, el } = await setup(false);
    expect(el.textContent).not.toContain('approve');
    await signUp(fixture, ok(''));
    expect(navigated).toEqual(['/recipes']);
  });
});
