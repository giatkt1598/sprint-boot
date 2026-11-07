import { afterEach, describe, expect, it } from 'vitest';
import { formatDate, formatDateTime, formatMinutes, parseDuration, setDisplayTimeZone, todayIso } from './format';

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

describe('time zone', () => {
  afterEach(() => setDisplayTimeZone(null));

  it('shows a timestamp in the chosen zone', () => {
    setDisplayTimeZone('Asia/Ho_Chi_Minh');
    expect(formatDateTime('2026-11-07T20:30:00Z', 'en')).toContain('Nov 8, 2026');
    setDisplayTimeZone('America/Los_Angeles');
    expect(formatDateTime('2026-11-07T20:30:00Z', 'en')).toContain('Nov 7, 2026');
  });

  it('never moves a calendar day', () => {
    setDisplayTimeZone('America/Los_Angeles');
    expect(formatDate('2026-11-08', 'en')).toBe('Nov 8, 2026');
    setDisplayTimeZone('Pacific/Kiritimati');
    expect(formatDate('2026-11-08', 'en')).toBe('Nov 8, 2026');
  });

  it('gives today in the chosen zone', () => {
    setDisplayTimeZone('Pacific/Kiritimati');
    expect(todayIso()).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });
});
