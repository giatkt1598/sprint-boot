import CssBaseline from '@mui/material/CssBaseline';
import GlobalStyles from '@mui/material/GlobalStyles';
import { StyledEngineProvider, ThemeProvider } from '@mui/material/styles';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StrictMode, useLayoutEffect, useMemo, type ReactNode } from 'react';
import { createRoot } from 'react-dom/client';
import { ApiError } from './api';
import { App } from './App';
import './i18n';
import './index.css';
import { applyAppearance, useAppearance } from './theme/appearance';
import { createAppTheme } from './theme/theme';
import { ThemeDecor } from './theme/ThemeDecor';

const MAX_RETRIES = 3;

// A missing or forbidden answer stays that way, so only server and network failures are asked again.
const queryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: (count, failure) => !(failure instanceof ApiError && failure.status < 500) && count < MAX_RETRIES },
  },
});

/** MUI follows the person's theme and accent; the rest of their choices reach the page as attributes on <html>. */
function ThemeRoot({ children }: { children: ReactNode }) {
  const appearance = useAppearance();
  const theme = useMemo(() => createAppTheme(appearance.theme, appearance.accent), [appearance.theme, appearance.accent]);
  useLayoutEffect(() => applyAppearance(appearance), [appearance]);

  return (
    <ThemeProvider theme={theme}>
      <ThemeDecor />
      {children}
    </ThemeProvider>
  );
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <StyledEngineProvider enableCssLayer>
      <GlobalStyles styles="@layer theme, base, mui, components, utilities;" />
      <ThemeRoot>
        <CssBaseline />
        <QueryClientProvider client={queryClient}>
          <App />
        </QueryClientProvider>
      </ThemeRoot>
    </StyledEngineProvider>
  </StrictMode>,
);
