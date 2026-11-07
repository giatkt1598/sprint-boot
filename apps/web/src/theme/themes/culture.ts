import { semanticDark, semanticLight, shadowValues } from '../tokens';
import chinese404 from './art/chinese-404.svg';
import chinese500 from './art/chinese-500.svg';
import chineseClouds from './art/chinese-clouds.svg';
import european404 from './art/european-404.svg';
import european500 from './art/european-500.svg';
import europeanDamask from './art/european-damask.svg';
import japanese404 from './art/japanese-404.svg';
import japanese500 from './art/japanese-500.svg';
import japaneseWaves from './art/japanese-waves.svg';
import vietnamese404 from './art/vietnamese-404.svg';
import vietnamese500 from './art/vietnamese-500.svg';
import { alpha, picture } from './kit';
import type { ThemeDefinition } from './types';

/** The top bar is 48px tall; rules drawn under it sit at its bottom edge. */
const TOP_BAR = 48;

const chineseCard = '0 1px 0 rgba(184, 134, 11, 0.25), 0 3px 8px rgba(127, 29, 29, 0.1)';

/** Rice paper, gold clouds, vermilion lacquer and red lanterns. Values from the Penpot page "Themes / Chinese". */
export const chinese = {
  id: 'chinese',
  category: 'culture',
  base: 'light',
  tokens: {
    ...semanticLight,
    'bg-page': '#FBF4E8',
    'bg-surface': '#FFFDF8',
    'bg-subtle': '#F6EADB',
    'bg-hover': '#F1E2CF',
    'bg-selected': '#FCE4DE',
    'text-primary': '#2A1612',
    'text-secondary': '#5C3B30',
    'text-tertiary': '#76544A',
    'text-on-accent': '#FFFFFF',
    'text-accent': '#9F1D1D',
    'border-default': '#EAD7B5',
    'border-strong': '#D9BC88',
    'border-focus': '#B91C1C',
    'accent-default': '#B91C1C',
    'accent-hover': '#991B1B',
    'status-todo-bg': '#F1E2CF',
    'status-todo-text': '#5C3B30',
  },
  shadows: {
    drawer: '0 10px 36px rgba(127, 29, 29, 0.2)',
    card: chineseCard,
    popover: '0 4px 16px rgba(127, 29, 29, 0.14)',
    dialog: '0 10px 36px rgba(127, 29, 29, 0.2)',
  },
  // A vermilion lacquer band tops the content, over rice paper tiled with gold xiangyun clouds.
  page: `linear-gradient(#B91C1C, #B91C1C) 0 ${TOP_BAR}px / 100% 6px no-repeat, url("${chineseClouds}") 0 0 / 120px 160px repeat, #FBF4E8`,
  content: 'transparent',
  surfaces: {
    chrome: alpha('#FFFDF8', 0.97),
    card: '#FFFDF8',
    column: alpha('#F6EADB', 0.75),
    overlay: '#FFFDF8',
    // A gold rule along the bottom of the top bar.
    topBar: 'inset 0 -2px 0 #D4A017',
    cardShadow: chineseCard,
  },
  font: 'notoSerif',
  decor: () => import('./decor/chinese'),
  illustrations: { notFound: picture(chinese404), error: picture(chinese500) },
} satisfies ThemeDefinition<'chinese'>;

const japaneseCard = '0 1px 2px rgba(28, 28, 30, 0.08)';

/** Washi paper, sumi ink and indigo, seigaiha waves and falling sakura. Values from the Penpot page "Themes / Japanese". */
export const japanese = {
  id: 'japanese',
  category: 'culture',
  base: 'light',
  tokens: {
    ...semanticLight,
    'bg-page': '#F7F3EA',
    'bg-surface': '#FFFDF8',
    'bg-subtle': '#EFEAE0',
    'bg-hover': '#E9E3D7',
    'bg-selected': '#E3E8F3',
    'text-primary': '#1C1C1E',
    'text-secondary': '#48464A',
    'text-tertiary': '#615E63',
    'text-on-accent': '#FFFFFF',
    'text-accent': '#1E3A6E',
    'border-default': '#E4DCCB',
    'border-strong': '#CFC4AE',
    'border-focus': '#264A7A',
    'accent-default': '#264A7A',
    'accent-hover': '#1E3A6E',
    'status-todo-bg': '#E9E3D7',
    'status-todo-text': '#48464A',
  },
  // Shadows are almost flat.
  shadows: {
    drawer: '0 8px 30px rgba(28, 28, 30, 0.15)',
    card: japaneseCard,
    popover: '0 4px 14px rgba(28, 28, 30, 0.12)',
    dialog: '0 8px 30px rgba(28, 28, 30, 0.16)',
  },
  // A faint red sun behind the header, and seigaiha waves filling the lower part of the page, fading out upward.
  page: `radial-gradient(circle at 428px 110px, rgb(200 16 46 / 0.12) 48px, transparent 49px), url("${japaneseWaves}") left bottom / 1440px 420px repeat-x, #F7F3EA`,
  content: 'transparent',
  surfaces: {
    chrome: alpha('#FFFDF8', 0.96),
    card: '#FFFDF8',
    column: alpha('#FFFDF8', 0.55),
    overlay: '#FFFDF8',
    cardShadow: japaneseCard,
  },
  font: 'notoSerifJp',
  decor: () => import('./decor/japanese'),
  illustrations: { notFound: picture(japanese404), error: picture(japanese500) },
} satisfies ThemeDefinition<'japanese'>;

// Cards carry a faint gold rim.
const vietnameseCard = '0 2px 6px rgba(0, 0, 0, 0.5), 0 0 0 1px rgba(201, 162, 39, 0.12)';

/**
 * Sơn mài lacquer: deep black-brown, cinnabar and gold leaf, with the face of a Đông Sơn bronze drum turning behind the
 * board. Values from the Penpot page "Themes / Vietnamese".
 */
export const vietnamese = {
  id: 'vietnamese',
  category: 'culture',
  base: 'dark',
  tokens: {
    ...semanticDark,
    'bg-page': '#150D09',
    'bg-surface': '#20140F',
    'bg-subtle': '#2C1D16',
    'bg-hover': '#35231A',
    'bg-selected': '#3A1D14',
    'text-primary': '#F6EBDD',
    'text-secondary': '#D8C3AC',
    'text-tertiary': '#B9A18A',
    'text-on-accent': '#FFFFFF',
    'text-accent': '#E9C46A',
    'border-default': '#3B2A1E',
    'border-strong': '#574030',
    'border-focus': '#E9C46A',
    'accent-default': '#B91C1C',
    'accent-hover': '#991B1B',
  },
  shadows: {
    ...shadowValues.dark,
    drawer: '0 10px 40px rgba(0, 0, 0, 0.6)',
    card: vietnameseCard,
  },
  // A warm amber glow on the lacquer; the drum itself turns in the decor.
  page: 'radial-gradient(circle 460px at 50% 62%, rgb(180 83 9 / 0.2), rgb(180 83 9 / 0) 100%), #150D09',
  content: 'transparent',
  surfaceFilter: 'blur(12px)',
  surfaces: {
    chrome: alpha('#20140F', 0.9),
    card: alpha('#20140F', 0.96),
    column: alpha('#20140F', 0.45),
    overlay: alpha('#20140F', 0.98),
    // A gold-leaf rule along the bottom of the top bar.
    topBar: 'inset 0 -1px 0 rgba(201, 162, 39, 0.6)',
    cardShadow: vietnameseCard,
  },
  font: 'alegreya',
  decor: () => import('./decor/vietnamese'),
  illustrations: { notFound: picture(vietnamese404), error: picture(vietnamese500) },
} satisfies ThemeDefinition<'vietnamese'>;

// Cards have a thin gold bottom edge.
const europeanCard = '0 1px 0 rgba(168, 134, 47, 0.3), 0 4px 10px rgba(34, 26, 20, 0.08)';

/**
 * Parchment, a gilded damask wallpaper and burgundy ink, lit through a stained-glass window. Values from the Penpot page
 * "Themes / European".
 */
export const european = {
  id: 'european',
  category: 'culture',
  base: 'light',
  tokens: {
    ...semanticLight,
    'bg-page': '#F4ECDA',
    'bg-surface': '#FFFCF5',
    'bg-subtle': '#F1E8D6',
    'bg-hover': '#EBE0CA',
    'bg-selected': '#F3E1E1',
    'text-primary': '#221A14',
    'text-secondary': '#4F4237',
    'text-tertiary': '#6B5C4F',
    'text-on-accent': '#FFFFFF',
    'text-accent': '#7F1D1D',
    'border-default': '#E6D9BF',
    'border-strong': '#D2BF98',
    'border-focus': '#7F1D1D',
    'accent-default': '#7F1D1D',
    'accent-hover': '#651717',
    'status-todo-bg': '#EBE0CA',
    'status-todo-text': '#4F4237',
  },
  shadows: {
    drawer: '0 10px 36px rgba(34, 26, 20, 0.2)',
    card: europeanCard,
    popover: '0 4px 16px rgba(34, 26, 20, 0.12)',
    dialog: '0 10px 36px rgba(34, 26, 20, 0.2)',
  },
  page: `url("${europeanDamask}") 0 0 / 120px 150px repeat, #F4ECDA`,
  content: 'transparent',
  surfaces: {
    chrome: alpha('#FFFCF5', 0.96),
    card: '#FFFCF5',
    column: alpha('#FFFCF5', 0.42),
    overlay: '#FFFCF5',
    // A gilded double rule along the bottom of the top bar: a fine line, a strip of cream, a stronger line.
    topBar: 'inset 0 -1px 0 rgba(168, 134, 47, 0.6), inset 0 -3px 0 #FFFCF5, inset 0 -4px 0 #A8862F',
    cardShadow: europeanCard,
  },
  font: 'playfair',
  decor: () => import('./decor/european'),
  illustrations: { notFound: picture(european404), error: picture(european500) },
} satisfies ThemeDefinition<'european'>;
