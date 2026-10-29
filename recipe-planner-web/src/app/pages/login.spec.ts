import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { LoginPage } from './login';
import { answer, fail, ok, type } from '../../testing/fake-api';

describe('LoginPage', () => {
  let http: HttpTestingController;
  let navigated: string[];

  function setup(query: Record<string, string> = {}) {
    TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } }
      ]
    });
    http = TestBed.inject(HttpTestingController);
    navigated = [];
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockImplementation(url => {
      navigated.push(url.toString());
      return Promise.resolve(true);
    });
    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();
    return fixture;
  }

  async function logIn(fixture: ReturnType<typeof setup>, loginReply: ReturnType<typeof ok>) {
    const el = fixture.nativeElement as HTMLElement;
    await answer(http, (_m, url) => url === '/api/features' ? ok({ passwordReset: true, nutrition: false }) : undefined);
    type(el.querySelector('input[name=email]')!, '  cook@example.com ');
    type(el.querySelector('input[name=password]')!, 'correct horse battery');
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    const calls = await answer(http, (method, url) =>
      method === 'POST' && url === '/api/auth/login?useCookies=true' ? loginReply :
      url === '/api/auth/manage/info' ? ok({ email: 'cook@example.com', isEmailConfirmed: true }) : undefined);
    fixture.detectChanges();
    return calls;
  }

  afterEach(() => http.verify());

  it('signs in with a trimmed email and returns to the page you came from', async () => {
    const fixture = setup({ returnUrl: '/meal-plans/3' });
    const calls = await logIn(fixture, ok(''));

    expect(calls[0].body).toEqual({ email: 'cook@example.com', password: 'correct horse battery' });
    expect(navigated).toEqual(['/meal-plans/3']);
  });

  it('never follows a returnUrl to another site', async () => {
    const fixture = setup({ returnUrl: 'https://evil.example/phish' });
    await logIn(fixture, ok(''));
    expect(navigated).toEqual(['/recipes']);
  });

  it('gives one message for any wrong email or password, so it reveals nothing about accounts', async () => {
    const fixture = setup();
    await logIn(fixture, fail(401, { title: 'Unauthorized', detail: 'LockedOut' }));

    expect((fixture.nativeElement as HTMLElement).querySelector('.error')!.textContent).toContain('Invalid email or password.');
    expect(navigated).toEqual([]);
  });

  it('says the account is waiting for approval when the password was right but nobody approved it yet', async () => {
    const fixture = setup();
    await logIn(fixture, fail(401, { title: 'Unauthorized', detail: 'NotAllowed' }));

    expect((fixture.nativeElement as HTMLElement).querySelector('.error')!.textContent).toContain('waiting for approval');
    expect(navigated).toEqual([]);
  });

  it('says to wait when there have been too many attempts', async () => {
    const fixture = setup();
    await logIn(fixture, fail(429));

    expect((fixture.nativeElement as HTMLElement).querySelector('.error')!.textContent).toContain('Too many attempts');
    expect(navigated).toEqual([]);
  });

  it('offers "Forgot your password?" only when the site can send email', async () => {
    const fixture = setup();
    await answer(http, (_m, url) => url === '/api/features' ? ok({ passwordReset: false, nutrition: false }) : undefined);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Forgot your password?');
  });
});
