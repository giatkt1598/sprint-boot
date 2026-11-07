import { useSyncExternalStore } from 'react';
import { DEFAULT_THEME, isThemeId, themeById, type ThemeId } from './themes';
import { FONT_CHOICES, type FontChoice } from './fonts';
import type { Accent } from './tokens';

export type { FontChoice } from './fonts';

export type MotionChoice = 'full' | 'reduced' | 'off';

/** What the person chose, plus the image they uploaded. Kept in localStorage too, so the first paint already matches. */
export interface Appearance {
  theme: ThemeId;
  accent: Accent;
  fontFamily: FontChoice;
  fontScale: number;
  backgroundOpacity: number;
  backgroundUrl: string | null;
  motion: MotionChoice;
}

export const FONT_SCALES = [90, 100, 110, 125] as const;
export const MOTIONS = ['full', 'reduced', 'off'] as const;

export const defaultAppearance: Appearance = {
  theme: DEFAULT_THEME,
  accent: 'blue',
  fontFamily: 'poppins',
  fontScale: 100,
  backgroundOpacity: 30,
  backgroundUrl: null,
  motion: 'full',
};

/** Read by the inline script in index.html as well, so the name and shape must stay in step with it. */
export const APPEARANCE_STORAGE_KEY = 'sb.appearance';

const systemDark = () => typeof window !== 'undefined' && window.matchMedia('(prefers-color-scheme: dark)').matches;

/**
 * Keeps only known values. Before themes there was a colour mode: dark, or system on a dark device, becomes the Dark
 * theme, so nobody is thrown back to a light screen.
 */
export function normalizeAppearance(stored: Record<string, unknown> | null | undefined): Appearance {
  const value = stored ?? {};
  const legacyDark = value.colorMode === 'dark' || (value.colorMode === 'system' && systemDark());
  const theme = isThemeId(value.theme) ? value.theme : value.theme === undefined && legacyDark ? 'dark' : DEFAULT_THEME;
  const pick = <T,>(key: keyof Appearance, allowed: readonly T[]) =>
    allowed.includes(value[key] as T) ? (value[key] as T) : (defaultAppearance[key] as T);

  return {
    theme,
    accent: pick<Accent>('accent', ['blue', 'green', 'violet']),
    fontFamily: pick<FontChoice>('fontFamily', FONT_CHOICES),
    fontScale: pick<number>('fontScale', FONT_SCALES),
    backgroundOpacity: typeof value.backgroundOpacity === 'number' ? value.backgroundOpacity : defaultAppearance.backgroundOpacity,
    backgroundUrl: typeof value.backgroundUrl === 'string' ? value.backgroundUrl : null,
    motion: pick<MotionChoice>('motion', MOTIONS),
  };
}

const read = (): Appearance => {
  try {
    return normalizeAppearance(JSON.parse(localStorage.getItem(APPEARANCE_STORAGE_KEY) ?? 'null') as Record<string, unknown> | null);
  } catch {
    return defaultAppearance;
  }
};

let current = read();
const listeners = new Set<() => void>();

/** Puts the choices on <html>, where the CSS variables and the background rules read them. */
export function applyAppearance(appearance: Appearance) {
  const root = document.documentElement;
  const theme = themeById(appearance.theme);
  root.dataset.appTheme = theme.id;
  // Rules that only care about light or dark, such as the rich-text colour swaps, read this one.
  root.dataset.theme = theme.base;
  root.dataset.accent = appearance.accent;
  root.dataset.font = appearance.fontFamily;
  root.dataset.motion = appearance.motion;
  root.style.setProperty('--sb-font-scale', String(appearance.fontScale / 100));
  if (appearance.backgroundUrl) {
    root.dataset.background = 'on';
    root.style.setProperty('--sb-bg-image', `url("${appearance.backgroundUrl}")`);
    // The page colour covers the picture by this much, so a high opacity shows more of it.
    root.style.setProperty('--sb-bg-cover', `${100 - appearance.backgroundOpacity}%`);
  } else {
    delete root.dataset.background;
    root.style.removeProperty('--sb-bg-image');
    root.style.removeProperty('--sb-bg-cover');
  }
}

export function setAppearance(next: Partial<Appearance>) {
  current = normalizeAppearance({ ...current, ...next });
  try {
    localStorage.setItem(APPEARANCE_STORAGE_KEY, JSON.stringify(current));
  } catch {
    // Private windows may refuse storage; the choice still applies for this visit.
  }
  applyAppearance(current);
  listeners.forEach((listener) => listener());
}

const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => listeners.delete(listener);
};

export const useAppearance = () => useSyncExternalStore(subscribe, () => current);
