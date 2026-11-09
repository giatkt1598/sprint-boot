import { expect, test, type Page } from '@playwright/test';

// Runs against `pnpm dev` at phone and tablet sizes. Reads only: it never saves anything for the account.
const ADMIN = { email: 'admin@sprintboot.local', password: 'Admin123!dev' };

const signIn = async (page: Page) => {
  await page.goto('/');
  await page.getByLabel('Email').fill(ADMIN.email);
  await page.getByLabel('Password').fill(ADMIN.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL((url) => !/\/(login|auth\/callback|connect|realms|accept-invitation)/.test(url.pathname));
};

/** The page itself never scrolls sideways; wide content scrolls inside its own area. */
const expectNoPageScroll = async (page: Page) => {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
};

// The local dev data has this space.
const SPACE = 'DOC';

for (const viewport of [{ width: 360, height: 740 }, { width: 390, height: 844 }, { width: 768, height: 1024 }]) {
  test.describe(`${viewport.width}×${viewport.height}`, () => {
    test.use({ viewport });

    test('the sidebar is a drawer the top bar opens and choosing a page closes', async ({ page }) => {
      await signIn(page);
      await page.goto(`/spaces/${SPACE}/overview`);
      const nav = page.getByRole('navigation', { name: 'Space navigation' });
      await expect(nav).toBeHidden();
      await expect(page.getByRole('separator', { name: 'Resize sidebar' })).toHaveCount(0);
      await expectNoPageScroll(page);

      const menu = page.getByRole('button', { name: 'Open menu' });
      await menu.click();
      await expect(nav).toBeVisible();
      await nav.getByRole('link', { name: 'Board' }).click();
      await page.waitForURL(`**/spaces/${SPACE}/board`);
      await expect(nav).toBeHidden();

      // A tap outside closes it, and so does Escape.
      await menu.click();
      await expect(nav).toBeVisible();
      await page.mouse.click(viewport.width - 8, viewport.height / 2);
      await expect(nav).toBeHidden();
      await menu.click();
      await expect(nav).toBeVisible();
      await page.keyboard.press('Escape');
      await expect(nav).toBeHidden();
    });

    test('search opens across the top bar and closes again', async ({ page }) => {
      await signIn(page);
      await page.getByRole('button', { name: 'Search (Ctrl K)' }).click();
      const field = page.getByRole('textbox', { name: 'Search (Ctrl K)' });
      await expect(field).toBeFocused();
      await page.getByRole('button', { name: 'Close' }).click();
      await expect(field).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Account' })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Create' })).toBeVisible();
    });

    test('settings menus open from the top bar too', async ({ page }) => {
      await signIn(page);
      await page.goto('/account/profile');
      await expect(page.getByRole('link', { name: 'Security' })).toBeHidden();
      await page.getByRole('button', { name: 'Open menu' }).click();
      await page.getByRole('link', { name: 'Security' }).click();
      await page.waitForURL('**/account/security');
      await expect(page.getByRole('link', { name: 'Security' })).toBeHidden();
      await expectNoPageScroll(page);
    });
  });
}

test('desktop keeps the sidebar in place, with no menu button', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await signIn(page);
  await page.goto(`/spaces/${SPACE}/overview`);
  await expect(page.getByRole('navigation', { name: 'Space navigation' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Open menu' })).toHaveCount(0);
  await expect(page.getByRole('textbox', { name: 'Search (Ctrl K)' })).toBeVisible();
});
