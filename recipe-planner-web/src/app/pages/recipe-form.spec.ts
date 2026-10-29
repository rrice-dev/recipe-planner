import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { RecipeFormPage } from './recipe-form';
import { answer, Call, choose, fail, ok, Reply, type } from '../../testing/fake-api';

describe('RecipeFormPage', () => {
  let http: HttpTestingController;
  let navigated: unknown[];

  const ingredients = [{ id: 1, name: 'Leek', storeSection: 'Produce' }, { id: 2, name: 'Butter', storeSection: 'Dairy' }];
  const techniques = [{ id: 7, name: 'Sweating', summary: 'Soften without colour', donenessCues: null, commonMistakes: null, difficulty: 1 }];

  // Everything the form loads before you can edit, plus whatever a test adds
  const loads = (extra: (method: string, url: string, body: any) => Reply | undefined = () => undefined) =>
    (method: string, url: string, body: any): Reply | undefined =>
      extra(method, url, body) ??
      (url === '/api/ingredients' ? ok(ingredients) :
       url === '/api/techniques' ? ok(techniques) :
       url === '/api/auth/manage/info' ? ok({ email: 'cook@example.com', isEmailConfirmed: true }) :
       url === '/api/settings' ? ok({ theme: 'system', diet: null, defaultServings: 2, units: 'us' }) :
       undefined);

  async function setup(id?: string, extra?: Parameters<typeof loads>[0]) {
    TestBed.configureTestingModule({
      imports: [RecipeFormPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    });
    http = TestBed.inject(HttpTestingController);
    navigated = [];
    vi.spyOn(TestBed.inject(Router), 'navigate').mockImplementation(commands => {
      navigated.push(commands);
      return Promise.resolve(true);
    });
    const fixture = TestBed.createComponent(RecipeFormPage);
    if (id) fixture.componentRef.setInput('id', id);
    fixture.detectChanges();
    await answer(http, loads(extra));
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  function fillIn(el: HTMLElement) {
    type(el.querySelector('input[formControlName=title]')!, 'Leek soup');
    choose(el.querySelector('select[formControlName=ingredientId]')!, 'Leek');
    type(el.querySelector('input[formControlName=quantity]')!, '2');
    type(el.querySelector('input[formControlName=unit]')!, ' whole ');
    type(el.querySelector('textarea[formControlName=instruction]')!, 'Sweat the leeks in butter');
    choose(el.querySelector('select[formControlName=techniqueId]')!, 'Sweating');
  }

  const saveButton = (el: HTMLElement) =>
    [...el.querySelectorAll('button')].find(b => b.textContent!.includes('Save recipe'))!;

  afterEach(() => http.verify());

  it('starts a new recipe at your default servings', async () => {
    const { el } = await setup();
    expect((el.querySelector('input[formControlName=servings]') as HTMLInputElement).value).toBe('2');
  });

  it('saves a new recipe: the recipe, then its ingredients, steps and techniques', async () => {
    const { el } = await setup();
    fillIn(el);
    saveButton(el).click();

    const calls: Call[] = await answer(http, (method, url) =>
      method === 'POST' && url === '/api/recipes' ? ok({ id: 42, title: 'Leek soup' }) :
      method === 'PUT' && url === '/api/recipes/42/ingredients' ? ok() :
      method === 'PUT' && url === '/api/recipes/42/steps' ? ok() :
      method === 'POST' && url === '/api/recipes/42/techniques/7' ? ok() : undefined);

    expect(calls.map(c => `${c.method} ${c.url}`)).toEqual([
      'POST /api/recipes', 'PUT /api/recipes/42/ingredients', 'PUT /api/recipes/42/steps', 'POST /api/recipes/42/techniques/7'
    ]);
    expect(calls[0].body).toMatchObject({ title: 'Leek soup', servings: 2, description: null });
    expect(calls[1].body).toEqual([{ ingredientId: 1, quantity: 2, unit: 'whole', note: null }]);
    expect(calls[2].body).toEqual([{ instruction: 'Sweat the leeks in butter', techniqueId: 7 }]);
    expect(navigated).toEqual([['/recipes', 42]]);
  });

  it("doesn't send anything while required fields are empty", async () => {
    const { fixture, el } = await setup();
    saveButton(el).click();
    fixture.detectChanges();

    expect(await answer(http, () => undefined)).toEqual([]);
    expect(el.textContent).toContain('Please fill in all required fields');
    expect(el.textContent).toContain('Title is required');
  });

  it('refuses an amount over 10,000 before asking the API', async () => {
    const { fixture, el } = await setup();
    fillIn(el);
    type(el.querySelector('input[formControlName=quantity]')!, '20000');
    saveButton(el).click();
    fixture.detectChanges();

    expect(await answer(http, () => undefined)).toEqual([]);
    expect(el.textContent).toContain('0.01 to 10,000');
  });

  it("shows the API's reason when it refuses the recipe", async () => {
    const { fixture, el } = await setup();
    fillIn(el);
    saveButton(el).click();
    await answer(http, (method, url) =>
      method === 'POST' && url === '/api/recipes'
        ? fail(400, { title: 'One or more validation errors occurred.', errors: { Description: ['Description must be 2000 characters or fewer.'] } })
        : undefined);
    fixture.detectChanges();

    expect(el.querySelector('.error')!.textContent).toContain('Description must be 2000 characters or fewer.');
    expect(navigated).toEqual([]);
  });

  it("explains when you can't add another ingredient of your own", async () => {
    const { fixture, el } = await setup();
    type(el.querySelector('input[placeholder="New ingredient name"]')!, 'Sumac');
    [...el.querySelectorAll('button')].find(b => b.textContent!.trim() === 'Create')!.click();
    await answer(http, (method, url) => method === 'POST' && url === '/api/ingredients'
      ? fail(400, { errors: { Name: ['You can add up to 500 ingredients of your own.'] } }) : undefined);
    fixture.detectChanges();

    expect(el.querySelector('.error')!.textContent).toContain('You can add up to 500 ingredients of your own.');
  });

  it('says so when the ingredient already exists', async () => {
    const { fixture, el } = await setup();
    type(el.querySelector('input[placeholder="New ingredient name"]')!, 'Leek');
    [...el.querySelectorAll('button')].find(b => b.textContent!.trim() === 'Create')!.click();
    await answer(http, (method, url) => method === 'POST' && url === '/api/ingredients'
      ? fail(409, { message: "An ingredient named 'Leek' already exists." }) : undefined);
    fixture.detectChanges();

    expect(el.querySelector('.error')!.textContent).toContain('"Leek" is already in the list');
  });

  it('loads an existing recipe and saves changes to it rather than creating a new one', async () => {
    const recipe = {
      id: 5, title: 'Old soup', description: 'Plain', cuisine: 'French', servings: 4, prepMinutes: 5, cookMinutes: 30,
      techniques: [], ingredients: [{ ingredientId: 2, name: 'Butter', quantity: 1, unit: 'tbsp', note: null, storeSection: 'Dairy' }],
      steps: [{ order: 1, instruction: 'Melt', techniqueId: null, techniqueName: null }]
    };
    const { fixture, el } = await setup('5', (_m, url) => url === '/api/recipes/5' ? ok(recipe) : undefined);
    expect((el.querySelector('input[formControlName=title]') as HTMLInputElement).value).toBe('Old soup');
    expect((el.querySelector('input[formControlName=unit]') as HTMLInputElement).value).toBe('tbsp');

    type(el.querySelector('input[formControlName=title]')!, 'Better soup');
    saveButton(el).click();
    const calls = await answer(http, (method, url) =>
      method === 'PUT' && url.startsWith('/api/recipes/5') ? ok() : undefined);
    fixture.detectChanges();

    expect(calls.map(c => `${c.method} ${c.url}`)).toEqual([
      'PUT /api/recipes/5', 'PUT /api/recipes/5/ingredients', 'PUT /api/recipes/5/steps'
    ]);
    expect(calls[0].body).toMatchObject({ title: 'Better soup', cuisine: 'French', servings: 4 });
    expect(navigated).toEqual([['/recipes', 5]]);
  });
});
