import { accents, semanticDark, semanticLight, shadowValues, type Accent, type AccentTokens, type ColorMode } from '../tokens';
import type { ThemeDefinition } from './types';

const accentsFor = (mode: ColorMode) =>
  Object.fromEntries(Object.entries(accents).map(([name, set]) => [name, set[mode]])) as Record<Accent, AccentTokens>;

/** The look the app was designed in: light, with a choice of accent. */
export const classic = {
  id: 'classic',
  category: 'basic',
  base: 'light',
  tokens: { ...semanticLight },
  shadows: shadowValues.light,
  accents: accentsFor('light'),
} satisfies ThemeDefinition<'classic'>;

/** Classic for dark rooms, with the same accents. */
export const dark = {
  id: 'dark',
  category: 'basic',
  base: 'dark',
  tokens: { ...semanticDark },
  shadows: shadowValues.dark,
  accents: accentsFor('dark'),
} satisfies ThemeDefinition<'dark'>;

/** Black on white: colour is kept for what it means (status, type, warnings) and nothing else. */
export const minimal = {
  id: 'minimal',
  category: 'basic',
  base: 'light',
  tokens: {
    ...semanticLight,
    'bg-page': '#FAFAFA',
    'bg-surface': '#FFFFFF',
    'bg-subtle': '#F4F4F5',
    'bg-hover': '#F4F4F5',
    'bg-selected': '#E4E4E7',
    'text-primary': '#18181B',
    'text-secondary': '#52525B',
    'text-tertiary': '#71717A',
    'text-on-accent': '#FFFFFF',
    'text-accent': '#18181B',
    'border-default': '#E4E4E7',
    'border-strong': '#D4D4D8',
    'border-focus': '#18181B',
    'accent-default': '#18181B',
    'accent-hover': '#3F3F46',
    'status-todo-bg': '#E4E4E7',
    'status-todo-text': '#3F3F46',
  },
  shadows: {
    drawer: '0 0 0 1px rgba(24, 24, 27, 0.08), 0 8px 24px rgba(24, 24, 27, 0.08)',
    card: '0 0 0 1px rgba(24, 24, 27, 0.08)',
    popover: '0 0 0 1px rgba(24, 24, 27, 0.08), 0 4px 12px rgba(24, 24, 27, 0.08)',
    dialog: '0 0 0 1px rgba(24, 24, 27, 0.08), 0 8px 24px rgba(24, 24, 27, 0.12)',
  },
} satisfies ThemeDefinition<'minimal'>;
