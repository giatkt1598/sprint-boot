import { describe, expect, it } from 'vitest';
import { formatMinutes, parseDuration } from './format';

describe('duration', () => {
  it.each([
    ['1h 30m', 90],
    ['2h', 120],
    ['45m', 45],
    ['15', 15],
  ])('parses %s', (text, minutes) => {
    expect(parseDuration(text)).toBe(minutes);
  });

  it('rejects unreadable text', () => {
    expect(parseDuration('soon')).toBeNull();
    expect(parseDuration('')).toBeNull();
  });

  it('formats minutes', () => {
    expect(formatMinutes(90)).toBe('1h 30m');
    expect(formatMinutes(0)).toBe('0m');
  });
});
