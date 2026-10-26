import { clampServings, formatAmount, formatUnit, scaleQuantity } from './scaling';

describe('scaleQuantity', () => {
  it('scales up and down in proportion', () => {
    expect(scaleQuantity(2, 4, 8)).toBe(4);
    expect(scaleQuantity(1, 4, 2)).toBe(0.5);
    expect(scaleQuantity(1.5, 4, 6)).toBeCloseTo(2.25);
  });

  it('leaves the amount alone if the recipe has no servings', () => {
    expect(scaleQuantity(3, 0, 6)).toBe(3);
  });
});

describe('clampServings', () => {
  it('keeps servings between 1 and 100 and whole', () => {
    expect(clampServings(0)).toBe(1);
    expect(clampServings(-5)).toBe(1);
    expect(clampServings(250)).toBe(100);
    expect(clampServings(3.6)).toBe(4);
    expect(clampServings(NaN)).toBe(1);
  });
});

describe('formatAmount', () => {
  it.each([
    [2, 'cup', '2'],
    [0.5, 'cup', '½'],
    [1.5, 'tbsp', '1½'],
    [0.33, 'cup', '⅓'],
    [1.333, 'cup', '1⅓'],
    [0.67, 'cup', '⅔'],
    [0.25, 'tsp', '¼'],
    [0.75, 'cup', '¾'],
    [0.125, 'tsp', '⅛'],
    [2.97, 'cup', '3'],
    [0.01, 'tsp', '⅛'],
  ])('%s %s -> %s', (q, unit, expected) => {
    expect(formatAmount(q, unit)).toBe(expected);
  });

  it('shows whole grams and milliliters, since that is what a scale reads', () => {
    expect(formatAmount(453.6, 'g')).toBe('454');
    expect(formatAmount(0.2, 'ml')).toBe('1');
    expect(formatAmount(1.25, 'kg')).toBe('1.25');
  });

  it('rounds counted things to halves and never down to nothing', () => {
    expect(formatAmount(2.4, 'whole')).toBe('2½');
    expect(formatAmount(2.8, 'clove')).toBe('3');
    expect(formatAmount(0.67, 'whole')).toBe('½');
    expect(formatAmount(0.2, 'whole')).toBe('¼');
  });

  it('drops fractions on large amounts', () => {
    expect(formatAmount(12.9, 'cup')).toBe('13');
  });

  it('never shows a negative or missing amount', () => {
    expect(formatAmount(0, 'cup')).toBe('0');
    expect(formatAmount(-1, 'cup')).toBe('0');
  });
});

describe('formatUnit', () => {
  it('hides "whole" but keeps real units', () => {
    expect(formatUnit('whole')).toBe('');
    expect(formatUnit('cup')).toBe('cup');
  });
});
