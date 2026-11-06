import { describe, expect, it } from 'vitest';
import { compareValues } from './Table';

describe('compareValues', () => {
  it('sorts issue keys by their number, not character by character', () => {
    expect(['SB-10', 'SB-9', 'SB-1'].sort((a, b) => compareValues(a, b, 'asc'))).toEqual(['SB-1', 'SB-9', 'SB-10']);
  });

  it('ignores case and flips for descending', () => {
    expect(['banana', 'Apple', 'cherry'].sort((a, b) => compareValues(a, b, 'desc'))).toEqual(['cherry', 'banana', 'Apple']);
  });

  it('keeps empty values last in both directions', () => {
    for (const direction of ['asc', 'desc'] as const) {
      expect([null, 2, undefined, 1].sort((a, b) => compareValues(a, b, direction)).slice(2)).toEqual([null, undefined]);
    }
  });
});
