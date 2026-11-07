import { useSyncExternalStore } from 'react';
import type { Accent, ColorMode } from './tokens';

export type ColorChoice = 'system' | 'light' | 'dark';
export type FontChoice = 'poppins' | 'system';

/** What the person chose, plus the image they uploaded. Kept in localStorage too, so the first paint already matches. */
export interface Appearance {
  colorMode: ColorChoice;
  accent: Accent;
  fontFamily: FontChoice;
  fontScale: number;
  backgroundOpacity: number;
  backgroundUrl: string | null;
}

export const FONT_SCALES = [90, 100, 110, 125] as const;

export const defaultAppearance: Appearance = {
  colorMode: 'system',
  accent: 'blue',
  fontFamily: 'poppins',
  fontScale: 100,
  backgroundOpacity: 30,
  backgroundUrl: null,
};

/** Read by the inline script in index.html as well, so the name and shape must stay in step with it. */
export const APPEARANCE_STORAGE_KEY = 'sb.appearance';

const read = (): Appearance => {
  try {
    const stored = JSON.parse(localStorage.getItem(APPEARANCE_STORAGE_KEY) ?? 'null') as Partial<Appearance> | null;
    return { ...defaultAppearance, ...stored };
  } catch {
    return defaultAppearance;
  }
};

let current = read();
const listeners = new Set<() => void>();

const systemDark = () => typeof window !== 'undefined' && window.matchMedia('(prefers-color-scheme: dark)').matches;

/** The mode in force: system follows the device. */
export const resolveMode = (choice: ColorChoice): ColorMode => (choice === 'system' ? (systemDark() ? 'dark' : 'light') : choice);

/** Puts the choices on <html>, where the CSS variables and the background rules read them. */
export function applyAppearance(appearance: Appearance) {
  const root = document.documentElement;
  root.dataset.theme = resolveMode(appearance.colorMode);
  root.dataset.accent = appearance.accent;
  root.dataset.font = appearance.fontFamily;
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
  current = { ...current, ...next };
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

/** Follows the device while the choice is "system". */
export function watchSystemMode() {
  const query = window.matchMedia('(prefers-color-scheme: dark)');
  const onChange = () => {
    if (current.colorMode === 'system') {
      setAppearance({});
    }
  };
  query.addEventListener('change', onChange);
  return () => query.removeEventListener('change', onChange);
}
