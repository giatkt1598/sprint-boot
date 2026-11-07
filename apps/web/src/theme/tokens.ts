// Values mirror the Penpot token sets "primitive" and "semantic/light".
// Components use semantic tokens only, so a theme (theme/themes) can replace every colour without touching them.

export const primitive = {
  white: '#FFFFFF',
  gray50: '#F7F8FA',
  gray100: '#EEF0F3',
  gray200: '#E2E5EA',
  gray300: '#CDD2DA',
  gray500: '#5F6B7A',
  gray600: '#4B5563',
  gray700: '#374151',
  gray900: '#172033',
  blue50: '#EFF5FF',
  blue100: '#DBEAFE',
  blue600: '#2563EB',
  blue700: '#1D4ED8',
  blue800: '#1E40AF',
  green50: '#F0FDF4',
  green100: '#DCFCE7',
  green700: '#15803D',
  green800: '#166534',
  amber100: '#FEF3C7',
  amber800: '#92400E',
  red100: '#FEE2E2',
  red700: '#B91C1C',
  red800: '#991B1B',
  violet50: '#F5F3FF',
  violet100: '#EDE9FE',
  violet600: '#7C3AED',
  violet700: '#6D28D9',
  violet800: '#5B21B6',
} as const;

const p = primitive;

export const semanticLight = {
  'bg-page': p.gray50,
  'bg-surface': p.white,
  'bg-subtle': p.gray100,
  'bg-hover': p.gray100,
  'bg-selected': p.blue50,
  'text-primary': p.gray900,
  'text-secondary': p.gray600,
  'text-tertiary': p.gray500,
  'text-on-accent': p.white,
  'text-accent': p.blue700,
  'border-default': p.gray200,
  'border-strong': p.gray300,
  'border-focus': p.blue700,
  'accent-default': p.blue600,
  'accent-hover': p.blue700,
  'status-todo-bg': p.gray200,
  'status-todo-text': p.gray700,
  'status-progress-bg': p.blue100,
  'status-progress-text': p.blue800,
  'status-done-bg': p.green100,
  'status-done-text': p.green800,
  'status-merged-bg': p.violet100,
  'status-merged-text': p.violet800,
  'feedback-success-bg': p.green100,
  'feedback-success-text': p.green800,
  'feedback-warning-bg': p.amber100,
  'feedback-warning-text': p.amber800,
  'feedback-danger-bg': p.red100,
  'feedback-danger-text': p.red700,
  'feedback-danger-hover': p.red800,
  'type-story': p.green700,
  'type-task': p.blue600,
  'type-bug': p.red700,
  'type-subtask': p.violet600,
  'type-epic': p.violet800,
  'bar-done': p.green800,
  'feedback-danger-solid': p.red700,
} as const;

export type SemanticToken = keyof typeof semanticLight;

/** The dark set keeps every token of the light one, so components never need to know the mode. */
export const semanticDark: Record<SemanticToken, string> = {
  'bg-page': '#0F141C',
  'bg-surface': '#161C27',
  'bg-subtle': '#1E2633',
  'bg-hover': '#222B3A',
  'bg-selected': '#1B2A44',
  'text-primary': '#E6E9EF',
  'text-secondary': '#A8B0BD',
  'text-tertiary': '#8B94A3',
  'text-on-accent': '#FFFFFF',
  'text-accent': '#7CB0FF',
  'border-default': '#2A3342',
  'border-strong': '#3A4557',
  'border-focus': '#60A5FA',
  'accent-default': '#2563EB',
  'accent-hover': '#3B82F6',
  'status-todo-bg': '#2A3342',
  'status-todo-text': '#C5CBD6',
  'status-progress-bg': '#1B2E57',
  'status-progress-text': '#93BBFD',
  'status-done-bg': '#14352A',
  'status-done-text': '#86EFAC',
  'status-merged-bg': '#2E2350',
  'status-merged-text': '#C4B5FD',
  'feedback-success-bg': '#14352A',
  'feedback-success-text': '#86EFAC',
  'feedback-warning-bg': '#3A2D12',
  'feedback-warning-text': '#FCD34D',
  'feedback-danger-bg': '#3F1D1D',
  'feedback-danger-text': '#FCA5A5',
  'feedback-danger-hover': '#991B1B',
  'type-story': '#4ADE80',
  'type-task': '#60A5FA',
  'type-bug': '#F87171',
  'type-subtask': '#A78BFA',
  'type-epic': '#C4B5FD',
  'bar-done': '#15803D',
  'feedback-danger-solid': '#B91C1C',
};

export type Accent = 'blue' | 'green' | 'violet';
export type ColorMode = 'light' | 'dark';

/** What an accent changes: only the colours that mark the interactive and the selected. */
export type AccentTokens = Pick<Record<SemanticToken, string>, 'accent-default' | 'accent-hover' | 'text-accent' | 'border-focus' | 'bg-selected'>;

export const accents: Record<Accent, Record<ColorMode, AccentTokens>> = {
  blue: {
    light: { 'accent-default': p.blue600, 'accent-hover': p.blue700, 'text-accent': p.blue700, 'border-focus': p.blue700, 'bg-selected': p.blue50 },
    dark: { 'accent-default': '#2563EB', 'accent-hover': '#3B82F6', 'text-accent': '#7CB0FF', 'border-focus': '#60A5FA', 'bg-selected': '#1B2A44' },
  },
  green: {
    light: { 'accent-default': p.green700, 'accent-hover': p.green800, 'text-accent': p.green800, 'border-focus': p.green700, 'bg-selected': p.green50 },
    dark: { 'accent-default': '#15803D', 'accent-hover': '#16A34A', 'text-accent': '#86EFAC', 'border-focus': '#4ADE80', 'bg-selected': '#14301F' },
  },
  violet: {
    light: { 'accent-default': p.violet600, 'accent-hover': p.violet700, 'text-accent': p.violet800, 'border-focus': p.violet700, 'bg-selected': p.violet50 },
    dark: { 'accent-default': '#7C3AED', 'accent-hover': '#8B5CF6', 'text-accent': '#C4B5FD', 'border-focus': '#A78BFA', 'bg-selected': '#2A2147' },
  },
};

/** CSS variable reference for a semantic token. */
export const sem = (token: SemanticToken) => `var(--sb-${token})`;

export const space = { 4: 4, 8: 8, 12: 12, 16: 16, 24: 24, 32: 32 } as const;

export const radius = { control: 6, card: 8, full: 999 } as const;

export const font = {
  /** --sb-font-family and --sb-font-scale are set from the person's preferences. */
  family: 'var(--sb-font-family)',
  mono: '"Source Code Pro", monospace',
  size: {
    xs: 'calc(12px * var(--sb-font-scale, 1))',
    md: 'calc(14px * var(--sb-font-scale, 1))',
    lg: 'calc(16px * var(--sb-font-scale, 1))',
    xl: 'calc(20px * var(--sb-font-scale, 1))',
  },
  weight: { regular: 400, medium: 500, semibold: 600 },
  // Vietnamese double diacritics clip below 1.4.
  lineHeight: 1.4,
} as const;

export type ShadowSet = Record<'drawer' | 'card' | 'popover' | 'dialog', string>;

/** The values for a light and a dark page; components use `shadow`, which reads the variable the theme sets. */
export const shadowValues: Record<ColorMode, ShadowSet> = {
  light: {
    drawer: 'rgba(100, 100, 111, 0.2) 0px 7px 29px 0px',
    card: 'rgba(67, 71, 85, 0.27) 0px 0px 0.25em, rgba(90, 125, 188, 0.05) 0px 0.25em 1em',
    popover: '0 4px 16px rgba(17, 24, 39, 0.12)',
    dialog: '0 8px 24px rgba(17, 24, 39, 0.16)',
  },
  dark: {
    drawer: 'rgba(0, 0, 0, 0.5) 0px 7px 29px 0px',
    card: 'rgba(0, 0, 0, 0.5) 0px 0px 0.25em, rgba(0, 0, 0, 0.25) 0px 0.25em 1em',
    popover: '0 4px 16px rgba(0, 0, 0, 0.5)',
    dialog: '0 8px 24px rgba(0, 0, 0, 0.6)',
  },
};

export const shadow = {
  drawer: 'var(--sb-shadow-drawer)',
  card: 'var(--sb-shadow-card)',
  popover: 'var(--sb-shadow-popover)',
  dialog: 'var(--sb-shadow-dialog)',
} as const;

/**
 * A block of content that edits in place, while hovered: a faint wash in most themes, a pane like the cards in a glass one,
 * with the theme's sheen, edge and frost.
 */
export const hoverPane = {
  backgroundColor: 'var(--sb-hover-bg)',
  backgroundImage: 'var(--sb-surface-sheen)',
  boxShadow: 'var(--sb-hover-edge)',
  backdropFilter: 'var(--sb-card-filter)',
} as const;

export const motion = { fast: '150ms', slow: '200ms' } as const;

export const size = {
  control: 32,
  input: 36,
  menuItem: 36,
  row: 36,
  topBar: 48,
  sidebar: 216,
} as const;

/** Colors people can pick inside rich text. Stored in the HTML, so they stay plain values. */
export const editorColors = {
  text: [
    { name: 'default', value: null },
    { name: 'red', value: p.red700 },
    { name: 'amber', value: p.amber800 },
    { name: 'green', value: p.green800 },
    { name: 'blue', value: p.blue700 },
  ],
  highlight: [
    { name: 'none', value: null },
    { name: 'red', value: p.red100 },
    { name: 'amber', value: p.amber100 },
    { name: 'green', value: p.green100 },
    { name: 'blue', value: p.blue100 },
  ],
};

/**
 * Sign-in buttons of other companies follow their brand rules, not ours: these values come from
 * the Google Identity branding guidelines and GitHub's Primer, and never change with the theme.
 */
export const brand = {
  /** Every sign-in button shares Google's 40px, so a stack of providers lines up. */
  signInHeight: 40,
  google: {
    background: '#FFFFFF',
    hover: '#F2F2F2',
    border: '#747775',
    text: '#1F1F1F',
    radius: 4,
  },
  github: {
    background: '#24292F',
    hover: '#32383F',
    border: '#24292F',
    text: '#FFFFFF',
    radius: 6,
  },
} as const;
