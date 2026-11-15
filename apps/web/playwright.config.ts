import { defineConfig } from '@playwright/test';

/** The landing page is on at 4173, the default address, and off at 4174; both serve the same build. */
export const LANDING_OFF_URL = 'http://localhost:4174';

export default defineConfig({
  testDir: 'e2e',
  use: { baseURL: 'http://localhost:4173', contextOptions: { reducedMotion: 'reduce' } },
  // Started one after the other, so the second preview finds the build of the first.
  webServer: [
    {
      command: 'pnpm build && pnpm preview',
      url: 'http://localhost:4173',
      env: { LANDING_PAGE_ENABLED: 'true' },
      reuseExistingServer: true,
      timeout: 180_000,
    },
    {
      command: 'npx vite preview --port 4174 --strictPort',
      url: LANDING_OFF_URL,
      env: { LANDING_PAGE_ENABLED: 'false' },
      reuseExistingServer: true,
      timeout: 60_000,
    },
  ],
});
