import { RecipeSummary } from './models';
import { searchRecipes } from './recipe-search';

const r = (title: string, extra: Partial<RecipeSummary> = {}): RecipeSummary => ({
  id: Math.random(), title, description: null, cuisine: null, servings: 4, prepMinutes: 10, cookMinutes: 30, photoVersion: null, ...extra,
});

const book = [
  r('Beef and Broccoli', { cuisine: 'Chinese', ingredientNames: ['Flank Steak', 'Broccoli'], timesCooked: 3, lastCooked: '2026-10-01' }),
  r('AIP Guacamole', { ingredientNames: ['Avocado', 'Lime'], dietTags: ['AIP', 'Vegetarian'], prepMinutes: 10, cookMinutes: 0, isFavorite: true, collectionIds: [1] }),
  r('Chicken Adobo', { cuisine: 'Filipino', ingredientNames: ['Chicken Thighs', 'Soy Sauce'], timesCooked: 1, lastCooked: '2026-10-20', collectionIds: [1, 2] }),
];
const q = { text: '', filter: 'all' as const, collectionId: null, sort: 'title' as const };

describe('searchRecipes', () => {
  it('searches titles, cuisines and ingredients, all words required', () => {
    expect(searchRecipes(book, { ...q, text: 'broccoli' }).map(x => x.title)).toEqual(['Beef and Broccoli']);
    expect(searchRecipes(book, { ...q, text: 'avocado' }).map(x => x.title)).toEqual(['AIP Guacamole']);
    expect(searchRecipes(book, { ...q, text: 'filipino chicken' }).map(x => x.title)).toEqual(['Chicken Adobo']);
    expect(searchRecipes(book, { ...q, text: 'chicken broccoli' })).toEqual([]);
  });

  it('filters by favorites, diet, time, untried and collection', () => {
    expect(searchRecipes(book, { ...q, filter: 'favorites' }).map(x => x.title)).toEqual(['AIP Guacamole']);
    expect(searchRecipes(book, { ...q, filter: 'AIP' }).map(x => x.title)).toEqual(['AIP Guacamole']);
    expect(searchRecipes(book, { ...q, filter: 'quick' }).map(x => x.title)).toEqual(['AIP Guacamole']);
    expect(searchRecipes(book, { ...q, filter: 'untried' }).map(x => x.title)).toEqual(['AIP Guacamole']);
    expect(searchRecipes(book, { ...q, collectionId: 2 }).map(x => x.title)).toEqual(['Chicken Adobo']);
  });

  it('sorts by recently cooked, most cooked and quickest', () => {
    expect(searchRecipes(book, { ...q, sort: 'recent' }).map(x => x.title)).toEqual(['Chicken Adobo', 'Beef and Broccoli', 'AIP Guacamole']);
    expect(searchRecipes(book, { ...q, sort: 'most' }).map(x => x.title)).toEqual(['Beef and Broccoli', 'Chicken Adobo', 'AIP Guacamole']);
    expect(searchRecipes(book, { ...q, sort: 'quickest' })[0].title).toBe('AIP Guacamole');
  });
});
