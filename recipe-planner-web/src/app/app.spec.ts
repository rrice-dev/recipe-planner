import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';

describe('App', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(signedInAs: string | null) {
    const fixture = TestBed.createComponent(App);
    const req = http.expectOne('/api/auth/manage/info');
    if (signedInAs) req.flush({ email: signedInAs, isEmailConfirmed: true });
    else req.flush(null, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    const nav = fixture.nativeElement.querySelector('nav') as HTMLElement;
    return [...nav.querySelectorAll('a, button')].map(e => e.textContent!.trim());
  }

  it('shows the brand', async () => {
    const fixture = TestBed.createComponent(App);
    http.expectOne('/api/auth/manage/info').flush(null, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.brand')?.textContent).toContain('Recipe Planner');
  });

  it('shows only public pages and a login link when signed out', async () => {
    const links = await render(null);
    expect(links).toEqual(['Browse', 'Techniques', 'Log in']);
  });

  it('shows the full menu and a logout button when signed in', async () => {
    const links = await render('robert@example.com');
    expect(links).toEqual(['Recipes', 'Meal Plans', 'What Can I Make?', 'Shopping List', 'Browse', 'Techniques', 'Log out']);
  });
});
