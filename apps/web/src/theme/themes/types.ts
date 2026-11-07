import type { ComponentType } from 'react';
import type { Accent, AccentTokens, ColorMode, SemanticToken, ShadowSet } from '../tokens';

export type ThemeCategory = 'basic' | 'gradient' | 'element' | 'culture';

/** Full-page and empty-state pictures a theme may draw in its own style. */
export type IllustrationKind = 'notFound' | 'error' | 'empty';

export interface DecorProps {
  /** Draw one quiet frame: the person or their device asked for less motion. */
  still: boolean;
  /** The tab is hidden; canvas loops should stop until it shows again. */
  paused: boolean;
}

/**
 * Everything a theme changes. Components keep reading the semantic tokens, so a theme reaches every screen without
 * touching it; the optional parts add what colours alone cannot: a page backdrop, frosted surfaces, a heading font,
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
  /** font-family for headings. Body text stays SVN-Poppins. The font must carry Vietnamese glyphs. */
  headingFont?: string;
  /** Marks the theme as a gradient one in the gallery. */
  gradient?: boolean;
  /** The animated layer behind the app, loaded only while the theme is in use. */
  decor?: () => Promise<{ default: ComponentType<DecorProps> }>;
  illustrations?: Partial<Record<IllustrationKind, ComponentType>>;
}
