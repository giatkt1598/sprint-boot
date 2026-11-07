import { createTheme } from '@mui/material/styles';
import { FONT_CHOICES, FONT_FAMILIES } from './fonts';
import { THEMES, themeById, type ThemeDefinition } from './themes';
import { font, motion, radius, sem, size, type Accent, type SemanticToken } from './tokens';

const focusRing = {
  outline: `2px solid ${sem('border-focus')}`,
  outlineOffset: 2,
};

const variables = (tokens: Record<string, string>) => Object.fromEntries(Object.entries(tokens).map(([token, value]) => [`--sb-${token}`, value]));

/** Every colour of a theme, with the accent laid over it when the theme offers accents. */
export const themeColors = (theme: ThemeDefinition, accent: Accent): Record<SemanticToken, string> => ({
  ...theme.tokens,
  ...theme.accents?.[accent],
});

/** What a theme sets besides colours: shadows, the page behind the app, the content area and its surfaces. */
const themeVariables = (theme: ThemeDefinition) => ({
  colorScheme: theme.base,
  ...variables(theme.tokens),
  ...variables(Object.fromEntries(Object.entries(theme.shadows).map(([name, value]) => [`shadow-${name}`, value]))),
  '--sb-page-bg': theme.page ?? 'var(--sb-bg-page)',
  '--sb-content-bg': theme.content ?? 'var(--sb-bg-surface)',
  '--sb-surface-filter': theme.surfaceFilter ?? 'none',
  '--sb-chrome-bg': theme.surfaces?.chrome ?? 'var(--sb-bg-surface)',
  '--sb-card-bg': theme.surfaces?.card ?? 'var(--sb-bg-surface)',
  '--sb-column-bg': theme.surfaces?.column ?? 'var(--sb-bg-page)',
  '--sb-overlay-bg': theme.surfaces?.overlay ?? 'var(--sb-bg-surface)',
  '--sb-panel-bg': theme.surfaces?.panel ?? 'transparent',
  '--sb-card-filter': theme.surfaces?.cardFilter ?? 'none',
  '--sb-surface-sheen': theme.surfaces?.sheen ?? 'none',
  '--sb-surface-edge': theme.surfaces?.edge ?? 'none',
  // A faint wash of the text colour: darker on a light theme, lighter on a dark one, and see-through over any backdrop.
  '--sb-top-bar-shadow': theme.surfaces?.topBar ?? 'var(--sb-surface-edge)',
  '--sb-hover-edge': theme.surfaces?.hoverEdge ?? 'none',
  '--sb-hover-bg': theme.surfaces?.hover ?? 'color-mix(in srgb, var(--sb-text-primary) 6%, transparent)',
  '--sb-input-bg': theme.surfaces?.input ?? 'var(--sb-bg-surface)',
  '--sb-subtle-field-bg': theme.surfaces?.subtleField ?? 'var(--sb-bg-subtle)',
  '--sb-board-card-shadow': theme.surfaces?.cardShadow ?? 'none',
});

/**
 * The variables the whole app reads. Attributes the preferences set on <html> pick the theme (data-app-theme), the
 * accent (data-accent) and the font (data-font), so a change never rebuilds a component. The font is the person's
 * choice, not the theme's, so it lives outside the theme blocks. Every theme block also
 * matches any element carrying the attribute, so a gallery card can show a theme other than the one in use.
 */
export const cssVariables = () => {
  const blocks: Record<string, Record<string, string>> = {
    ':root': {
      '--sb-font-family': FONT_FAMILIES.poppins.body,
      '--sb-heading-font': FONT_FAMILIES.poppins.heading,
      ...themeVariables(themeById('classic')),
    },
  };
  for (const choice of FONT_CHOICES.filter((choice) => choice !== 'poppins')) {
    blocks[`:root[data-font="${choice}"]`] = { '--sb-font-family': FONT_FAMILIES[choice].body, '--sb-heading-font': FONT_FAMILIES[choice].heading };
  }
  for (const theme of THEMES) {
    blocks[`:root[data-app-theme="${theme.id}"], [data-app-theme="${theme.id}"]`] = themeVariables(theme);
  }
  // After the themes, so an accent wins over the blue a theme block sets.
  for (const theme of THEMES) {
    for (const [accent, tokens] of Object.entries((theme as ThemeDefinition).accents ?? {})) {
      if (accent !== 'blue') {
        blocks[`:root[data-app-theme="${theme.id}"][data-accent="${accent}"], [data-app-theme="${theme.id}"][data-accent="${accent}"]`] = variables(tokens);
      }
    }
  }
  return blocks;
};

/** MUI needs real colours for its own components, so the palette follows the theme and accent while the app reads variables. */
export function createAppTheme(themeId: string, accent: Accent) {
  const theme = themeById(themeId);
  const colors = themeColors(theme, accent);
  const mode = theme.base;
  const heading = { fontFamily: 'var(--sb-heading-font)' };

  return createTheme({
    palette: {
      mode,
      // Links read this colour, so it is the accent that stays readable as text on the page.
      primary: { main: colors['text-accent'], dark: colors['accent-hover'], contrastText: colors['text-on-accent'] },
      error: { main: colors['feedback-danger-text'], dark: colors['feedback-danger-hover'] },
      warning: { main: colors['feedback-warning-text'] },
      success: { main: colors['status-done-text'] },
      text: { primary: colors['text-primary'], secondary: colors['text-secondary'] },
      background: { default: colors['bg-page'], paper: colors['bg-surface'] },
      divider: colors['border-default'],
    },
    shape: { borderRadius: radius.control },
    spacing: 4,
    shadows: Array(25).fill('none') as never,
    typography: {
      fontFamily: font.family,
      fontSize: 14,
      fontWeightRegular: font.weight.regular,
      fontWeightMedium: font.weight.medium,
      fontWeightBold: font.weight.semibold,
      allVariants: { lineHeight: font.lineHeight },
      h1: { ...heading, fontSize: font.size.xl, fontWeight: font.weight.semibold },
      h2: { ...heading, fontSize: font.size.lg, fontWeight: font.weight.semibold },
      h3: { ...heading, fontSize: font.size.md, fontWeight: font.weight.semibold },
      body1: { fontSize: font.size.md },
      body2: { fontSize: font.size.xs },
      button: { fontSize: font.size.md, fontWeight: font.weight.medium, textTransform: 'none' },
    },
    transitions: {
      duration: { shortest: 150, shorter: 150, short: 150, standard: 200, complex: 200 },
    },
    components: {
      MuiCssBaseline: {
        styleOverrides: {
          ...cssVariables(),
          body: { background: 'var(--sb-page-bg)', backgroundAttachment: 'fixed', color: sem('text-primary') },
          'button, input, select, textarea': { fontFamily: 'inherit' },
          '*:focus-visible': focusRing,
        },
      },
      // MUI lightens a raised surface in dark mode; ours are flat and take their colour from the tokens.
      // Every paper floats over the page, so a see-through theme lets its backdrop through by the overlay amount.
      MuiPaper: { styleOverrides: { root: { backgroundImage: 'var(--sb-surface-sheen)', backgroundColor: 'var(--sb-overlay-bg)', backdropFilter: 'var(--sb-surface-filter)' } } },
      MuiButtonBase: { defaultProps: { disableRipple: true } },
      MuiButton: {
        defaultProps: { disableElevation: true },
        styleOverrides: {
          root: {
            minHeight: size.control,
            padding: '4px 12px',
            transition: `background-color ${motion.fast}`,
            '&.Mui-disabled': { opacity: 0.5 },
          },
        },
      },
      MuiOutlinedInput: {
        styleOverrides: {
          root: {
            minHeight: size.input,
            backgroundColor: 'var(--sb-input-bg)',
            '& .MuiOutlinedInput-notchedOutline': { borderColor: sem('border-strong') },
            '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: sem('border-strong') },
            '&.Mui-focused .MuiOutlinedInput-notchedOutline': { borderColor: sem('border-focus'), borderWidth: 2 },
            '&.Mui-error .MuiOutlinedInput-notchedOutline': { borderColor: sem('feedback-danger-text') },
          },
          input: { padding: '8px 12px', fontSize: font.size.md, lineHeight: font.lineHeight, height: 'auto' },
        },
      },
      MuiDialog: {
        styleOverrides: {
          paper: { borderRadius: radius.card, boxShadow: 'var(--sb-shadow-dialog)', margin: 16 },
        },
      },
      MuiPopover: {
        styleOverrides: {
          paper: {
            borderRadius: radius.card,
            boxShadow: 'var(--sb-shadow-popover)',
            border: `1px solid ${sem('border-default')}`,
            padding: 8,
          },
        },
      },
      MuiTab: {
        styleOverrides: {
          root: { minHeight: size.control, padding: '4px 12px', minWidth: 0 },
        },
      },
      MuiTabs: { styleOverrides: { root: { minHeight: size.control } } },
      MuiTable: { styleOverrides: { root: { backgroundColor: 'var(--sb-panel-bg)', backdropFilter: 'var(--sb-card-filter)', borderRadius: radius.card } } },
      MuiTableCell: {
        styleOverrides: {
          root: { padding: '6px 8px', height: size.row, fontSize: font.size.md, borderColor: sem('border-default') },
          head: { fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-tertiary') },
        },
      },
      MuiLink: { defaultProps: { underline: 'hover' } },
    },
  });
}
