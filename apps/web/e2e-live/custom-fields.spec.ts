import { expect, test, type Page } from '@playwright/test';

// SB-158: a space defines a custom field, a ticket is created with a value, the Details sidebar edits it and history
// keeps it. Runs against `pnpm dev`, creating its own throwaway space.
const signIn = async (page: Page) => {
  await page.goto('/');
  await page.getByLabel('Email').fill('admin@sprintboot.local');
  await page.getByLabel('Password').fill('Admin123!dev');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL((url) => !/\/(login|auth\/callback|connect)/.test(url.pathname));
};

const api = async <T,>(page: Page, method: string, path: string, body?: unknown): Promise<T> =>
  page.evaluate(async ({ method, path, body }) => {
    const user = JSON.parse(sessionStorage.getItem(Object.keys(sessionStorage).find((key) => key.startsWith('oidc.user'))!)!);
    const response = await fetch(path, {
      method,
      headers: { Authorization: `Bearer ${user.access_token}`, 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (!response.ok) {
      throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
    }
    return response.status === 204 ? null : response.json();
  }, { method, path, body }) as Promise<T>;

test('a custom field is set on create, edited in the sidebar and kept in history', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page);
  const key = `CF${Math.floor(Math.random() * 9000 + 1000)}`;
  const space = await api<{ id: string }>(page, 'POST', '/api/v1/project', { name: `Custom fields ${key}`, key, type: 0 });

  // Space settings → Fields: add a select field of the space.
  await page.goto(`/spaces/${key}/settings/fields`);
  await page.getByRole('button', { name: 'Add field' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Name', { exact: true }).fill('Severity');
  await dialog.getByRole('combobox', { name: 'Kind' }).click();
  await page.getByRole('option', { name: 'Select', exact: true }).click();
  await dialog.getByLabel('Option 1', { exact: true }).fill('Low');
  await dialog.getByLabel('Option 2', { exact: true }).fill('High');
  await dialog.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('listitem').filter({ hasText: 'Severity' })).toBeVisible();

  // Create a Bug with the field set in the dialog.
  await page.goto(`/spaces/${key}/tickets`);
  await page.getByRole('button', { name: 'Create', exact: true }).first().click();
  const create = page.getByRole('dialog');
  await create.getByLabel('Title').fill('Login fails on Safari');
  await create.getByRole('combobox', { name: 'Severity' }).click();
  await page.getByRole('option', { name: 'High' }).click();
  await create.getByRole('button', { name: 'Create', exact: true }).click();
  await page.waitForURL(/\/browse\//);

  // The Details sidebar shows it and changes it; history keeps the change under the field's name.
  const sidebar = page.locator('aside', { hasText: 'Reporter' });
  await expect(sidebar.getByRole('combobox', { name: 'Severity' })).toHaveText('High');
  await sidebar.getByRole('combobox', { name: 'Severity' }).click();
  await page.getByRole('option', { name: 'Low' }).click();
  await expect(sidebar.getByRole('combobox', { name: 'Severity' })).toHaveText('Low');
  await page.getByRole('tab', { name: 'History' }).click();
  await expect(page.getByText(/Severity/).first()).toBeVisible();
  await expect(page.getByText('High → Low')).toBeVisible();

  await api(page, 'POST', `/api/v1/project/${space.id}/archive`).catch(() => undefined);
});
