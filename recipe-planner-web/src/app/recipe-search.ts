import { RecipeCalories, RecipeSummary } from './models';

// Searching and filtering your recipe book. Kept separate from the page so it's easy to test.

export type RecipeFilter = 'all' | 'favorites' | 'AIP' | 'Vegetarian' | 'quick' | 'untried';
export type RecipeSort = 'title' | 'recent' | 'most' | 'quickest' | 'calories';

export interface RecipeQuery {
  text: string;
  filter: RecipeFilter;
  collectionId: number | null;
  sort: RecipeSort;
  /** Calories per serving, at most. Recipes without an estimate are left out while this is set. */
  maxCalories?: number | null;
  /** Estimates by recipe id, from the calories endpoint */
  calories?: ReadonlyMap<number, RecipeCalories>;
}

/** Every word must appear somewhere: title, description, cuisine or an ingredient */
function matchesText(r: RecipeSummary, text: string): boolean {
  const words = text.toLowerCase().split(/\s+/).filter(Boolean);
  if (!words.length) return true;
  const haystack = [r.title, r.description ?? '', r.cuisine ?? '', ...(r.ingredientNames ?? [])].join(' ').toLowerCase();
  return words.every(w => haystack.includes(w));
}

function matchesFilter(r: RecipeSummary, filter: RecipeFilter): boolean {
  switch (filter) {
    case 'favorites': return !!r.isFavorite;
    case 'AIP': case 'Vegetarian': return (r.dietTags ?? []).includes(filter);
    case 'quick': { const t = r.prepMinutes + r.cookMinutes; return t > 0 && t <= 30; }
    case 'untried': return !r.timesCooked;
    default: return true;
  }
}

function caloriesOf(r: RecipeSummary, q: RecipeQuery): number | null {
  return q.calories?.get(r.id)?.calories ?? null;
}

function matchesCalories(r: RecipeSummary, q: RecipeQuery): boolean {
  if (q.maxCalories == null) return true;
  const cal = caloriesOf(r, q);
  return cal !== null && cal <= q.maxCalories;
}

export function searchRecipes(recipes: RecipeSummary[], q: RecipeQuery): RecipeSummary[] {
  const found = recipes.filter(r =>
    matchesText(r, q.text) && matchesFilter(r, q.filter) && matchesCalories(r, q)
    && (q.collectionId === null || (r.collectionIds ?? []).includes(q.collectionId)));

  const byTitle = (a: RecipeSummary, b: RecipeSummary) => a.title.localeCompare(b.title);
  const sorters: Record<RecipeSort, (a: RecipeSummary, b: RecipeSummary) => number> = {
    title: byTitle,
    // Never-cooked recipes go last
    recent: (a, b) => (b.lastCooked ?? '').localeCompare(a.lastCooked ?? '') || byTitle(a, b),
    most: (a, b) => (b.timesCooked ?? 0) - (a.timesCooked ?? 0) || byTitle(a, b),
    quickest: (a, b) => (a.prepMinutes + a.cookMinutes) - (b.prepMinutes + b.cookMinutes) || byTitle(a, b),
    // Recipes without an estimate go last
    calories: (a, b) => (caloriesOf(a, q) ?? Infinity) - (caloriesOf(b, q) ?? Infinity) || byTitle(a, b),
  };
  return [...found].sort(sorters[q.sort]);
}

/** How many recipes a calorie limit hides only because they have no estimate */
export function countWithoutEstimate(recipes: RecipeSummary[], calories: ReadonlyMap<number, RecipeCalories>): number {
  return recipes.filter(r => calories.get(r.id)?.calories == null).length;
}
