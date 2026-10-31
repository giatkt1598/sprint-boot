import { createTheme } from '@mui/material/styles';
import { font, motion, primitive, radius, sem, semanticLight, shadow, size } from './tokens';

const focusRing = {
  outline: `2px solid ${sem('border-focus')}`,
  outlineOffset: 2,
};

export const semanticCssVariables = Object.fromEntries(
  Object.entries(semanticLight).map(([token, value]) => [`--sb-${token}`, value]),
);

export const theme = createTheme({
  palette: {
    primary: { main: primitive.blue600, dark: primitive.blue700, contrastText: primitive.white },
    error: { main: primitive.red700, dark: primitive.red800 },
    warning: { main: primitive.amber800 },
    success: { main: primitive.green800 },
    text: { primary: primitive.gray900, secondary: primitive.gray600 },
    background: { default: primitive.gray50, paper: primitive.white },
    divider: primitive.gray200,
  },
  shape: { borderRadius: radius.control },
  spacing: 4,
  shadows: Array(25).fill('none') as never,
  typography: {
    fontFamily: font.family,
    fontSize: font.size.md,
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
        ':root': semanticCssVariables,
        body: { backgroundColor: sem('bg-page'), color: sem('text-primary') },
        'button, input, select, textarea': { fontFamily: 'inherit' },
        '*:focus-visible': focusRing,
      },
    },
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
        paper: { borderRadius: radius.card, boxShadow: shadow.dialog, margin: 16 },
      },
    },
    MuiPopover: {
      styleOverrides: {
        paper: {
          borderRadius: radius.card,
          boxShadow: shadow.popover,
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
