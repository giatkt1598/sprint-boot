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

  // The list filters by the field and shows it as a column.
  await page.goto(`/spaces/${key}/tickets`);
  await page.getByRole('button', { name: 'Fields', exact: true }).click();
  await page.getByLabel('Show as a column').check();
  // The filter lives in the address, so the box ticks once the navigation lands.
  await page.getByLabel('High', { exact: true }).click();
  await expect(page.getByLabel('High', { exact: true })).toBeChecked();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('button', { name: /Severity: High/ })).toBeVisible();
  await expect(page.getByText('No matching tickets')).toBeVisible();
  await page.getByRole('button', { name: /Severity: High/ }).getByTestId('CancelIcon').click();
  await expect(page.getByRole('columnheader', { name: /Severity/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Login fails on Safari/ }).getByText('Low')).toBeVisible();

  // Space settings → Fields orders built-in and custom fields together and hides built-in ones; the sidebar follows.
  await page.goto(`/spaces/${key}/settings/fields`);
  const severityRow = page.getByRole('listitem').filter({ hasText: 'Severity' });
  for (let step = 0; step < 10; step++) {
    await severityRow.getByRole('button', { name: 'Move Severity up' }).click();
  }
  await page.getByRole('listitem').filter({ hasText: 'Reporter' }).getByLabel('Show in ticket details').uncheck();
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByText('Fields saved')).toBeVisible();
  await page.goto(`/browse/${key}-1`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Login fails on Safari');
  const details = page.locator('aside').filter({ has: page.getByText('Details', { exact: true }) });
  await expect(details.getByText('Reporter')).toHaveCount(0);
  const labels = await details.locator('div.flex.min-h-9 > span').allTextContents();
  expect(labels[0]).toBe('Severity');

  await api(page, 'POST', `/api/v1/project/${space.id}/archive`).catch(() => undefined);
});
