import { expect, test } from '@playwright/test';

// SB-162: a move that requires a custom field asks for it in its dialog. Runs against `pnpm dev`.
test('a move that requires a custom field asks for it and saves it', async ({ page }) => {
  test.setTimeout(180_000);
  await page.goto('/');
  await page.getByLabel('Email').fill('admin@sprintboot.local');
  await page.getByLabel('Password').fill('Admin123!dev');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL((url) => !/\/(login|auth\/callback|connect)/.test(url.pathname));
  const call = (method: string, path: string, body?: unknown) => page.evaluate(async ({ method, path, body }) => {
    const user = JSON.parse(sessionStorage.getItem(Object.keys(sessionStorage).find((key) => key.startsWith('oidc.user'))!)!);
    const r = await fetch(path, { method, headers: { Authorization: `Bearer ${user.access_token}`, 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
    const text = await r.text();
    if (!r.ok) throw new Error(`${method} ${path} ${r.status} ${text}`);
    return text ? JSON.parse(text) : null;
  }, { method, path, body });
  const key = `WF${Math.floor(Math.random() * 9000 + 1000)}`;
  const space = await call('POST', '/api/v1/project', { name: `Workflow fields ${key}`, key, type: 0 });
  const field = await call('POST', '/api/v1/custom-field', { projectId: space.id, name: 'Root cause', type: 4, options: [{ label: 'Code' }, { label: 'Config' }] });
  const types = await call('GET', `/api/v1/ticket/types/${space.id}`);

  const todo = '6f1b1a10-0000-4000-8000-000000000001', done = '6f1b1a10-0000-4000-8000-000000000003';
  const workflow = await call('POST', '/api/v1/workflow', { name: `Flow ${key}`, projectId: space.id, definition: { initialStatusId: todo, statusIds: [todo, done], transitions: [
    { name: 'Finish', fromStatusId: null, toStatusId: done, rules: [{ kind: 1, key: 'required-field', config: { fields: [`cf.${field.key}`] } }] } ] } });
  await call('POST', `/api/v1/workflow/${workflow.id}/publish`, {});
  await call('POST', '/api/v1/project-workflow/set', { projectId: space.id, ticketTypeId: types.find((t: { name: string }) => t.name === 'Bug').id, workflowId: workflow.id });
  const ticket2 = await call('POST', '/api/v1/ticket', { projectId: space.id, typeId: types.find((t: { name: string }) => t.name === 'Bug').id, title: 'Crash on save' });

  await page.goto(`/browse/${ticket2.key}`);
  await page.getByRole('button', { name: /^Status:/ }).click();
  await page.getByRole('menuitem', { name: 'Done' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('combobox', { name: 'Root cause' }).click();
  await page.getByRole('option', { name: 'Config' }).click();
  await dialog.getByRole('button', { name: 'Done' }).click();
  await expect(page.locator('aside', { hasText: 'Reporter' }).getByRole('combobox', { name: 'Root cause' })).toHaveText('Config');
});
