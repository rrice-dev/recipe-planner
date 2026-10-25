// "Chinese (Sichuan)" and "Chinese-inspired" both belong under "Chinese" in the cuisine filter
export function cuisineFamily(cuisine: string): string {
  return cuisine.replace(/\s*\(.*\)$/, '').replace(/-inspired$/i, '').trim();
}

const FRACTIONS: Record<string, string> = { '0.25': '¼', '0.33': '⅓', '0.5': '½', '0.67': '⅔', '0.75': '¾' };

// 1.5 -> "1½", 0.33 -> "⅓", 2 -> "2"
export function friendlyQuantity(q: number): string {
  const whole = Math.floor(q);
  const part = (q - whole).toFixed(2).replace(/0$/, '');
  const frac = FRACTIONS[part];
  if (frac) return whole ? `${whole}${frac}` : frac;
  return String(Number(q.toFixed(2)));
}

// Units like "whole" read better left out: "2 Eggs", not "2 whole Eggs"
export function friendlyUnit(unit: string): string {
  return unit === 'whole' ? '' : unit;
}

export function totalTime(minutes: number): string {
  if (minutes < 60) return `${minutes} min`;
  const h = Math.floor(minutes / 60), m = minutes % 60;
  return m ? `${h} hr ${m} min` : `${h} hr`;
}
