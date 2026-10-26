// The order you'd walk through a typical grocery store
export const STORE_SECTION_ORDER = [
  'Produce', 'Meat', 'Seafood', 'Dairy', 'Bakery', 'Grains & Pasta', 'Canned Goods', 'Baking',
  'Oils & Vinegars', 'Condiments & Sauces', 'Spices', 'Nuts & Seeds', 'Frozen', 'Beverages'
];

export function groupBySection<T extends { storeSection: string | null }>(
  items: T[], sortItems: (a: T, b: T) => number
): { section: string; items: T[] }[] {
  const groups = new Map<string, T[]>();
  for (const item of items) {
    const section = item.storeSection ?? 'Other';
    groups.set(section, [...(groups.get(section) ?? []), item]);
  }
  const rank = (s: string) => {
    const i = STORE_SECTION_ORDER.indexOf(s);
    return i === -1 ? STORE_SECTION_ORDER.length : i;
  };
  return [...groups.entries()]
    .sort(([a], [b]) => rank(a) - rank(b) || a.localeCompare(b))
    .map(([section, list]) => ({ section, items: [...list].sort(sortItems) }));
}
