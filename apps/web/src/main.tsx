import CssBaseline from '@mui/material/CssBaseline';
import GlobalStyles from '@mui/material/GlobalStyles';
import { StyledEngineProvider, ThemeProvider } from '@mui/material/styles';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StrictMode, useEffect, useLayoutEffect, useMemo, type ReactNode } from 'react';
import { createRoot } from 'react-dom/client';
import { ApiError } from './api';
import { App } from './App';
import './i18n';
import './index.css';
import { applyAppearance, resolveMode, useAppearance, watchSystemMode } from './theme/appearance';
import { createAppTheme } from './theme/theme';

const MAX_RETRIES = 3;

// A missing or forbidden answer stays that way, so only server and network failures are asked again.
const queryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: (count, failure) => !(failure instanceof ApiError && failure.status < 500) && count < MAX_RETRIES },
  },
});

/** The theme follows the person's mode and accent; the rest of their choices reach the page as attributes on <html>. */
function ThemeRoot({ children }: { children: ReactNode }) {
  const appearance = useAppearance();
  const mode = resolveMode(appearance.colorMode);
  const theme = useMemo(() => createAppTheme(mode, appearance.accent), [mode, appearance.accent]);
  useLayoutEffect(() => applyAppearance(appearance), [appearance]);
  useEffect(() => watchSystemMode(), []);

  return <ThemeProvider theme={theme}>{children}</ThemeProvider>;
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
