// Values mirror the Penpot token sets "primitive" and "semantic/light".
// Components use semantic tokens only, so a dark set can be added without touching them.

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
  green100: '#DCFCE7',
  green700: '#15803D',
  green800: '#166534',
  amber100: '#FEF3C7',
  amber800: '#92400E',
  red100: '#FEE2E2',
  red700: '#B91C1C',
  red800: '#991B1B',
  violet100: '#EDE9FE',
  violet600: '#7C3AED',
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
} as const;

export type SemanticToken = keyof typeof semanticLight;

/** CSS variable reference for a semantic token. */
export const sem = (token: SemanticToken) => `var(--sb-${token})`;

export const space = { 4: 4, 8: 8, 12: 12, 16: 16, 24: 24, 32: 32 } as const;

export const radius = { control: 6, card: 8, full: 999 } as const;

export const font = {
  family: '"SVN-Poppins", sans-serif',
  mono: '"Source Code Pro", monospace',
  size: { xs: 12, md: 14, lg: 16, xl: 20 },
  weight: { regular: 400, medium: 500, semibold: 600 },
  // Vietnamese double diacritics clip below 1.4.
  lineHeight: 1.4,
} as const;

export const shadow = {
  drawer: 'rgba(100, 100, 111, 0.2) 0px 7px 29px 0px',
  card: 'rgba(67, 71, 85, 0.27) 0px 0px 0.25em, rgba(90, 125, 188, 0.05) 0px 0.25em 1em',
  popover: '0 4px 16px rgba(17, 24, 39, 0.12)',
  dialog: '0 8px 24px rgba(17, 24, 39, 0.16)',
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
