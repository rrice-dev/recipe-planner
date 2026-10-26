import { describe, expect, it } from 'vitest';
import { targetMeters } from './plan-nutrition';

describe('targetMeters', () => {
  const avg = { calories: 1100, protein: 75, carbs: 90, fat: 50, fiber: 10, sugar: 12, sodiumMg: 900 };
  it('only includes targets that are set', () => {
    const m = targetMeters(avg, { calories: 2200, protein: null, carbs: 0, fat: 40 });
    expect(m.map(x => x.label)).toEqual(['Calories', 'Fat']);
  });
  it('works out the percent, even past 100', () => {
    const m = targetMeters(avg, { calories: 2200, protein: null, carbs: null, fat: 40 });
    expect(m[0]).toMatchObject({ value: 1100, target: 2200, percent: 50, unit: '' });
    expect(m[1]).toMatchObject({ percent: 125, unit: ' g' });
  });
});
