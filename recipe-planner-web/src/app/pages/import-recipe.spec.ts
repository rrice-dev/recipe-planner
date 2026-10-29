import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { ImportRecipePage } from './import-recipe';
import { answer, fail, ok, Reply, type } from '../../testing/fake-api';

describe('ImportRecipePage', () => {
  let http: HttpTestingController;
  let navigated: unknown[];

  const draft = {
    title: 'Pancakes', description: 'Fluffy', servings: 4, prepMinutes: 10, cookMinutes: 15,
    ingredients: [{ name: 'Flour', quantity: 1.5, unit: 'cup', note: null, onHand: false }],
    steps: [{ instruction: 'Whisk and fry', techniqueId: 3, techniqueName: 'Pan-frying' }]
  };

  // What every visit loads: who you are, your settings, and which features are on
  const background = (emailConfirmed = true) => (_m: string, url: string): Reply | undefined =>
    url === '/api/auth/manage/info' ? ok({ email: 'cook@example.com', isEmailConfirmed: emailConfirmed }) :
    url === '/api/settings' ? ok({ theme: 'system', diet: null, defaultServings: 4, units: 'us' }) :
    url === '/api/features' ? ok({ passwordReset: true, nutrition: false }) : undefined;

  async function setup(query: Record<string, string> = {}, routes?: (m: string, url: string, body: any) => Reply | undefined) {
    TestBed.configureTestingModule({
      imports: [ImportRecipePage],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } }
      ]
    });
    http = TestBed.inject(HttpTestingController);
    navigated = [];
    vi.spyOn(TestBed.inject(Router), 'navigate').mockImplementation(commands => {
      navigated.push(commands);
      return Promise.resolve(true);
    });
    const fixture = TestBed.createComponent(ImportRecipePage);
    fixture.detectChanges();
    const calls = await answer(http, (m, url, body) => routes?.(m, url, body) ?? background()(m, url));
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, calls };
  }

  async function importLink(fixture: Awaited<ReturnType<typeof setup>>['fixture'], link: string,
                            reply: Reply, emailConfirmed = true) {
    const el = fixture.nativeElement as HTMLElement;
    type(el.querySelector('input[aria-label="Recipe link"]')!, link);
    fixture.detectChanges();
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    const calls = await answer(http, (m, url) => m === 'POST' && url === '/api/import' ? reply : background(emailConfirmed)(m, url));
    fixture.detectChanges();
    return calls;
  }

  afterEach(() => http.verify());

  it('imports a link, adding https:// when it was left off, and shows the recipe to review', async () => {
    const { fixture, el } = await setup();
    const calls = await importLink(fixture, 'www.example.com/pancakes',
      ok({ recipe: draft, sourceUrl: 'https://www.example.com/pancakes', method: 'structured' }));

    expect(calls.find(c => c.url === '/api/import')!.body).toEqual({ url: 'https://www.example.com/pancakes' });
    expect(el.querySelector('.recipe-draft h2')!.textContent).toBe('Pancakes');
    expect(el.querySelector('.recipe-draft')!.textContent).toContain('example.com');
    expect(el.querySelector('.recipe-draft')!.textContent).toContain('Flour');
    // Read from the page's own recipe data, not by AI, so no "double-check" warning
    expect(el.textContent).not.toContain('AI typed this up');
  });

  it('warns to double-check when the AI read the page', async () => {
    const { fixture, el } = await setup();
    await importLink(fixture, 'https://example.com/x', ok({ recipe: draft, sourceUrl: 'https://example.com/x', method: 'ai' }));
    expect(el.textContent).toContain('AI typed this up');
  });

  it("shows the API's explanation when a page can't be imported", async () => {
    const { fixture, el } = await setup();
    await importLink(fixture, 'https://example.com/news',
      fail(422, { detail: "That site doesn't allow apps to read its recipes. Try copying the recipe in by hand." }));

    expect(el.querySelector('[role=alert]')!.textContent).toContain("doesn't allow apps to read its recipes");
    expect(el.querySelector('.recipe-draft')).toBeNull();
  });

  it('asks you to confirm your email when the API requires it', async () => {
    const { fixture, el } = await setup();
    await importLink(fixture, 'https://example.com/x',
      fail(403, { title: 'email_not_confirmed', detail: 'Confirm your email to use AI features.' }));

    expect(el.querySelector('[role=alert]')!.textContent).toContain('Confirm your email to use AI features');
  });

  it('shows the confirm-email note up front to people who have not confirmed yet', async () => {
    const { el } = await setup({}, background(false));
    expect(el.textContent).toContain('Confirm your email to use the AI features');
  });

  it('imports right away when a link is shared to the app from another one', async () => {
    const { el, calls } = await setup({ text: 'Look at this https://example.com/stew so good' }, (m, url) =>
      m === 'POST' && url === '/api/import' ? ok({ recipe: draft, sourceUrl: 'https://example.com/stew', method: 'structured' }) : undefined);

    expect(calls.find(c => c.url === '/api/import')!.body).toEqual({ url: 'https://example.com/stew' });
    expect(el.querySelector('.recipe-draft h2')!.textContent).toBe('Pancakes');
  });

  it('reads pasted text and lists anything it was unsure about', async () => {
    const { fixture, el } = await setup({ mode: 'paste' });
    const box = el.querySelector('textarea[aria-label="Recipe text"]') as HTMLTextAreaElement;
    type(box, 'too short');
    fixture.detectChanges();
    const read = () => [...el.querySelectorAll('button')].find(b => b.textContent!.includes('Read recipe'))!;
    expect(read().disabled).toBe(true);

    type(box, 'Pancakes: 1.5 cups flour, 2 eggs. Whisk and fry.');
    fixture.detectChanges();
    read().click();
    const calls = await answer(http, (m, url) =>
      m === 'POST' && url === '/api/import/text' ? ok({ recipe: draft, unclear: ['Oven temperature was smudged'] }) : undefined);
    fixture.detectChanges();

    expect(calls[0].body).toEqual({ text: 'Pancakes: 1.5 cups flour, 2 eggs. Whisk and fry.' });
    expect(el.textContent).toContain('Check these before you cook');
    expect(el.textContent).toContain('Oven temperature was smudged');
  });

  it("shows the API's reason when the recipe can't be saved", async () => {
    const { fixture, el } = await setup();
    await importLink(fixture, 'https://example.com/pancakes', ok({ recipe: draft, sourceUrl: 'https://example.com/pancakes', method: 'structured' }));

    [...el.querySelectorAll('button')].find(b => b.textContent!.includes('Save to my recipes'))!.click();
    await answer(http, (m, url) =>
      m === 'GET' && url === '/api/ingredients' ? ok([]) :
      m === 'POST' && url === '/api/ingredients' ? fail(400, { errors: { Name: ['You can add up to 500 ingredients of your own.'] } }) : undefined);
    fixture.detectChanges();

    expect(el.querySelector('.recipe-draft [role=alert]')!.textContent).toContain('You can add up to 500 ingredients of your own.');
    expect(navigated).toEqual([]);
  });

  it('saves the reviewed recipe with a link back to the original page, then opens it', async () => {
    const { fixture, el } = await setup();
    await importLink(fixture, 'https://example.com/pancakes', ok({ recipe: draft, sourceUrl: 'https://example.com/pancakes', method: 'structured' }));

    [...el.querySelectorAll('button')].find(b => b.textContent!.includes('Save to my recipes'))!.click();
    const calls = await answer(http, (m, url) =>
      m === 'GET' && url === '/api/ingredients' ? ok([{ id: 9, name: 'flour', storeSection: 'Baking' }]) :
      m === 'POST' && url === '/api/recipes' ? ok({ id: 77, title: 'Pancakes' }) :
      m === 'PUT' && url.startsWith('/api/recipes/77/') ? ok() :
      m === 'POST' && url === '/api/recipes/77/techniques/3' ? ok() : undefined);

    expect(calls.find(c => c.url === '/api/recipes')!.body).toMatchObject({ title: 'Pancakes', sourceUrl: 'https://example.com/pancakes' });
    // "Flour" matched the existing "flour" instead of creating a duplicate
    expect(calls.find(c => c.url === '/api/recipes/77/ingredients')!.body).toEqual([{ ingredientId: 9, quantity: 1.5, unit: 'cup', note: null }]);
    expect(calls.some(c => c.method === 'POST' && c.url === '/api/ingredients')).toBe(false);
    expect(navigated).toEqual([['/recipes', 77]]);
  });
});
