import type { ComponentType } from 'react';
import type { FontChoice } from '../fonts';
import type { Accent, AccentTokens, ColorMode, SemanticToken, ShadowSet } from '../tokens';

export type ThemeCategory = 'basic' | 'gradient' | 'element' | 'culture';

/** Full-page pictures a theme may draw in its own style: not found and crash. Empty states never get one. */
export type IllustrationKind = 'notFound' | 'error';

export interface DecorProps {
  /** Draw one quiet frame: the person or their device asked for less motion. */
  still: boolean;
  /** The tab is hidden; canvas loops should stop until it shows again. */
  paused: boolean;
}

/**
 * Backgrounds of the surfaces a see-through theme lets its backdrop through, each by its own amount. Any CSS colour.
 * Left out, chrome, cards and overlays are bg-surface and columns are bg-page, as in the basic themes.
 */
export interface SurfaceSet {
  /** Top bar and sidebar. */
  chrome: string;
  /** Board cards. */
  card: string;
  /** Board columns. */
  column: string;
  /** Drawers, dialogs, menus and popovers: everything that floats over the page. */
  overlay: string;
  /** Tables, which sit straight on the content area. See-through by default; a busy backdrop puts paper under them. */
  panel: string;
  /** Box shadow of board cards. None by default, where cards sit flat on their column. */
  cardShadow: string;
}

/**
 * Everything a theme changes. Components keep reading the semantic tokens, so a theme reaches every screen without
 * touching it; the optional parts add what colours alone cannot: a page backdrop, frosted surfaces, a suggested font,
 * an animated layer behind the app and its own pictures.
 */
export interface ThemeDefinition<Id extends string = string> {
  id: Id;
  category: ThemeCategory;
  /** Whether the theme is light or dark underneath: MUI's palette mode and the browser's color-scheme follow it. */
  base: ColorMode;
  tokens: Record<SemanticToken, string>;
  shadows: ShadowSet;
  /** Themes that let people pick an accent. Blue is the default and must be there. */
  accents?: Record<Accent, AccentTokens>;
  /** CSS background behind the whole app. Defaults to the bg-page colour. */
  page?: string;
  /** Background of the main content area. Defaults to the bg-surface colour; a decor needs it see-through. */
  content?: string;
  /** backdrop-filter of surfaces (papers, sidebar, top bar), such as a blur for frosted glass. */
  surfaceFilter?: string;
  surfaces?: Partial<SurfaceSet>;
  /** The font the theme suggests: choosing the theme picks it, and the person may choose another. Defaults to SVN-Poppins. */
  font?: FontChoice;
  /** Marks the theme as a gradient one in the gallery. */
  gradient?: boolean;
  /** The animated layer behind the app, loaded only while the theme is in use. */
  decor?: () => Promise<{ default: ComponentType<DecorProps> }>;
  illustrations?: Partial<Record<IllustrationKind, ComponentType>>;
}
