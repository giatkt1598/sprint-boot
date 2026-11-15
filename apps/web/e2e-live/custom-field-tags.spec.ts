import { expect, test, type Page } from '@playwright/test';

// A required tags field: the create dialog waits for it, the sidebar edits the words like labels. Runs against `pnpm dev`.
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

test('a required tags field is filled in on create and edited like labels', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page);
  const key = `TG${Math.floor(Math.random() * 9000 + 1000)}`;
  const space = await api<{ id: string }>(page, 'POST', '/api/v1/project', { name: `Tags ${key}`, key, type: 0 });
  await api(page, 'POST', '/api/v1/custom-field', { projectId: space.id, name: 'Components', type: 9, isRequired: true });

  await page.goto(`/spaces/${key}/tickets`);
  await page.getByRole('button', { name: 'Create', exact: true }).first().click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Title').fill('Login fails on Safari');
  const create = dialog.getByRole('button', { name: 'Create', exact: true });
  await expect(create).toBeDisabled();
  const words = dialog.getByLabel('Components *');
  await words.fill('login');
  await words.press('Enter');
  await expect(create).toBeEnabled();
  await create.click();
  await page.waitForURL(/\/browse\//);

  const details = page.locator('aside').filter({ has: page.getByText('Details', { exact: true }) });
  await details.getByRole('button', { name: /Components/ }).click();
  const input = details.getByLabel('Components');
  await input.fill('api');
  await input.press('Enter');
  await page.getByRole('heading', { level: 1 }).click();
  await expect(details.getByText('api', { exact: true })).toBeVisible();
  await expect(details.getByText('login', { exact: true })).toBeVisible();

  await api(page, 'POST', `/api/v1/project/${space.id}/archive`).catch(() => undefined);
});
