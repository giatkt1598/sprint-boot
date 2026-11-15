import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import en from '../src/i18n/locales/en.json' with { type: 'json' };
import vi from '../src/i18n/locales/vi.json' with { type: 'json' };
import { LANDING_OFF_URL } from '../playwright.config.ts';

// What / serves with the landing page on (the default base URL) and off. The app's own redirects need the API, so
// "the app" here means index.html with its #root; e2e-live covers signing in and out.
const wcag = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];
const hint = (baseURL: string) => [{ name: 'sb_signed_in', value: '1', url: baseURL }];

const isLanding = (page: Page) => page.locator('h1#hero-title').isVisible();
const isApp = async (page: Page) => (await page.locator('#root').count()) === 1;

// No auth server, even when the preview proxy finds a running dev API (see a11y.spec.ts).
test.beforeEach(async ({ page }) => {
  await page.route('**/.well-known/**', (route) => route.abort());
});

test.describe('landing page on', () => {
  test('guests at / get the landing page, the address stays /, and no app script loads', async ({ page }) => {
    const scripts: string[] = [];
    let scriptBytes = 0;
    page.on('response', async (response) => {
      if (response.request().resourceType() === 'script') {
        scripts.push(response.url());
        scriptBytes += (await response.body()).length;
      }
    });
    const response = await page.goto('/');
    expect(new URL(page.url()).pathname).toBe('/');
    expect(await isLanding(page)).toBe(true);
    expect(response?.headers()['cache-control']).toBe('no-cache, private');
    expect(response?.headers().vary).toContain('Cookie');
    expect(scripts).toEqual([]);
    expect(scriptBytes).toBe(0);

    // Every way in goes to the sign-in page, never back to /.
    const signIn = page.getByRole('link', { name: en.landing.hero.signIn }).first();
    await expect(signIn).toHaveAttribute('href', '/login');
  });

  test('picks the browser language, switches on request and remembers the choice for the app', async ({ browser }) => {
    const context = await browser.newContext({ locale: 'vi-VN' });
    const page = await context.newPage();
    await page.goto('/');
    await expect(page.locator('html')).toHaveAttribute('lang', 'vi');
    await expect(page.locator('h1')).toHaveText(vi.landing.hero.title);
    await expect(page).toHaveTitle(vi.landing.title);

    // The flag opens a menu of languages; Escape closes it again.
    const picker = page.getByRole('button', { name: vi.landing.language });
    await picker.click();
    await expect(page.getByRole('menuitemradio', { name: vi.landing.languages.vi })).toHaveAttribute('aria-checked', 'true');
    await page.keyboard.press('Escape');
    await expect(page.getByRole('menu')).toBeHidden();
    await picker.click();
    await page.getByRole('menuitemradio', { name: en.landing.languages.en }).click();
    await expect(page.getByRole('menu')).toBeHidden();
    await expect(page.locator('h1')).toHaveText(en.landing.hero.title);
    await expect(page.getByRole('img', { name: en.landing.shots.board })).toBeVisible();
    expect(await page.evaluate(() => localStorage.getItem('i18nextLng'))).toBe('en');

    await page.reload();
    await expect(page.locator('h1')).toHaveText(en.landing.hero.title);
    await context.close();
  });

  test('the screens move with the arrows and the dots, and the caption follows', async ({ page }) => {
    await page.goto('/');
    const caption = page.locator('.shots-caption');
    await expect(caption).toHaveText(en.landing.shots.board);
    await page.getByRole('button', { name: en.landing.shots.next }).click();
    await expect(caption).toHaveText(en.landing.shots.backlog);
    await page.getByRole('button', { name: en.landing.shots.mcp }).click();
    await expect(caption).toHaveText(en.landing.shots.mcp);
    await expect(page.getByRole('button', { name: en.landing.shots.mcp })).toHaveAttribute('aria-current', 'true');
    await page.getByRole('button', { name: en.landing.shots.next }).click();
    await expect(caption).toHaveText(en.landing.shots.board);
  });

  test('people with the signed-in hint get the app at /', async ({ context, page, baseURL }) => {
    await context.addCookies(hint(baseURL!));
    await page.goto('/');
    expect(await isApp(page)).toBe(true);
    expect(await page.locator('#hero-title').count()).toBe(0);
  });

  test('a deep link still opens the app', async ({ page }) => {
    await page.goto('/login');
    expect(await isApp(page)).toBe(true);
    await expect(page.locator('main')).toBeVisible();
  });

  for (const language of ['en', 'vi']) {
    for (const width of [360, 1280]) {
      test(`has no WCAG violations in ${language} at ${width}px`, async ({ browser }) => {
        const context = await browser.newContext({ locale: language, viewport: { width, height: 800 } });
        const page = await context.newPage();
        await page.goto('/');
        const results = await new AxeBuilder({ page }).withTags(wcag).analyze();
        expect(results.violations.map((violation) => `${violation.id}: ${violation.nodes[0]?.html}`)).toEqual([]);
        // Nothing scrolls sideways on a small phone.
        expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
        await context.close();
      });
    }
  }
});

test.describe('landing page off', () => {
  test.use({ baseURL: LANDING_OFF_URL });

  test('/ is the app, as before', async ({ page }) => {
    await page.goto('/');
    expect(await isApp(page)).toBe(true);
    expect(await page.locator('#hero-title').count()).toBe(0);
  });

  test('a deep link opens the app', async ({ page }) => {
    await page.goto('/login');
    expect(await isApp(page)).toBe(true);
  });
});
