import { useTheme } from '@mui/material/styles';
import useMediaQuery from '@mui/material/useMediaQuery';

// The theme comes from @mui/material, which falls back to the default theme outside a ThemeProvider, as in unit tests.

/** Below md the layout is for phones and tablets: side menus become drawers, popovers become sheets. */
export function useCompact() {
  const theme = useTheme();
  return useMediaQuery(theme.breakpoints.down('md'), { noSsr: true });
}

/** Below sm, a phone held upright: dialogs take the whole screen. */
export function usePhone() {
  const theme = useTheme();
  return useMediaQuery(theme.breakpoints.down('sm'), { noSsr: true });
}
