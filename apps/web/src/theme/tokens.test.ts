import { describe, expect, it } from 'vitest';
import { themeColors } from './theme';
import { THEMES, type ThemeDefinition } from './themes';
import { primitive, semanticDark, semanticLight, type Accent } from './tokens';

const luminance = (hex: string) => {
  const channels = [1, 3, 5].map((start) => {
    const value = parseInt(hex.slice(start, start + 2), 16) / 255;
    return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * channels[0]! + 0.7152 * channels[1]! + 0.0722 * channels[2]!;
};

const contrast = (a: string, b: string) => {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (light! + 0.05) / (dark! + 0.05);
};

describe('tokens', () => {
  it('match the Penpot token counts', () => {
    // 25 and 35 in Penpot, plus green50, violet50, violet700, bar-done and feedback-danger-solid that the accents and dark mode brought.
    expect(Object.keys(primitive)).toHaveLength(28);
    expect(Object.keys(semanticLight)).toHaveLength(37);
  });

  it('keep the dark set in step with the light one', () => {
    expect(Object.keys(semanticDark).sort()).toEqual(Object.keys(semanticLight).sort());
  });

  it.each([
    ['text-primary', 'bg-page'],
    ['text-secondary', 'bg-page'],
    ['text-tertiary', 'bg-page'],
    ['text-tertiary', 'bg-surface'],
    ['text-accent', 'bg-selected'],
    ['text-on-accent', 'accent-default'],
    ['status-todo-text', 'status-todo-bg'],
    ['status-progress-text', 'status-progress-bg'],
    ['status-done-text', 'status-done-bg'],
    ['status-merged-text', 'status-merged-bg'],
    ['feedback-warning-text', 'feedback-warning-bg'],
    ['feedback-danger-text', 'feedback-danger-bg'],
  ] as const)('%s on %s meets 4.5:1', (text, background) => {
    expect(contrast(semanticLight[text], semanticLight[background])).toBeGreaterThanOrEqual(4.5);
  });
});

const PAIRS = [
  ['text-primary', 'bg-page'],
  ['text-primary', 'bg-surface'],
  ['text-secondary', 'bg-surface'],
  ['text-tertiary', 'bg-surface'],
  ['text-primary', 'bg-hover'],
  ['text-on-accent', 'accent-default'],
  ['text-accent', 'bg-selected'],
  ['text-accent', 'bg-surface'],
  ['status-todo-text', 'status-todo-bg'],
  ['status-progress-text', 'status-progress-bg'],
  ['status-done-text', 'status-done-bg'],
  ['status-merged-text', 'status-merged-bg'],
  ['feedback-warning-text', 'feedback-warning-bg'],
  ['feedback-danger-text', 'feedback-danger-bg'],
  ['text-on-accent', 'bar-done'],
  ['text-on-accent', 'feedback-danger-solid'],
  ['text-on-accent', 'feedback-danger-hover'],
] as const;

describe.each(THEMES.map((theme) => [theme.id, theme as ThemeDefinition] as const))('the %s theme', (_, theme) => {
  const offered = Object.keys(theme.accents ?? { blue: {} }) as Accent[];

  it.each(offered)('keeps text readable with the %s accent', (accent) => {
    const colors = themeColors(theme, accent);
    for (const [text, background] of PAIRS) {
      expect(contrast(colors[text], colors[background]), `${text} on ${background}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it('sets every semantic token', () => {
    expect(Object.keys(theme.tokens).sort()).toEqual(Object.keys(semanticLight).sort());
  });
});
