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
      await expect(page.getByRole('button', { name: 'Create', exact: true })).toBeVisible();
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

test.describe('phone with a touch screen', () => {
  test.use({ viewport: { width: 360, height: 740 }, hasTouch: true, isMobile: true });

  test.beforeEach(async ({ page }) => {
    // Nothing here saves preferences; this keeps it so if a page tries.
    await page.route('**/api/v1/auth/preferences', (route) => route.fulfill({ status: 200, body: '{}' }));
    await signIn(page);
  });

  test('no screen scrolls sideways', async ({ page }) => {
    test.setTimeout(240_000);
    const pages = [
      `/spaces/${SPACE}/overview`,
      `/spaces/${SPACE}/overview?tab=timeline`,
      `/spaces/${SPACE}/backlog`,
      `/spaces/${SPACE}/board`,
      `/spaces/${SPACE}/epics`,
      `/spaces/${SPACE}/tickets`,
      `/spaces/${SPACE}/people`,
      `/spaces/${SPACE}/settings/members`,
      '/search',
      '/notifications',
      '/assistant',
      '/account/profile',
      '/admin/users',
    ];
    for (const size of [{ width: 360, height: 740 }, { width: 390, height: 844 }, { width: 768, height: 1024 }]) {
      await page.setViewportSize(size);
      for (const path of pages) {
        await page.goto(path);
        await page.locator('main').first().waitFor();
        await page.waitForLoadState('networkidle');
        const overflow = await page.evaluate(() =>
          [document.documentElement, ...document.querySelectorAll('main')].map((element) => element.scrollWidth - element.clientWidth),
        );
        expect(Math.max(...overflow), `${path} at ${size.width}px`).toBeLessThanOrEqual(0);
      }
    }
  });

  test('icon buttons are big enough for a finger', async ({ page }) => {
    await page.goto(`/spaces/${SPACE}/overview`);
    for (const name of ['Open menu', 'Search (Ctrl K)', 'Create', 'Account']) {
      // The account button also reads out the avatar's name.
      const box = await page.getByRole('button', { name, exact: name !== 'Account' }).boundingBox();
      expect(box!.width, name).toBeGreaterThanOrEqual(44);
      expect(box!.height, name).toBeGreaterThanOrEqual(44);
    }
  });

  test('dialogs take the whole screen and filters rise from the bottom', async ({ page }) => {
    await page.goto(`/spaces/${SPACE}/tickets`);
    await page.getByRole('button', { name: 'Create', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Create ticket' });
    await expect(dialog).toBeVisible();
    const paper = await dialog.boundingBox();
    expect(paper!.width).toBe(360);
    await page.getByRole('button', { name: 'Cancel' }).click();

    await page.getByRole('button', { name: /^Filter/ }).click();
    const sheet = page.getByRole('dialog', { name: 'Filter' });
    await expect(sheet).toBeVisible();
    // Once it has slid up, it sits on the bottom edge.
    await expect.poll(async () => {
      const box = await sheet.boundingBox();
      return Math.round(box!.y + box!.height);
    }).toBe(740);
    await sheet.getByRole('button', { name: 'Done' }).click();
    await expect(sheet).toBeHidden();
  });

  test('the board snaps from column to column and a tapped card opens with its status', async ({ page }) => {
    await page.goto(`/spaces/${SPACE}/board`);
    const columns = page.locator('section[data-testid^="column-"]');
    await expect(columns.first()).toBeVisible();
    const board = columns.first().locator('..');
    await expect(board).toHaveCSS('scroll-snap-type', 'x mandatory');
    const width = (await columns.first().boundingBox())!.width;
    expect(width).toBeGreaterThan(240);
    expect(width).toBeLessThan(360);

    const card = page.locator('[data-testid^="card-"]').first();
    if (await card.count()) {
      await card.tap();
      const panel = page.locator('.MuiDrawer-paper').last();
      await expect(panel.getByText('Details', { exact: true })).toBeVisible();
      await expect(panel.getByText('Status', { exact: true }).first()).toBeVisible();
    }
  });

  test('a ticket stacks its details under the content and keeps the comment box reachable', async ({ page }) => {
    await page.goto(`/spaces/${SPACE}/tickets`);
    await page.locator('ul[aria-label="Tickets"] a').first().click();
    await page.waitForURL('**/browse/**');
    const heading = page.locator('h1').first();
    const details = page.getByText('Details', { exact: true });
    await expect(details).toBeAttached();
    expect((await details.boundingBox())!.y).toBeGreaterThan((await heading.boundingBox())!.y);
    expect((await details.boundingBox())!.x).toBeLessThan(40);
    const comment = page.getByRole('textbox', { name: 'Add a comment' });
    await comment.scrollIntoViewIfNeeded();
    await expect(comment).toBeVisible();
    await expectNoPageScroll(page);
  });

  test('tickets and epics list as cards with a sort select', async ({ page }) => {
    for (const [path, label] of [[`/spaces/${SPACE}/tickets`, 'Tickets'], [`/spaces/${SPACE}/epics`, 'Epics']] as const) {
      await page.goto(path);
      const list = page.getByRole('list', { name: label });
      await expect(list.getByRole('listitem').first()).toBeVisible();
      await expect(page.getByRole('table')).toHaveCount(0);
      await expect(page.getByRole('combobox', { name: 'Sort by' })).toBeVisible();
    }
  });

  test('the backlog keeps one line per ticket with a menu to move it', async ({ page }) => {
    await page.goto(`/spaces/${SPACE}/backlog`);
    const row = page.locator('tr[data-testid^="row-"]').first();
    await expect(row).toBeVisible();
    await expect(row.locator('td')).toHaveCount(5);
    await row.getByRole('button', { name: /^Actions for/ }).click();
    await expect(page.getByRole('menuitem', { name: /^Move to/ }).first()).toBeVisible();
  });
});
