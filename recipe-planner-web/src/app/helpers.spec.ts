import { cuisineFamily, totalTime } from './library-utils';
import { displayName } from './world-names';
import { groupBySection } from './store-sections';

describe('cuisineFamily', () => {
  it('groups regional and "-inspired" cuisines under the country', () => {
    expect(cuisineFamily('Chinese (Sichuan)')).toBe('Chinese');
    expect(cuisineFamily('Thai-inspired')).toBe('Thai');
    expect(cuisineFamily('Italian')).toBe('Italian');
  });
});

describe('totalTime', () => {
  it('reads naturally', () => {
    expect(totalTime(25)).toBe('25 min');
    expect(totalTime(60)).toBe('1 hr');
    expect(totalTime(195)).toBe('3 hr 15 min');
  });
});

describe('displayName', () => {
  it('expands the map data abbreviations', () => {
    expect(displayName('United States of America')).toBe('United States');
    expect(displayName('Dem. Rep. Congo')).toBe('Democratic Republic of the Congo');
    expect(displayName('Peru')).toBe('Peru');
  });
});

describe('groupBySection', () => {
  it('orders sections the way you walk a store, with unknown sections last', () => {
    const items = [
      { name: 'Milk', storeSection: 'Dairy' },
      { name: 'Batteries', storeSection: null },
      { name: 'Onion', storeSection: 'Produce' },
      { name: 'Apple', storeSection: 'Produce' },
    ];
    const groups = groupBySection(items, (a, b) => a.name.localeCompare(b.name));
    expect(groups.map(g => g.section)).toEqual(['Produce', 'Dairy', 'Other']);
    expect(groups[0].items.map(i => i.name)).toEqual(['Apple', 'Onion']);
  });
});
