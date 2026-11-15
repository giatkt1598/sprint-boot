import { expect, test } from '@playwright/test';

// The browser tab names the page, then what it sits in, then the app (SB-155). Runs against `pnpm dev` and its DOC space.
test('the tab title follows the page', async ({ page }) => {
  test.setTimeout(120_000);
  await page.goto('/');
  await page.getByLabel('Email').fill('admin@sprintboot.local');
  await page.getByLabel('Password').fill('Admin123!dev');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL((url) => !/\/(login|auth\/callback|connect)/.test(url.pathname));

  const space = await page.evaluate(async () => {
    const user = JSON.parse(sessionStorage.getItem(Object.keys(sessionStorage).find((key) => key.startsWith('oidc.user'))!)!);
    const response = await fetch('/api/v1/project/by-key?key=DOC', { headers: { Authorization: `Bearer ${user.access_token}` } });
    return ((await response.json()) as { name: string }).name;
  });

  await page.goto('/spaces/DOC/backlog');
  await expect(page).toHaveTitle(`Backlog - ${space} | Sprint Boot`);

  await page.goto('/spaces/DOC/overview?tab=timeline');
  await expect(page).toHaveTitle(`Timeline - Overview - ${space} | Sprint Boot`);

  await page.goto('/spaces/DOC/settings/members');
  await expect(page).toHaveTitle(`Members - Settings - ${space} | Sprint Boot`);

  await page.goto('/browse/DOC-5');
  const heading = await page.getByRole('heading', { level: 1 }).first().textContent();
  await expect(page).toHaveTitle(`DOC-5 - ${heading} - ${space} | Sprint Boot`);

  // A ticket opened over the board names the tab until it closes.
  await page.goto('/spaces/DOC/board');
  await expect(page).toHaveTitle(`Board - ${space} | Sprint Boot`);
  await page.getByText('DOC-5', { exact: true }).first().click();
  await expect(page).toHaveTitle(`DOC-5 - ${heading} - ${space} | Sprint Boot`);
  await page.getByRole('button', { name: 'Close' }).click();
  await expect(page).toHaveTitle(`Board - ${space} | Sprint Boot`);

  await page.goto('/search');
  await expect(page).toHaveTitle('Search results | Sprint Boot');

  await page.goto('/account/profile');
  await expect(page).toHaveTitle('Profile - Account | Sprint Boot');

  await page.goto('/admin/users');
  await expect(page).toHaveTitle('Users - Administration | Sprint Boot');

  await page.goto('/no-such-page');
  await expect(page).toHaveTitle('Page not found | Sprint Boot');

  // Back and forward show each page's own title.
  await page.goBack();
  await expect(page).toHaveTitle('Users - Administration | Sprint Boot');
  await page.goForward();
  await expect(page).toHaveTitle('Page not found | Sprint Boot');
});
