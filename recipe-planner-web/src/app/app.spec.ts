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
      providers: [provideRouter([{ path: '**', children: [] }]), provideHttpClient(), provideHttpClientTesting()],
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
    // Signing in loads your settings
    if (signedInAs) {
      http.expectOne('/api/settings').flush({ theme: 'system', diet: null, defaultServings: 4 });
      await fixture.whenStable();
    }
    const nav = fixture.nativeElement.querySelector('nav') as HTMLElement;
    return [...nav.querySelectorAll('a, button')].map(e => e.textContent!.trim());
  }

  it('opens and closes the phone menu', async () => {
    const fixture = TestBed.createComponent(App);
    http.expectOne('/api/auth/manage/info').flush(null, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const toggle = el.querySelector('.menu-toggle') as HTMLButtonElement;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    toggle.click();
    fixture.detectChanges();
    expect(el.querySelector('nav')!.classList).toContain('open');
    (el.querySelector('nav a') as HTMLAnchorElement).click(); // picking a page closes it
    fixture.detectChanges();
    expect(el.querySelector('nav')!.classList).not.toContain('open');
  });

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
    expect(links).toEqual(['Recipes', 'Meal plans', 'What can I make?', 'Shopping list', 'Browse', 'Techniques', 'Settings', 'Log out']);
  });
});
