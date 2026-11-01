import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: Number(process.env.WEB_PORT ?? 5173),
    // Keep the browser's host so the auth server issues same-origin URLs and cookies.
    proxy: Object.fromEntries(
      ['/api', '/connect', '/.well-known', '/hubs'].map((path) => [
        path,
        { target: process.env.API_TARGET ?? 'http://localhost:5080', ws: path === '/hubs', headers: { host: `localhost:${process.env.WEB_PORT ?? 5173}` } },
      ]),
    ),
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
    css: false,
  },
});
