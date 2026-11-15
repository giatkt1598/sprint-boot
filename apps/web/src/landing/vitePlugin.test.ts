import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import type { IncomingMessage, ServerResponse } from 'node:http';
import { describe, expect, it } from 'vitest';
import en from '../i18n/locales/en.json';
import { breakpoint, semanticDark, semanticLight } from '../theme/tokens';
import { renderLanding, resolveBreakpoints, rootPageMiddleware, tokenStyles } from './vitePlugin';

const page = readFileSync(resolve(__dirname, '../../welcome.html'), 'utf8');

describe('landing page build', () => {
  const html = renderLanding(page);

  it('fills every string, icon and token placeholder', () => {
    expect(html).not.toMatch(/\{\{|<!--tokens-->|<!--strings-->/);
    expect(html).toContain(`<h1 id="hero-title" data-i18n="hero.title">${en.landing.hero.title}</h1>`);
    expect(html).toContain(`content="${en.landing.description}"`);
    expect(html).toMatch(/<svg[^>]*class="lucide/);
  });

  it('writes no visible text of its own: every text node comes from the locale files', () => {
    const text = html
      .replace(/<!--[\s\S]*?-->/g, '')
      .replace(/<(script|style|svg)\b[\s\S]*?<\/\1>/g, '')
      .replace(/<(\w+)\b[^>]*\sdata-i18n="[^"]+"[^>]*>[^<]*<\/\1>/g, '')
      .replace(/<[^>]+>/g, '')
      .trim();
    expect(text).toBe('');
  });

  it('takes colours from the tokens, with the dark set for dark devices', () => {
    const styles = tokenStyles();
    expect(styles).toContain(`--sb-accent-default:${semanticLight['accent-default']}`);
    expect(styles).toMatch(new RegExp(`prefers-color-scheme:dark\\)\\{:root\\{.*--sb-bg-page:${semanticDark['bg-page']}`));
  });

  it('turns bp(name) into the breakpoint of the tokens', () => {
    expect(resolveBreakpoints('@media (min-width: bp(md)) {}')).toBe(`@media (min-width: ${breakpoint.md}px) {}`);
  });

  it('keeps the inline script small and loads no other script', () => {
    const scripts = [...html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/g)];
    const code = scripts.filter(([, attributes]) => !attributes.includes('application/json'));
    expect(code.every(([, attributes]) => !attributes.includes('src'))).toBe(true);
    expect(code.reduce((total, [, , body]) => total + body.length, 0)).toBeLessThan(5 * 1024);
  });
});

describe('root page middleware', () => {
  const run = (enabled: boolean, url: string, cookie?: string) => {
    const req = { method: 'GET', url, headers: { cookie } } as IncomingMessage;
    const headers: Record<string, string> = {};
    const res = { setHeader: (name: string, value: string) => (headers[name] = value) } as unknown as ServerResponse;
    rootPageMiddleware(() => enabled)(req, res, () => undefined);
    return { url: req.url, headers };
  };

  it('serves the landing page to guests at / while it is on', () => {
    expect(run(true, '/').url).toBe('/welcome.html');
    expect(run(true, '/?utm=1').url).toBe('/welcome.html');
    expect(run(true, '/', 'other=1').url).toBe('/welcome.html');
  });

  it('serves the app to people with the hint cookie, when it is off, and at every other path', () => {
    expect(run(true, '/', 'sb_signed_in=1').url).toBe('/');
    expect(run(true, '/', 'a=b; sb_signed_in=1').url).toBe('/');
    expect(run(false, '/').url).toBe('/');
    expect(run(true, '/browse/SB-1').url).toBe('/browse/SB-1');
    expect(run(true, '/login').url).toBe('/login');
  });

  it('marks / as private and varying by cookie', () => {
    expect(run(true, '/').headers).toEqual({ 'Cache-Control': 'no-cache, private', Vary: 'Cookie' });
    expect(run(true, '/login').headers).toEqual({});
  });
});
