import type { IncomingMessage, ServerResponse } from 'node:http';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import type { Plugin } from 'vite';
import { SIGNED_IN_COOKIE } from '../auth/signedInHint.ts';
import { classicIcons, type IconName } from '../components/icons.tsx';
import en from '../i18n/locales/en.json' with { type: 'json' };
import vi from '../i18n/locales/vi.json' with { type: 'json' };
import { breakpoint, font, radius, semanticDark, semanticLight, shadowValues, size, space, motion } from '../theme/tokens.ts';

/**
 * The landing page (welcome.html) is plain HTML and CSS, built from the same sources as the app: colours, type and
 * spacing from theme/tokens.ts, words from the locale files and icons from the Classic set of the icon registry.
 *
 * At `/`, the page goes to guests only while LANDING_PAGE_ENABLED is true; anyone with the `sb_signed_in` hint cookie,
 * which the app sets after sign-in, gets the app. nginx makes this choice in production (nginx/default.conf.template);
 * the middleware below does the same for `vite` and `vite preview`.
 */

const PAGE = 'welcome.html';

type Strings = { [key: string]: string | Strings };

/** "hero.title" → its text, from the landing section of a locale file. */
function flatten(strings: Strings, prefix = ''): Record<string, string> {
  return Object.fromEntries(
    Object.entries(strings).flatMap(([key, value]) =>
      typeof value === 'string' ? [[`${prefix}${key}`, value]] : Object.entries(flatten(value, `${prefix}${key}.`)),
    ),
  );
}

const escapeHtml = (text: string) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

const variables = (entries: Record<string, string | number>) =>
  Object.entries(entries).map(([name, value]) => `--sb-${name}:${typeof value === 'number' ? `${value}px` : value};`).join('');

/** The custom properties the landing CSS reads: the default theme, its dark set when the device is dark. */
export function tokenStyles() {
  const fixed = variables({
    ...Object.fromEntries(Object.entries(space).map(([key, value]) => [`space-${key}`, value])),
    ...Object.fromEntries(Object.entries(radius).map(([key, value]) => [`radius-${key}`, value])),
    ...Object.fromEntries(Object.entries(font.size).map(([key, value]) => [`font-${key}`, value])),
    ...Object.fromEntries(Object.entries(font.display).map(([key, value]) => [`font-display-${key}`, value])),
    ...Object.fromEntries(Object.entries(font.weight).map(([key, value]) => [`weight-${key}`, String(value)])),
    'line-height': String(font.lineHeight),
    'font-family': '"SVN-Poppins", sans-serif',
    'size-control': size.control,
    'size-input': size.input,
    'size-landing': size.landing,
    'motion-slow': motion.slow,
  });
  const shadows = (mode: 'light' | 'dark') =>
    variables(Object.fromEntries(Object.entries(shadowValues[mode]).map(([key, value]) => [`shadow-${key}`, value])));
  return (
    `:root{color-scheme:light dark;${fixed}${variables(semanticLight)}${shadows('light')}}` +
    `@media (prefers-color-scheme:dark){:root{${variables(semanticDark)}${shadows('dark')}}}`
  );
}

/**
 * Flags of the languages on offer. A flag keeps its own colours whatever the theme, like the brand colours in tokens.ts.
 * Each copy gets its own ids, since the page shows a flag more than once.
 */
let flagCount = 0;
const star = (cx: number, cy: number, outer: number) =>
  Array.from({ length: 10 }, (_, index) => {
    const radius = index % 2 === 0 ? outer : outer * 0.382;
    const angle = -Math.PI / 2 + (index * Math.PI) / 5;
    return `${(cx + radius * Math.cos(angle)).toFixed(2)},${(cy + radius * Math.sin(angle)).toFixed(2)}`;
  }).join(' ');
const flags: Record<string, () => string> = {
  // The Union Flag, for English.
  en: () => {
    const id = `flag-${++flagCount}`;
    return (
      `<svg class="flag" viewBox="0 0 60 30" width="24" height="16" preserveAspectRatio="xMidYMid slice" aria-hidden="true" focusable="false">` +
      `<clipPath id="${id}"><path d="M30,15h30v15zv15h-30zh-30v-15zv-15h30z"/></clipPath>` +
      `<path d="M0,0v30h60v-30z" fill="#012169"/><path d="M0,0L60,30M60,0L0,30" stroke="#fff" stroke-width="6"/>` +
      `<path d="M0,0L60,30M60,0L0,30" clip-path="url(#${id})" stroke="#C8102E" stroke-width="4"/>` +
      `<path d="M30,0v30M0,15h60" stroke="#fff" stroke-width="10"/><path d="M30,0v30M0,15h60" stroke="#C8102E" stroke-width="6"/></svg>`
    );
  },
  vi: () =>
    `<svg class="flag" viewBox="0 0 30 20" width="24" height="16" aria-hidden="true" focusable="false">` +
    `<rect width="30" height="20" fill="#DA251D"/><polygon points="${star(15, 10, 6)}" fill="#FFFF00"/></svg>`,
};

/** Fills the page: English text in place (what crawlers and first paint see), both languages for the switch. */
export function renderLanding(html: string) {
  const strings = { en: flatten(en.landing), vi: flatten(vi.landing) };
  const text = (key: string) => {
    const value = strings.en[key];
    if (value === undefined) {
      throw new Error(`welcome.html uses the missing string landing.${key}`);
    }
    return escapeHtml(value);
  };

  return html
    // The inline script ships without its comment lines and indentation.
    .replace(/<script>([\s\S]*?)<\/script>/g, (_, code: string) =>
      `<script>${code.split('\n').map((line) => line.trim()).filter((line) => line && !line.startsWith('//')).join('\n')}</script>`)
    .replace('<!--tokens-->', `<style>${tokenStyles()}</style>`)
    .replace('<!--strings-->', () => `<script type="application/json" id="strings">${JSON.stringify(strings).replace(/</g, '\\u003c')}</script>`)
    // <h1 data-i18n="hero.title"></h1>: the element's text.
    .replace(/(<(\w+)\b[^>]*\sdata-i18n="([\w.]+)"[^>]*>)(<\/\2>)/g, (_, open: string, __, key: string, close: string) => `${open}${text(key)}${close}`)
    // data-i18n-alt="hero.imageAlt": the attribute after the dash.
    .replace(/\sdata-i18n-([\w-]+)="([\w.]+)"/g, (whole: string, attribute: string, key: string) => `${whole} ${attribute}="${text(key)}"`)
    // {{icon:board}}: an inline SVG of the Classic set.
    .replace(/\{\{icon:(\w+)\}\}/g, (_, name: string) => {
      const icon = classicIcons[name as IconName];
      if (!icon) {
        throw new Error(`welcome.html uses the unknown icon ${name}`);
      }
      return renderToStaticMarkup(createElement(icon, { size: 20, strokeWidth: 1.5, 'aria-hidden': true, focusable: false }));
    })
    // {{flag:vi}}: the flag of a language.
    .replace(/\{\{flag:(\w+)\}\}/g, (_, language: string) => {
      const flag = flags[language];
      if (!flag) {
        throw new Error(`welcome.html uses the unknown flag ${language}`);
      }
      return flag();
    });
}

/** `bp(md)` in the landing CSS → the breakpoint in pixels, since a media query cannot read a custom property. */
export const resolveBreakpoints = (css: string) =>
  css.replace(/\bbp\((\w+)\)/g, (_, name: string) => {
    const value = breakpoint[name as keyof typeof breakpoint];
    if (value === undefined) {
      throw new Error(`landing.css uses the unknown breakpoint ${name}`);
    }
    return `${value}px`;
  });

const hasHintCookie = (cookie: string | undefined) =>
  (cookie ?? '').split(';').some((part) => part.trim().startsWith(`${SIGNED_IN_COOKIE}=`) && part.trim().length > SIGNED_IN_COOKIE.length + 1);

/** What nginx does at `/`: guests get the landing page while it is on, everyone else the app; never cached as shared. */
export function rootPageMiddleware(enabled: () => boolean) {
  return (req: IncomingMessage, res: ServerResponse, next: () => void) => {
    const path = (req.url ?? '').split('?')[0];
    if (req.method === 'GET' && path === '/') {
      res.setHeader('Cache-Control', 'no-cache, private');
      res.setHeader('Vary', 'Cookie');
      if (enabled() && !hasHintCookie(req.headers.cookie)) {
        req.url = `/${PAGE}`;
      }
    }
    next();
  };
}

export function landingPage(): Plugin[] {
  const enabled = () => process.env.LANDING_PAGE_ENABLED === 'true';
  return [
    {
      name: 'sprint-boot-landing',
      // Before Vite's CSS step, so bp(md) is a number by the time the media query is parsed.
      enforce: 'pre',
      config: () => ({ build: { rollupOptions: { input: { main: 'index.html', welcome: PAGE } } } }),
      configureServer: (server) => void server.middlewares.use(rootPageMiddleware(enabled)),
      configurePreviewServer: (server) => void server.middlewares.use(rootPageMiddleware(enabled)),
      transform: (code, id) => (id.split('?')[0].endsWith('/landing/landing.css') ? resolveBreakpoints(code) : undefined),
      transformIndexHtml: {
        order: 'pre',
        handler: (html, context) => (context.filename.endsWith(PAGE) ? renderLanding(html) : html),
      },
    },
    {
      // The built stylesheet goes inline: one request fewer before the first paint, for a page of a few KB.
      name: 'sprint-boot-landing-inline-css',
      apply: 'build',
      transformIndexHtml: {
        order: 'post',
        handler: (html, context) => {
          if (!context.filename.endsWith(PAGE) || !context.bundle) {
            return html;
          }
          return html.replace(/<link rel="stylesheet"[^>]*href="\/([^"]+\.css)"[^>]*>/g, (link: string, file: string) => {
            const asset = context.bundle![file];
            return asset?.type === 'asset' ? `<style>${String(asset.source)}</style>` : link;
          });
        },
      },
    },
  ];
}
