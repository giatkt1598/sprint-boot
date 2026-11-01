import { describe, expect, it } from 'vitest';
import { monthCells } from './DatePicker';

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
