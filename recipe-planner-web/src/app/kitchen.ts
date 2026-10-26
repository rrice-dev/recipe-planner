import { Leftover } from './models';
import { parseDate } from './date-utils';

export type FreshnessLevel = 'ok' | 'soon' | 'today' | 'past';

// How urgent a leftover is, judged on the user's own calendar day
export function freshness(l: Pick<Leftover, 'useBy'>, today: string): { level: FreshnessLevel; label: string } {
  const days = Math.round((parseDate(l.useBy).getTime() - parseDate(today).getTime()) / 86_400_000);
  if (days < 0) return { level: 'past', label: 'Past its date, best thrown out' };
  if (days === 0) return { level: 'today', label: 'Eat today' };
  if (days === 1) return { level: 'soon', label: 'Eat by tomorrow' };
  const when = parseDate(l.useBy).toLocaleDateString(undefined, days < 7 ? { weekday: 'long' } : { month: 'short', day: 'numeric' });
  return { level: days <= 2 ? 'soon' : 'ok', label: `Eat by ${when}` };
}

export function portionsLabel(n: number) {
  return `${n} portion${n === 1 ? '' : 's'}`;
}

// A starting point for "add common staples"; ones already in the pantry are skipped
export const COMMON_STAPLES = [
  'Salt', 'Kosher Salt', 'Black Pepper', 'Olive Oil', 'Vegetable Oil', 'Butter', 'All-Purpose Flour', 'Granulated Sugar',
  'Brown Sugar', 'Baking Soda', 'Baking Powder', 'Garlic Powder', 'Onion Powder', 'Paprika', 'Ground Cumin',
  'Dried Oregano', 'Red Pepper Flakes', 'Soy Sauce', 'White Vinegar', 'Honey', 'Water'
];

// "Left off what's in your pantry: salt, olive oil." after adding to the shopping list
export function pantryNote(skipped: string[]) {
  if (!skipped.length) return '';
  const names = skipped.length <= 4 ? skipped.join(', ') : `${skipped.slice(0, 3).join(', ')} and ${skipped.length - 3} more`;
  return ` Left off what's in your pantry: ${names.toLowerCase()}.`;
}
