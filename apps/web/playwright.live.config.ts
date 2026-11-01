import { defineConfig } from '@playwright/test';

/** End-to-end flows against a running `pnpm dev`. */
export default defineConfig({
  testDir: 'e2e-live',
  workers: 1,
  use: { baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5173', locale: 'en', contextOptions: { reducedMotion: 'reduce' }, screenshot: 'only-on-failure' },
});
