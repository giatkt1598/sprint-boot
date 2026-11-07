import { classic, dark, minimal } from './basic';
import type { ThemeCategory, ThemeDefinition } from './types';

export type { DecorProps, IllustrationKind, ThemeCategory, ThemeDefinition } from './types';

/**
 * Every theme the gallery offers, in gallery order. A new theme also needs its id in PreferencesDto.ThemePattern on
 * the server, its name in the locale files and, when it is dark, in the DARK_THEMES list of index.html.
 */
export const THEMES = [classic, dark, minimal] as const;

export type ThemeId = (typeof THEMES)[number]['id'];

export const THEME_CATEGORIES: ThemeCategory[] = ['basic', 'gradient', 'element', 'culture'];

export const DEFAULT_THEME: ThemeId = 'classic';

const byId = new Map<string, ThemeDefinition>(THEMES.map((theme) => [theme.id, theme]));

export const isThemeId = (value: unknown): value is ThemeId => typeof value === 'string' && byId.has(value);

/** An unknown id, such as one a newer version saved, means Classic. */
export const themeById = (id: string | null | undefined): ThemeDefinition => byId.get(id ?? '') ?? classic;
