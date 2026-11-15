import { expect, test, type BrowserContext, type Page } from '@playwright/test';

// The landing page is chosen by the web server from the sb_signed_in hint (e2e/landing.spec.ts); this checks that the
// app sets the hint on sign-in, clears it on sign-out and when the session is gone, and never loops.

const hint = async (context: BrowserContext) => (await context.cookies()).find((cookie) => cookie.name === 'sb_signed_in');

const signIn = async (page: Page) => {
  await page.getByLabel('Email').fill('admin@sprintboot.local');
  await page.getByLabel('Password').fill('Admin123!dev');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL((url) => !/\/(login|auth\/callback|connect)/.test(url.pathname));
};

test('the app sets and clears the signed-in hint', async ({ browser }) => {
  test.setTimeout(120_000);
  const context = await browser.newContext();
  const page = await context.newPage();

  // A stale hint and no session: the app clears it and goes to sign-in, keeping the deep link.
  await context.addCookies([{ name: 'sb_signed_in', value: '1', url: 'http://localhost:5173' }]);
  await page.goto('/spaces/DOC/board');
  await page.waitForURL(/\/login/);
  expect(await hint(context)).toBeUndefined();

  await signIn(page);
  expect(new URL(page.url()).pathname).toBe('/spaces/DOC/board');
  const cookie = await hint(context);
  expect(cookie?.value).toBe('1');
  expect(cookie?.path).toBe('/');
  expect(cookie?.sameSite).toBe('Lax');
  expect(cookie!.expires - Date.now() / 1000).toBeGreaterThan(29 * 24 * 3600);

  // The landing page's sign-in link with a live session: straight to Home.
  await page.goto('/login');
  await page.waitForURL((url) => url.pathname === '/');

  // Where a sign-in with Google or another provider ends without an authorize request: a server session, nothing in
  // this tab. The sign-in page starts the authorize request itself and lands in the app.
  await page.evaluate(() => sessionStorage.clear());
  await context.clearCookies({ name: 'sb_signed_in' });
  await page.goto('/login');
  await page.waitForURL((url) => url.pathname === '/');
  await page.getByRole('button', { name: 'Account' }).waitFor();
  expect((await hint(context))?.value).toBe('1');

  await page.getByRole('button', { name: 'Account' }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await page.waitForURL(/\/login/);
  expect(await hint(context)).toBeUndefined();
  await context.close();
});
