import { findTimes, formatClock } from './cook-timers';

describe('findTimes', () => {
  it('finds single times and ranges, using the low end of a range', () => {
    expect(findTimes('Simmer for 10 minutes.')).toEqual([{ label: '10 minutes', seconds: 600 }]);
    expect(findTimes('Cook until golden, 6–8 minutes.')).toEqual([{ label: '6–8 minutes', seconds: 360 }]);
    expect(findTimes('Braise 1.5 to 2 hours')).toEqual([{ label: '1.5 to 2 hours', seconds: 5400 }]);
    expect(findTimes('Fry 30 seconds')).toEqual([{ label: '30 seconds', seconds: 30 }]);
    expect(findTimes('Rest 5 min')).toEqual([{ label: '5 min', seconds: 300 }]);
  });

  it('finds several times in one step without duplicates', () => {
    const times = findTimes('Sear 3 minutes per side, then roast 20 minutes. Rest 3 minutes.');
    expect(times.map(t => t.seconds)).toEqual([180, 1200]);
  });

  it('ignores numbers that are not times', () => {
    expect(findTimes('Heat the oven to 425°F and use 2 cups of rice.')).toEqual([]);
    expect(findTimes('Marinate 4 hours to overnight')).toEqual([{ label: '4 hours', seconds: 14400 }]);
  });
});

describe('formatClock', () => {
  it('reads like a kitchen timer', () => {
    expect(formatClock(75)).toBe('1:15');
    expect(formatClock(5)).toBe('0:05');
    expect(formatClock(3725)).toBe('1:02:05');
    expect(formatClock(-3)).toBe('0:00');
    expect(formatClock(59.2)).toBe('1:00');
  });
});
