import { RecipeSummary } from './models';

// Searching and filtering your recipe book. Kept separate from the page so it's easy to test.

export type RecipeFilter = 'all' | 'favorites' | 'AIP' | 'Vegetarian' | 'quick' | 'untried';
export type RecipeSort = 'title' | 'recent' | 'most' | 'quickest';

export interface RecipeQuery {
  text: string;
  filter: RecipeFilter;
  collectionId: number | null;
  sort: RecipeSort;
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

export function searchRecipes(recipes: RecipeSummary[], q: RecipeQuery): RecipeSummary[] {
  const found = recipes.filter(r =>
    matchesText(r, q.text) && matchesFilter(r, q.filter)
    && (q.collectionId === null || (r.collectionIds ?? []).includes(q.collectionId)));

  const byTitle = (a: RecipeSummary, b: RecipeSummary) => a.title.localeCompare(b.title);
  const sorters: Record<RecipeSort, (a: RecipeSummary, b: RecipeSummary) => number> = {
    title: byTitle,
    // Never-cooked recipes go last
    recent: (a, b) => (b.lastCooked ?? '').localeCompare(a.lastCooked ?? '') || byTitle(a, b),
    most: (a, b) => (b.timesCooked ?? 0) - (a.timesCooked ?? 0) || byTitle(a, b),
    quickest: (a, b) => (a.prepMinutes + a.cookMinutes) - (b.prepMinutes + b.cookMinutes) || byTitle(a, b),
  };
  return [...found].sort(sorters[q.sort]);
}
