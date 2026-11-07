import { semanticDark, semanticLight, shadowValues } from '../tokens';
import aurora404 from './art/aurora-404.svg';
import aurora500 from './art/aurora-500.svg';
import auroraEmpty from './art/aurora-empty.svg';
import sunset404 from './art/sunset-404.svg';
import sunset500 from './art/sunset-500.svg';
import sunsetEmpty from './art/sunset-empty.svg';
import { alpha, picture } from './kit';
import type { ThemeDefinition } from './types';

const auroraCard = '0 1px 2px rgba(0, 0, 0, 0.4), 0 0 0 1px rgba(94, 234, 212, 0.06)';

/** Northern lights behind night glass. Values from the Penpot page "Themes / Aurora". */
export const aurora = {
  id: 'aurora',
  category: 'gradient',
  base: 'dark',
  gradient: true,
  tokens: {
    ...semanticDark,
    'bg-page': '#070D1A',
    'bg-surface': '#0F172A',
    'bg-subtle': '#1A2540',
    'bg-hover': '#1F2B4A',
    'bg-selected': '#123B3D',
    'text-primary': '#E6F1FF',
    'text-secondary': '#A9B8D0',
    'text-tertiary': '#8C9DBA',
    'text-on-accent': '#FFFFFF',
    'text-accent': '#5EEAD4',
    'border-default': '#24314F',
    'border-strong': '#34446A',
    'border-focus': '#5EEAD4',
    'accent-default': '#0F766E',
    'accent-hover': '#115E59',
  },
  shadows: { ...shadowValues.dark, card: auroraCard },
  page: '#070D1A',
  content: 'transparent',
  surfaceFilter: 'blur(24px)',
  surfaces: {
    chrome: alpha('#0F172A', 0.62),
    card: alpha('#0F172A', 0.82),
    column: alpha('#0F172A', 0.35),
    overlay: alpha('#0F172A', 0.97),
    cardShadow: auroraCard,
  },
  decor: () => import('./decor/aurora'),
  illustrations: { notFound: picture(aurora404), error: picture(aurora500), empty: picture(auroraEmpty, 160, 100) },
} satisfies ThemeDefinition<'aurora'>;

const sunsetCard = '0 2px 8px rgba(154, 52, 18, 0.1)';

/** A warm evening sky behind frosted white. Values from the Penpot page "Themes / Sunset". */
export const sunset = {
  id: 'sunset',
  category: 'gradient',
  base: 'light',
  gradient: true,
  tokens: {
    ...semanticLight,
    'bg-page': '#FFF1E8',
    'bg-surface': '#FFFFFF',
    'bg-subtle': '#FDEDE4',
    'bg-hover': '#FBE4D8',
    'bg-selected': '#FFE6D2',
    'text-primary': '#2A1A2E',
    'text-secondary': '#5A4560',
    'text-tertiary': '#72587A',
    'text-on-accent': '#FFFFFF',
    'text-accent': '#9A3412',
    'border-default': '#F3D9CC',
    'border-strong': '#E8C3B1',
    'border-focus': '#C2410C',
    'accent-default': '#C2410C',
    'accent-hover': '#9A3412',
    'status-todo-bg': '#F6E3D9',
    'status-todo-text': '#5A4560',
  },
  // Shadows are tinted orange instead of grey.
  shadows: {
    drawer: '0 12px 40px rgba(154, 52, 18, 0.18)',
    card: sunsetCard,
    popover: '0 4px 16px rgba(154, 52, 18, 0.14)',
    dialog: '0 8px 24px rgba(154, 52, 18, 0.18)',
  },
  page: 'linear-gradient(180deg, #FFD3B0 0%, #FFC4D3 55%, #E3D3FF 100%)',
  content: 'transparent',
  surfaceFilter: 'blur(20px)',
  surfaces: {
    chrome: alpha('#FFFFFF', 0.74),
    card: alpha('#FFFFFF', 0.9),
    column: alpha('#FFFFFF', 0.32),
    overlay: alpha('#FFFFFF', 0.98),
    cardShadow: sunsetCard,
  },
  decor: () => import('./decor/sunset'),
  illustrations: { notFound: picture(sunset404), error: picture(sunset500), empty: picture(sunsetEmpty, 160, 100) },
} satisfies ThemeDefinition<'sunset'>;
