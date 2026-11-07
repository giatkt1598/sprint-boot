import { semanticDark, semanticLight, shadowValues } from '../tokens';
import fire404 from './art/fire-404.svg';
import fire500 from './art/fire-500.svg';
import fireEmpty from './art/fire-empty.svg';
import glass404 from './art/glass-404.svg';
import glass500 from './art/glass-500.svg';
import glassEmpty from './art/glass-empty.svg';
import water404 from './art/water-404.svg';
import water500 from './art/water-500.svg';
import waterEmpty from './art/water-empty.svg';
import wood404 from './art/wood-404.svg';
import wood500 from './art/wood-500.svg';
import woodEmpty from './art/wood-empty.svg';
import woodPlanks from './art/wood-planks.svg';
import { alpha, picture } from './kit';
import type { ThemeDefinition } from './types';

// Every surface and card of Glass has a bright rim instead of a grey border.
const glassCard = '0 8px 24px rgba(30, 27, 75, 0.12), inset 0 0 0 1px rgba(255, 255, 255, 0.75)';

/** Frosted panes over drifting colour. Values from the Penpot page "Themes / Glass". */
export const glass = {
  id: 'glass',
  category: 'element',
  base: 'light',
  tokens: {
    ...semanticLight,
    'bg-page': '#EEF2FF',
    'bg-surface': '#FFFFFF',
    'bg-subtle': '#EEF0FB',
    'bg-hover': '#E6E9F8',
    'bg-selected': '#E0E7FF',
    'text-primary': '#141B3A',
    'text-secondary': '#3E4668',
    'text-tertiary': '#5A6285',
    'text-on-accent': '#FFFFFF',
    'text-accent': '#4338CA',
    'border-default': '#FFFFFFBF',
    'border-strong': '#C7CCE8',
    'border-focus': '#4F46E5',
    'accent-default': '#4F46E5',
    'accent-hover': '#4338CA',
  },
  shadows: {
    drawer: '0 16px 48px rgba(30, 27, 75, 0.2)',
    card: glassCard,
    popover: '0 8px 24px rgba(30, 27, 75, 0.16)',
    dialog: '0 16px 48px rgba(30, 27, 75, 0.2)',
  },
  page: '#EEF2FF',
  content: 'transparent',
  surfaceFilter: 'blur(32px) saturate(160%)',
  surfaces: {
    chrome: alpha('#FFFFFF', 0.45),
    card: alpha('#FFFFFF', 0.58),
    column: alpha('#FFFFFF', 0.22),
    overlay: alpha('#FFFFFF', 0.86),
    cardShadow: glassCard,
  },
  decor: () => import('./decor/glass'),
  illustrations: { notFound: picture(glass404), error: picture(glass500), empty: picture(glassEmpty, 160, 100) },
} satisfies ThemeDefinition<'glass'>;

const woodCard = '0 1px 0 rgba(92, 58, 30, 0.18), 0 3px 8px rgba(59, 36, 20, 0.18)';

/** Cream paper on a wooden desk in a sunbeam. Values from the Penpot page "Themes / Wood". */
export const wood = {
  id: 'wood',
  category: 'element',
  base: 'light',
  tokens: {
    ...semanticLight,
    'bg-page': '#E9D8C0',
    'bg-surface': '#FFFBF3',
    'bg-subtle': '#F4EADB',
    'bg-hover': '#EFE2CF',
    'bg-selected': '#F3E3C8',
    'text-primary': '#2B1D12',
    'text-secondary': '#5C4632',
    'text-tertiary': '#745B44',
    'text-on-accent': '#FFFFFF',
    'text-accent': '#7C3A10',
    'border-default': '#E6D5BE',
    'border-strong': '#D3BC9C',
    'border-focus': '#8B4513',
    'accent-default': '#8B4513',
    'accent-hover': '#6F360F',
    'status-todo-bg': '#EFE2CF',
    'status-todo-text': '#5C4632',
  },
  shadows: {
    drawer: '0 10px 36px rgba(43, 29, 18, 0.35)',
    card: woodCard,
    popover: '0 4px 16px rgba(43, 29, 18, 0.2)',
    dialog: '0 10px 36px rgba(43, 29, 18, 0.3)',
  },
  page: `url("${woodPlanks}") center / cover, #B9844F`,
  content: 'transparent',
  surfaces: {
    chrome: alpha('#FFFBF3', 0.96),
    card: '#FFFBF3',
    column: alpha('#FFF8EC', 0.4),
    overlay: '#FFFBF3',
    // Grain lines under rows of text are hard to read, so tables rest on paper.
    panel: alpha('#FFFBF3', 0.94),
    cardShadow: woodCard,
  },
  font: 'lora',
  decor: () => import('./decor/wood'),
  illustrations: { notFound: picture(wood404), error: picture(wood500), empty: picture(woodEmpty, 160, 100) },
} satisfies ThemeDefinition<'wood'>;

const waterCard = '0 4px 14px rgba(7, 89, 133, 0.14)';

/** Clear shallow water with caustics above and waves below. Values from the Penpot page "Themes / Water". */
export const water = {
  id: 'water',
  category: 'element',
  base: 'light',
  tokens: {
    ...semanticLight,
    'bg-page': '#E6F6FD',
    'bg-surface': '#FFFFFF',
    'bg-subtle': '#E7F3FA',
    'bg-hover': '#DCEEF8',
    'bg-selected': '#D6EEFB',
    'text-primary': '#0B2537',
    'text-secondary': '#2F4B5E',
    'text-tertiary': '#4B6577',
    'text-on-accent': '#FFFFFF',
    'text-accent': '#075985',
    'border-default': '#CFE6F3',
    'border-strong': '#A9CFE4',
    'border-focus': '#0369A1',
    'accent-default': '#0369A1',
    'accent-hover': '#075985',
  },
  // Shadows are tinted blue.
  shadows: {
    drawer: '0 12px 40px rgba(11, 37, 55, 0.25)',
    card: waterCard,
    popover: '0 4px 16px rgba(11, 37, 55, 0.16)',
    dialog: '0 8px 24px rgba(11, 37, 55, 0.22)',
  },
  page: 'linear-gradient(180deg, #E0F7FF 0%, #8FD8F8 45%, #1E88C7 100%)',
  content: 'transparent',
  surfaceFilter: 'blur(16px)',
  surfaces: {
    chrome: alpha('#FFFFFF', 0.78),
    card: alpha('#FFFFFF', 0.92),
    column: alpha('#FFFFFF', 0.4),
    overlay: alpha('#FFFFFF', 0.97),
    cardShadow: waterCard,
  },
  decor: () => import('./decor/water'),
  illustrations: { notFound: picture(water404), error: picture(water500), empty: picture(waterEmpty, 160, 100) },
} satisfies ThemeDefinition<'water'>;

// Cards carry a faint orange rim.
const fireCard = '0 2px 6px rgba(0, 0, 0, 0.5), 0 0 0 1px rgba(251, 146, 60, 0.08)';

/** Firelight rising from the bottom of a dark page. Values from the Penpot page "Themes / Fire". */
export const fire = {
  id: 'fire',
  category: 'element',
  base: 'dark',
  tokens: {
    ...semanticDark,
    'bg-page': '#120806',
    'bg-surface': '#1C1210',
    'bg-subtle': '#2A1A15',
    'bg-hover': '#33201A',
    'bg-selected': '#3A1E12',
    'text-primary': '#FCEEE6',
    'text-secondary': '#D9BBAA',
    'text-tertiary': '#B8988A',
    'text-on-accent': '#FFFFFF',
    'text-accent': '#FDBA74',
    'border-default': '#3A2620',
    'border-strong': '#53362C',
    'border-focus': '#FB923C',
    'accent-default': '#C2410C',
    'accent-hover': '#9A3412',
  },
  shadows: {
    ...shadowValues.dark,
    drawer: '0 10px 40px rgba(0, 0, 0, 0.6)',
    card: fireCard,
  },
  // The wide glow stays when motion is off; the flames and embers of the decor move over it.
  page: 'radial-gradient(64% 39% at 50% 92%, rgb(253 186 116 / 0.55), rgb(234 88 12 / 0.35) 40%, rgb(124 45 18 / 0) 100%), #120806',
  content: 'transparent',
  surfaceFilter: 'blur(16px)',
  surfaces: {
    chrome: alpha('#1C1210', 0.84),
    card: alpha('#1C1210', 0.92),
    column: alpha('#1C1210', 0.32),
    overlay: alpha('#1C1210', 0.98),
    cardShadow: fireCard,
  },
  font: 'oswald',
  decor: () => import('./decor/fire'),
  illustrations: { notFound: picture(fire404), error: picture(fire500), empty: picture(fireEmpty, 160, 100) },
} satisfies ThemeDefinition<'fire'>;
