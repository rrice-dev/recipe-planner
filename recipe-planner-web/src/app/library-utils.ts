// "Chinese (Sichuan)" and "Chinese-inspired" both belong under "Chinese" in the cuisine filter
export function cuisineFamily(cuisine: string): string {
  return cuisine.replace(/\s*\(.*\)$/, '').replace(/-inspired$/i, '').trim();
}

export function totalTime(minutes: number): string {
  if (minutes < 60) return `${minutes} min`;
  const h = Math.floor(minutes / 60), m = minutes % 60;
  return m ? `${h} hr ${m} min` : `${h} hr`;
}
