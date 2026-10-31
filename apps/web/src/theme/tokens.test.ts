import { describe, expect, it } from 'vitest';
import { primitive, semanticLight } from './tokens';

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
    expect(Object.keys(primitive)).toHaveLength(25);
    expect(Object.keys(semanticLight)).toHaveLength(35);
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
