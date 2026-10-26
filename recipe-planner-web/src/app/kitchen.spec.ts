import { describe, expect, it } from 'vitest';
import { freshness, portionsLabel } from './kitchen';

describe('freshness', () => {
  const today = '2026-10-26';
  it('flags food past its date', () => expect(freshness({ useBy: '2026-10-25' }, today).level).toBe('past'));
  it('says eat today on the use-by day', () => expect(freshness({ useBy: today }, today)).toEqual({ level: 'today', label: 'Eat today' }));
  it('says tomorrow the day before', () => expect(freshness({ useBy: '2026-10-27' }, today).label).toBe('Eat by tomorrow'));
  it('is soon within two days and fine after', () => {
    expect(freshness({ useBy: '2026-10-28' }, today).level).toBe('soon');
    expect(freshness({ useBy: '2026-10-30' }, today).level).toBe('ok');
  });
  it('names a weekday this week and a date further out', () => {
    expect(freshness({ useBy: '2026-10-30' }, today).label).toMatch(/^Eat by \D+$/);
    expect(freshness({ useBy: '2027-01-24' }, today).label).toMatch(/\d/);
  });
});

describe('portionsLabel', () => {
  it('handles one and many', () => {
    expect(portionsLabel(1)).toBe('1 portion');
    expect(portionsLabel(3)).toBe('3 portions');
  });
});
