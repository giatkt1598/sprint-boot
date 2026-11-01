import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    // Keep the browser's host so the auth server issues same-origin URLs and cookies.
    proxy: Object.fromEntries(
      ['/api', '/connect', '/.well-known'].map((path) => [
        path,
        { target: 'http://localhost:5080', headers: { host: 'localhost:5173' } },
      ]),
    ),
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
    css: false,
  },
});
