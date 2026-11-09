import { describe, expect, it } from 'vitest';
import { monthCells, rangePart } from './DatePicker';

describe('monthCells', () => {
  it('pads to the first weekday and counts the month', () => {
    const november = monthCells(2026, 10);
    expect(november.filter((day) => day === null)).toHaveLength(0);
    expect(november.at(-1)).toBe(30);

    const february = monthCells(2026, 1);
    expect(february.at(-1)).toBe(28);

    const october = monthCells(2026, 9);
    expect(october.slice(0, 5)).toEqual([null, null, null, null, 1]);
  });
});

describe('rangePart', () => {
  it('marks the ends and the days between, and nothing for a backwards range', () => {
    expect(rangePart('2026-11-02', '2026-11-02', '2026-11-05')).toBe('start');
    expect(rangePart('2026-11-03', '2026-11-02', '2026-11-05')).toBe('inside');
    expect(rangePart('2026-11-05', '2026-11-02', '2026-11-05')).toBe('end');
    expect(rangePart('2026-11-06', '2026-11-02', '2026-11-05')).toBeUndefined();
    expect(rangePart('2026-11-02', '2026-11-02', '2026-11-02')).toBe('single');
    expect(rangePart('2026-11-02', '2026-11-02', '')).toBe('single');
    expect(rangePart('2026-11-03', '2026-11-05', '2026-11-02')).toBeUndefined();
  });
});
