// The map data abbreviates some names to fit. These are the names people expect to read and search for.
// Keys are map names exactly as they appear in world-110m.json (and in the library's "country" field).
export const DISPLAY_NAMES: Record<string, string> = {
  'United States of America': 'United States',
  'Dem. Rep. Congo': 'Democratic Republic of the Congo',
  'Central African Rep.': 'Central African Republic',
  'Dominican Rep.': 'Dominican Republic',
  'Eq. Guinea': 'Equatorial Guinea',
  'Bosnia and Herz.': 'Bosnia and Herzegovina',
  'Solomon Is.': 'Solomon Islands',
  'S. Sudan': 'South Sudan',
  'eSwatini': 'Eswatini',
  'Macedonia': 'North Macedonia',
  "Côte d'Ivoire": "Côte d'Ivoire (Ivory Coast)",
};

// On the map but not somewhere to suggest a cuisine for
export const NOT_SELECTABLE = new Set([
  'Antarctica', 'Fr. S. Antarctic Lands', 'Falkland Is.', 'W. Sahara', 'N. Cyprus', 'Somaliland',
]);

// Too small to draw at this map size, but well worth a search
export const EXTRA_PLACES = [
  'Singapore', 'Hong Kong', 'Malta', 'Barbados', 'Trinidad and Tobago', 'Mauritius', 'Samoa', 'Bahrain', 'Hawaii',
];

export function displayName(mapName: string): string {
  return DISPLAY_NAMES[mapName] ?? mapName;
}
