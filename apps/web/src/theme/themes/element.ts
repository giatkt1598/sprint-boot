import { semanticDark, semanticLight, shadowValues } from '../tokens';
import fire404 from './art/fire-404.svg';
import fire500 from './art/fire-500.svg';
import glass404 from './art/glass-404.svg';
import glass500 from './art/glass-500.svg';
import glassSeedling from './art/glass-seedling.svg';
import water404 from './art/water-404.svg';
import water500 from './art/water-500.svg';
import wood404 from './art/wood-404.svg';
import wood500 from './art/wood-500.svg';
import woodPlanks from './art/wood-planks.svg';
import { alpha, picture } from './kit';
import type { ThemeDefinition } from './types';

// Liquid Glass: clear panes over young sprouts wet with drops of water, white text on top. The panes barely tint or frost
// what is behind them; their rim catches the light, brightest at the top-left and bottom-right corners, with a soft
// inner glow and a faint sheen across the face.
const glassEdge = [
  'inset 1.5px 1.5px 0 rgba(255, 255, 255, 0.7)',
  'inset -1.5px -1.5px 0 rgba(255, 255, 255, 0.45)',
  'inset 0 0 0 1px rgba(255, 255, 255, 0.12)',
  'inset 0 0 14px rgba(255, 255, 255, 0.1)',
].join(', ');
const glassSheen =
  'linear-gradient(135deg, rgb(255 255 255 / 0.16) 0%, rgb(255 255 255 / 0.03) 30%, rgb(255 255 255 / 0) 60%, rgb(255 255 255 / 0.08) 100%)';
const glassLift = (blur: number, opacity: number) => `${glassEdge}, 0 ${Math.round(blur / 3)}px ${blur}px rgba(0, 0, 0, ${opacity})`;
const glassTint = '#121626';

/** Clear glass over sprouts with drops of water on their leaves. The picture is crisp; the panes only just soften what is behind them. */
export const glass = {
  id: 'glass',
  category: 'element',
  base: 'dark',
  tokens: {
    ...semanticDark,
    'bg-page': '#141826',
    'bg-surface': '#1C2133',
    'bg-subtle': '#262C42',
    'bg-hover': '#2A3150',
    'bg-selected': '#2E3466',
    'text-primary': '#F5F7FF',
    'text-secondary': '#CDD3E6',
    'text-tertiary': '#A6AEC8',
    'text-on-accent': '#FFFFFF',
    'text-accent': '#B4C0FF',
    'border-default': '#FFFFFF38',
    'border-strong': '#FFFFFF5C',
    'border-focus': '#B4C0FF',
    'accent-default': '#4F46E5',
    'accent-hover': '#4338CA',
  },
  shadows: {
    drawer: glassLift(48, 0.4),
    card: glassLift(24, 0.3),
    popover: glassLift(24, 0.35),
    dialog: glassLift(48, 0.45),
  },
  page: `url("${glassSeedling}") center / cover, #4E6534`,
  content: 'transparent',
  surfaceFilter: 'blur(6px) saturate(140%)',
  surfaces: {
    chrome: alpha(glassTint, 0.34),
    card: alpha(glassTint, 0.32),
    column: alpha(glassTint, 0.16),
    overlay: alpha(glassTint, 0.6),
    panel: alpha(glassTint, 0.36),
    cardFilter: 'blur(4px) saturate(140%)',
    sheen: glassSheen,
    edge: glassEdge,
    input: alpha(glassTint, 0.38),
    subtleField: alpha(glassTint, 0.22),
    // A hovered block looks like a card.
    hover: alpha(glassTint, 0.32),
    cardShadow: glassLift(24, 0.3),
  },
  illustrations: { notFound: picture(glass404), error: picture(glass500) },
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
  illustrations: { notFound: picture(wood404), error: picture(wood500) },
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
  illustrations: { notFound: picture(water404), error: picture(water500) },
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
  illustrations: { notFound: picture(fire404), error: picture(fire500) },
} satisfies ThemeDefinition<'fire'>;
