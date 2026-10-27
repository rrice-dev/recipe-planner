import { RecipeSummary } from './models';
import { countWithoutEstimate, searchRecipes } from './recipe-search';

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

  describe('calories', () => {
    const [beef, guac, adobo] = book;
    // Beef 520 (complete), guacamole 180 (some ingredients not counted), adobo has no estimate
    const calories = new Map([
      [beef.id, { recipeId: beef.id, calories: 520, complete: true }],
      [guac.id, { recipeId: guac.id, calories: 180, complete: false }],
      [adobo.id, { recipeId: adobo.id, calories: null, complete: false }],
    ]);

    it('keeps recipes at or under the limit and leaves out ones without an estimate', () => {
      expect(searchRecipes(book, { ...q, calories, maxCalories: 500 }).map(x => x.title)).toEqual(['AIP Guacamole']);
      expect(searchRecipes(book, { ...q, calories, maxCalories: 520 }).map(x => x.title)).toEqual(['AIP Guacamole', 'Beef and Broccoli']);
      expect(searchRecipes(book, { ...q, calories, maxCalories: 100 })).toEqual([]);
      expect(countWithoutEstimate(book, calories)).toBe(1);
    });

    it('shows everything when no limit is set, even before estimates load', () => {
      expect(searchRecipes(book, { ...q, calories, maxCalories: null }).length).toBe(3);
      expect(searchRecipes(book, { ...q, maxCalories: null }).length).toBe(3);
    });

    it('sorts fewest calories first, with unknowns last', () => {
      expect(searchRecipes(book, { ...q, calories, sort: 'calories' }).map(x => x.title))
        .toEqual(['AIP Guacamole', 'Beef and Broccoli', 'Chicken Adobo']);
    });
  });
});
