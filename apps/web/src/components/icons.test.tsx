import { act, cleanup, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { THEMES, type ThemeDefinition } from '../theme/themes';
import chinese from '../theme/icons/chinese';
import european from '../theme/icons/european';
import japanese from '../theme/icons/japanese';
import vietnamese from '../theme/icons/vietnamese';
import { BoardIcon, ICON_NAMES, setIconSet, type IconProps } from './icons';

describe('themed icons', () => {
  afterEach(() => {
    act(() => setIconSet({}));
    cleanup();
  });

  it('draw Classic until a theme brings its own set, and fall back to Classic for names the set lacks', () => {
    const { container } = render(<BoardIcon size={16} aria-hidden />);
    expect(container.querySelector('svg')?.classList.contains('lucide')).toBe(true);

    act(() => setIconSet({ board: (props: IconProps) => <svg data-testid="own" width={props.size} /> }));
    expect(container.querySelector('[data-testid="own"]')?.getAttribute('width')).toBe('16');

    act(() => setIconSet({ close: () => <svg data-testid="other" /> }));
    expect(container.querySelector('svg')?.classList.contains('lucide')).toBe(true);
  });

  it.each(THEMES.filter((theme) => 'icons' in theme).map((theme) => [theme.id, theme as ThemeDefinition] as const))(
    'the %s set only names known icons and draws each one as an SVG',
    async (_, theme) => {
      const set = await theme.icons!();
      const names = Object.keys(set);
      expect(names.length).toBeGreaterThan(ICON_NAMES.length * 0.7);
      expect(names.filter((name) => !(ICON_NAMES as readonly string[]).includes(name))).toEqual([]);
      for (const name of names) {
        const Drawn = set[name as keyof typeof set]!;
        const { container, unmount } = render(<Drawn size={16} aria-hidden />);
        expect(container.querySelector('svg'), name).not.toBeNull();
        unmount();
      }
    },
    // The first import of a whole icon library is slow while the other test files run beside it.
    60_000,
  );

  it.each([
    ['chinese', chinese],
    ['japanese', japanese],
    ['vietnamese', vietnamese],
    ['european', european],
  ] as const)('the %s theme draws every icon itself, none from a library', async (id, own) => {
    expect(Object.keys(own).sort()).toEqual([...ICON_NAMES].sort());
    const set = await (THEMES.find((theme) => theme.id === id) as ThemeDefinition).icons!();
    expect(set).toBe(own);
  }, 60_000);
});
