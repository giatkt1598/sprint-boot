import { describe, expect, it } from 'vitest';
import indexHtml from '../../index.html?raw';
import en from '../i18n/locales/en.json';
import { normalizeAppearance } from './appearance';
import { FONT_CHOICES } from './fonts';
import { cssVariables } from './theme';
import { THEMES, themeById, type ThemeDefinition } from './themes';

describe('themes', () => {
  it('have unique ids, a name and a blue accent when they offer accents', () => {
    expect(new Set(THEMES.map((theme) => theme.id)).size).toBe(THEMES.length);
    for (const theme of THEMES) {
      expect(en.account.preferences.themeName).toHaveProperty(theme.id);
      if ('accents' in theme) {
        expect(theme.accents).toHaveProperty('blue');
      }
    }
  });

  it('list the dark ones in the first-paint script of index.html', () => {
    const listed = /DARK_THEMES = \[([^\]]*)\]/.exec(indexHtml)?.[1] ?? '';
    const names = [...listed.matchAll(/'([^']+)'/g)].map((match) => match[1]).sort();
    expect(names).toEqual(THEMES.filter((theme) => theme.base === 'dark').map((theme) => theme.id).sort());
  });

  it('emit a block that a preview card can carry, and accent blocks after the themes', () => {
    const selectors = Object.keys(cssVariables());
    for (const theme of THEMES) {
      expect(selectors).toContain(`:root[data-app-theme="${theme.id}"], [data-app-theme="${theme.id}"]`);
    }
    const lastTheme = selectors.findLastIndex((selector) => !selector.includes('data-accent') && selector.includes('data-app-theme'));
    const firstAccent = selectors.findIndex((selector) => selector.includes('data-accent'));
    expect(firstAccent).toBeGreaterThan(lastTheme);
  });

  it('let the decor through the content area and draw all three pictures when they have a decor', () => {
    for (const theme of THEMES as readonly ThemeDefinition[]) {
      if (theme.decor) {
        expect(theme.content, theme.id).toBe('transparent');
        expect(Object.keys(theme.illustrations ?? {}).sort(), theme.id).toEqual(['empty', 'error', 'notFound']);
      }
    }
  });

  it('suggest a font people can pick, and leave the font to the person', () => {
    const blocks = cssVariables();
    for (const theme of THEMES as readonly ThemeDefinition[]) {
      expect(FONT_CHOICES, theme.id).toContain(theme.font ?? 'poppins');
      expect(blocks[`:root[data-app-theme="${theme.id}"], [data-app-theme="${theme.id}"]`], theme.id).not.toHaveProperty('--sb-heading-font');
    }
    for (const choice of FONT_CHOICES.filter((choice) => choice !== 'poppins')) {
      expect(blocks[`:root[data-font="${choice}"]`]).toHaveProperty('--sb-heading-font');
    }
  });

  it('fall back to Classic for an id nobody knows', () => {
    expect(themeById('retired').id).toBe('classic');
  });
});

describe('saved appearance', () => {
  it('turns the old dark colour mode into the Dark theme', () => {
    expect(normalizeAppearance({ colorMode: 'dark', accent: 'violet' })).toMatchObject({ theme: 'dark', accent: 'violet', motion: 'full' });
    expect(normalizeAppearance({ colorMode: 'light' }).theme).toBe('classic');
  });

  it('keeps a chosen theme and drops values it does not know', () => {
    expect(normalizeAppearance({ theme: 'minimal', colorMode: 'dark' }).theme).toBe('minimal');
    expect(normalizeAppearance({ theme: 'retired', fontScale: 105, motion: 'wild' })).toMatchObject({ theme: 'classic', fontScale: 100, motion: 'full' });
    expect(normalizeAppearance(null)).not.toHaveProperty('colorMode');
    expect(normalizeAppearance({ theme: 'classic', fontFamily: 'oswald' }).fontFamily).toBe('oswald');
    expect(normalizeAppearance({ fontFamily: 'comic' }).fontFamily).toBe('poppins');
  });
});
