import { describe, expect, it } from 'vitest';
import { budgetUse, money } from './cost';

describe('money', () => {
  it('formats dollars and handles missing values', () => {
    expect(money(3.5)).toBe('$3.50');
    expect(money(null)).toBe('—');
  });
});

describe('budgetUse', () => {
  it('is null without a total or budget', () => {
    expect(budgetUse(null, 80)).toBeNull();
    expect(budgetUse(40, null)).toBeNull();
  });
  it('works out what is left, or how far over', () => {
    expect(budgetUse(60, 80)).toEqual({ percent: 75, left: 20, over: false });
    expect(budgetUse(92.5, 80)).toEqual({ percent: 116, left: -12.5, over: true });
  });
});
