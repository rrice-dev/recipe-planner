// Scaling recipes to a different number of servings, and showing the amounts the way a cook would write them.

export const MIN_SERVINGS = 1;
export const MAX_SERVINGS = 100;

export function clampServings(n: number): number {
  if (!Number.isFinite(n)) return MIN_SERVINGS;
  return Math.min(MAX_SERVINGS, Math.max(MIN_SERVINGS, Math.round(n)));
}

export function scaleQuantity(quantity: number, fromServings: number, toServings: number): number {
  if (fromServings <= 0) return quantity;
  return quantity * toServings / fromServings;
}

// Weighed and poured in metric: a scale reads whole grams, so show whole numbers
const METRIC = new Set(['g', 'ml']);
const METRIC_LARGE = new Set(['kg', 'l']);
// Things you count: half an onion makes sense, 0.37 of one doesn't
const COUNTED = new Set(['whole', 'clove', 'slice', 'can', 'stalk', 'sprig', 'head', 'inch', 'bunch', 'package', 'piece']);

// Fractions found on measuring cups and spoons
const FRACTIONS: [number, string][] = [
  [0, ''], [1 / 8, '⅛'], [1 / 4, '¼'], [1 / 3, '⅓'], [3 / 8, '⅜'], [1 / 2, '½'],
  [5 / 8, '⅝'], [2 / 3, '⅔'], [3 / 4, '¾'], [7 / 8, '⅞'], [1, ''],
];

function nearestFraction(q: number, allowed: [number, string][]): string {
  const whole = Math.floor(q);
  const part = q - whole;
  let best = allowed[0];
  for (const f of allowed) if (Math.abs(f[0] - part) < Math.abs(best[0] - part)) best = f;
  const w = best[0] === 1 ? whole + 1 : whole;
  if (!best[1]) return String(w);
  return w ? `${w}${best[1]}` : best[1];
}

/** 1.333 cup -> "1⅓", 0.25 tsp -> "¼", 453.6 g -> "454", 2.4 whole -> "2½" */
export function formatAmount(quantity: number, unit: string): string {
  if (!(quantity > 0)) return '0';
  const u = unit.toLowerCase();
  if (METRIC.has(u)) return String(Math.max(1, Math.round(quantity)));
  if (METRIC_LARGE.has(u)) return String(Number(quantity.toFixed(quantity < 10 ? 2 : 1)));
  // Big amounts don't need fractions: 13 cups, not 12⅞
  if (quantity >= 10) return String(Math.round(quantity));
  if (COUNTED.has(u)) {
    // Never round something you need down to zero
    if (quantity < 0.375) return '¼';
    return nearestFraction(quantity, FRACTIONS.filter(([v]) => v === 0 || v === 0.5 || v === 1));
  }
  // Tiny amounts (a pinch of a pinch) still show something
  if (quantity < 1 / 16) return '⅛';
  return nearestFraction(quantity, FRACTIONS);
}

/** "whole" reads better left out: "2 Eggs", not "2 whole Eggs" */
export function formatUnit(unit: string): string {
  return unit === 'whole' ? '' : unit;
}
