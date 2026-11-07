import { createTheme } from '@mui/material/styles';
import { accents, font, motion, radius, sem, semanticDark, semanticLight, shadowValues, size, type Accent, type ColorMode, type SemanticToken } from './tokens';

const focusRing = {
  outline: `2px solid ${sem('border-focus')}`,
  outlineOffset: 2,
};

const SYSTEM_FONT = 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif';

const variables = (tokens: Record<string, string>) => Object.fromEntries(Object.entries(tokens).map(([token, value]) => [`--sb-${token}`, value]));
const shadowVariables = (mode: ColorMode) => variables(Object.fromEntries(Object.entries(shadowValues[mode]).map(([name, value]) => [`shadow-${name}`, value])));

/** Every colour of one mode, with an accent laid over the base set. */
export const semanticFor = (mode: ColorMode, accent: Accent): Record<SemanticToken, string> => ({
  ...(mode === 'dark' ? semanticDark : semanticLight),
  ...accents[accent][mode],
});

/**
 * The variables the whole app reads. The root carries light blue; attributes the preferences set on <html> switch
 * the mode (data-theme), the accent (data-accent) and the font (data-font), so a change never rebuilds a component.
 */
export const cssVariables = () => {
  const blocks: Record<string, Record<string, string>> = {
    ':root': {
      colorScheme: 'light',
      '--sb-font-family': '"SVN-Poppins", sans-serif',
      ...variables(semanticFor('light', 'blue')),
      ...shadowVariables('light'),
    },
    ':root[data-font="system"]': { '--sb-font-family': SYSTEM_FONT },
    ':root[data-theme="dark"]': { colorScheme: 'dark', ...variables(semanticFor('dark', 'blue')), ...shadowVariables('dark') },
  };
  for (const accent of ['green', 'violet'] as const) {
    blocks[`:root[data-accent="${accent}"]`] = variables(accents[accent].light);
    blocks[`:root[data-theme="dark"][data-accent="${accent}"]`] = variables(accents[accent].dark);
  }
  return blocks;
};

/** MUI needs real colours for its own components, so the palette follows the mode and accent while the app reads variables. */
export function createAppTheme(mode: ColorMode, accent: Accent) {
  const colors = semanticFor(mode, accent);

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
      h1: { fontSize: font.size.xl, fontWeight: font.weight.semibold },
      h2: { fontSize: font.size.lg, fontWeight: font.weight.semibold },
      h3: { fontSize: font.size.md, fontWeight: font.weight.semibold },
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
          body: { backgroundColor: sem('bg-page'), color: sem('text-primary') },
          'button, input, select, textarea': { fontFamily: 'inherit' },
          '*:focus-visible': focusRing,
        },
      },
      // MUI lightens a raised surface in dark mode; ours are flat and take their colour from the tokens.
      MuiPaper: { styleOverrides: { root: { backgroundImage: 'none' } } },
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
            backgroundColor: sem('bg-surface'),
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
