import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

// Runs against `pnpm dev` with the real backend, database, broker and Mailpit.
const ADMIN = { email: 'admin@sprintboot.local', password: 'Admin123!dev' };
const MAILPIT = 'http://localhost:8025/api/v1';
const WCAG = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

const expectAccessible = async (page: Page) => {
  const results = await new AxeBuilder({ page }).withTags(WCAG).analyze();
  expect(results.violations.map((violation) => `${page.url()} ${violation.id}: ${violation.nodes[0]?.html}`)).toEqual([]);
};

const signIn = async (page: Page, email: string, password: string) => {
  await page.goto('/');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL('**/spaces');
};

const suffix = () => Math.random().toString(36).slice(2, 7).toUpperCase();

test.describe.configure({ mode: 'serial' });

test('sign in, run a space and an issue end to end', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page, ADMIN.email, ADMIN.password);
  await expectAccessible(page);

  const key = `E${suffix()}`;
  await page.getByRole('button', { name: 'Create space' }).click();
  await page.getByLabel('Name').fill(`Space ${key}`);
  await page.getByLabel('Key').fill(key);
  await expectAccessible(page);
  await page.getByRole('dialog').getByRole('button', { name: 'Create space' }).click();
  await page.waitForURL(`**/spaces/${key}/issues`);
  await expect(page.getByText('No issues yet')).toBeVisible();

  await page.getByRole('button', { name: 'Create', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Title').fill('Build đăng nhập screen');
  await dialog.getByRole('textbox', { name: 'Description' }).fill('First description');
  await expectAccessible(page);
  await dialog.getByRole('button', { name: 'Create', exact: true }).click();
  await page.waitForURL(`**/issues/${key}-1`);
  await expect(page.getByRole('heading', { name: 'Build đăng nhập screen' })).toBeVisible();
  await expect(page.getByText('First description')).toBeVisible();
  await expectAccessible(page);

  // Inline edit writes history.
  await page.getByLabel('Points').fill('5');
  await page.getByLabel('Points').blur();
  await page.getByRole('tab', { name: 'History' }).click();
  await expect(page.getByText('changed Points')).toBeVisible();

  // Comment with rich text.
  await page.getByRole('tab', { name: 'Comments' }).click();
  await page.getByLabel('Add a comment').click();
  await page.getByRole('textbox', { name: 'Add a comment' }).fill('Looks good');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('article').getByText('Looks good')).toBeVisible();
  await page.getByRole('button', { name: 'Like' }).click();
  await expect(page.getByRole('button', { name: 'Like' })).toHaveAttribute('aria-pressed', 'true');

  // Mention suggestions.
  await page.getByLabel('Add a comment').click();
  await page.getByRole('textbox', { name: 'Add a comment' }).pressSequentially('@adm');
  await expect(page.getByRole('option').first()).toBeVisible();
  await page.keyboard.press('Enter');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.locator('article .mention')).toBeVisible();

  // Emoji reaction and comment link.
  await page.getByRole('button', { name: 'Add reaction' }).first().click();
  await page.getByRole('button', { name: 'React with 🚀' }).click();
  await expect(page.getByRole('button', { name: 'React with 🚀' })).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'More comment actions' }).first().click();
  await expect(page.getByRole('menuitem', { name: 'Copy link' })).toBeVisible();
  await page.keyboard.press('Escape');

  // Due date through the calendar.
  await page.getByRole('button', { name: 'Due date' }).click();
  await expectAccessible(page);
  await page.getByRole('button', { name: /^15 / }).click();
  await page.getByRole('tab', { name: 'History' }).click();
  await expect(page.getByText('changed Due date')).toBeVisible();
  await page.getByRole('tab', { name: 'Comments' }).click();

  // Attachment.
  await page.getByLabel('Upload').setInputFiles({ name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('hello') });
  await expect(page.getByRole('button', { name: 'Delete notes.txt' })).toBeVisible();

  // Work log.
  await page.getByRole('tab', { name: 'Work Log' }).click();
  await page.getByRole('button', { name: 'Log work' }).click();
  await page.getByLabel('Time spent').fill('1h 30m');
  await page.getByRole('dialog').getByRole('button', { name: 'Log work' }).click();
  await expect(page.getByText('Total logged: 1h 30m')).toBeVisible();

  // Subtask.
  await page.getByRole('button', { name: 'Add subtask' }).click();
  await page.getByRole('dialog').getByLabel('Title').fill('Child work');
  await page.getByRole('dialog').getByRole('button', { name: 'Create', exact: true }).click();
  await expect(page.getByRole('link', { name: `${key}-2` })).toBeVisible();

  // Ctrl K goes straight to an exact key.
  await page.goto(`/spaces/${key}/issues`);
  await expect(page.getByRole('table')).toBeVisible();
  await expectAccessible(page);
  await page.keyboard.press('Control+k');
  await page.keyboard.type(`${key}-2`);
  await expect(page.getByRole('menuitem').filter({ hasText: 'Child work' })).toBeVisible();
  await page.keyboard.press('Enter');
  await page.waitForURL(`**/issues/${key}-2`);

  // Filter by type.
  await page.goto(`/spaces/${key}/issues`);
  await page.getByRole('button', { name: 'Filter' }).click();
  await page.getByRole('button', { name: 'Issue type' }).click();
  await page.getByRole('checkbox', { name: 'Subtask' }).click();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('cell', { name: 'Child work' })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Build đăng nhập screen' })).toHaveCount(0);

  // Create an epic with a file, link an issue to it, filter by epic.
  await page.getByRole('button', { name: 'Create', exact: true }).click();
  await page.getByRole('dialog').getByLabel('Title').fill('Launch epic');
  await page.getByRole('dialog').getByRole('combobox', { name: 'Issue type' }).click();
  await page.getByRole('option', { name: 'Epic' }).click();
  await page.getByLabel('Attachments').setInputFiles({ name: 'plan.txt', mimeType: 'text/plain', buffer: Buffer.from('plan') });
  await page.getByRole('dialog').getByRole('button', { name: 'Create', exact: true }).click();
  await page.waitForURL(`**/issues/${key}-3`);
  await expect(page.getByRole('button', { name: 'Delete plan.txt' })).toBeVisible();
  await page.goto(`/issues/${key}-1`);
  await page.getByRole('combobox', { name: 'Epic' }).click();
  await page.getByRole('option', { name: `${key}-3 Launch epic` }).click();
  await expect(page.getByRole('link', { name: `${key}-3` }).first()).toBeVisible();
  await page.goto(`/spaces/${key}/issues`);
  await page.getByRole('button', { name: 'Filter' }).click();
  await page.getByRole('button', { name: 'Epic' }).click();
  await page.getByRole('checkbox', { name: `${key}-3 Launch epic` }).click();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('cell', { name: 'Build đăng nhập screen' })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Child work' })).toHaveCount(0);

  // Move through the workflow from the status menu.
  await page.goto(`/issues/${key}-1`);
  await page.getByRole('button', { name: 'Status: To Do' }).click();
  await expectAccessible(page);
  await page.getByRole('menuitem', { name: /^In Progress/ }).click();
  await expect(page.getByRole('button', { name: 'Status: In Progress' })).toBeVisible();
  await page.getByRole('tab', { name: 'History' }).click();
  await expect(page.getByText('To Do → In Progress')).toBeVisible();

  // Soft delete with subtask, then restore.
  await page.goto(`/issues/${key}-1`);
  await page.getByRole('button', { name: 'More actions' }).click();
  await page.getByRole('menuitem', { name: 'Delete' }).click();
  await expect(page.getByRole('dialog')).toContainText('1 subtasks');
  await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click();
  await page.waitForURL(`**/spaces/${key}/issues`);
  await page.goto('/admin/deleted-issues');
  await expectAccessible(page);
  await page.getByRole('button', { name: `Restore ${key}-1` }).click();
  await expect(page.getByText('Issue restored')).toBeVisible();
  await page.goto(`/issues/${key}-2`);
  await expect(page.getByRole('heading', { name: 'Child work' })).toBeVisible();

  // Settings screens.
  await page.goto(`/spaces/${key}/settings/members`);
  await expect(page.getByRole('heading', { name: 'Members' })).toBeVisible();
  await expectAccessible(page);
  await page.goto('/admin/users');
  await expect(page.getByRole('cell', { name: ADMIN.email })).toBeVisible();
  await expectAccessible(page);
  // Manage a user: spaces and sign-in methods.
  await page.getByRole('link', { name: /^Manage / }).first().click();
  await expect(page.getByRole('heading', { name: 'Sign-in methods' })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Email and password' })).toBeVisible();
  await expectAccessible(page);

  // Workflow administration.
  await page.goto('/admin/workflows');
  await expect(page.getByRole('cell', { name: 'Default', exact: true })).toBeVisible();
  await expectAccessible(page);
  await page.goto('/admin/statuses');
  await expect(page.getByRole('cell', { name: 'In Progress', exact: true }).first()).toBeVisible();
  await expectAccessible(page);
  await page.goto(`/spaces/${key}/settings/workflows`);
  await expect(page.getByRole('cell', { name: 'Subtask', exact: true })).toBeVisible();
  await expectAccessible(page);

  // Create space from the switcher.
  await page.goto(`/spaces/${key}/issues`);
  await page.getByRole('button', { name: /Switch space/ }).click();
  await page.getByRole('menuitem', { name: 'Create space' }).click();
  await expect(page.getByRole('dialog').getByLabel('Key')).toBeVisible();
  await page.keyboard.press('Escape');

  await page.goto('/account/profile');
  await expectAccessible(page);
  await page.goto('/account/security');
  await expectAccessible(page);
});

test('member flow: created by admin, forgets password, resets by email, signs out', async ({ page, request }) => {
  test.setTimeout(90_000);
  const email = `member-${suffix().toLowerCase()}@test.local`;
  await signIn(page, ADMIN.email, ADMIN.password);
  await page.goto('/admin/users');
  await page.getByRole('button', { name: 'Create user' }).click();
  await page.getByRole('dialog').getByLabel('Name').fill('Nguyễn Thành Viên');
  await page.getByRole('dialog').getByLabel('Email').fill(email);
  await page.getByRole('dialog').getByLabel('Password').fill('Member123!dev');
  await page.getByRole('dialog').getByRole('button', { name: 'Create user' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);

  await page.getByRole('button', { name: 'Account' }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await page.waitForURL('**/login**');

  await page.getByRole('link', { name: 'Forgot password?' }).click();
  await expect(page.getByRole('heading', { name: 'Reset your password' })).toBeVisible();
  await page.getByLabel('Email').fill(email);
  await page.getByRole('button', { name: 'Send reset link' }).click();
  await expect(page.getByRole('status')).toBeVisible();
  await expectAccessible(page);

  let link: string | undefined;
  await expect
    .poll(
      async () => {
        const list = await (await request.get(`${MAILPIT}/search?query=${encodeURIComponent(`to:${email}`)}`)).json();
        const id = list.messages?.[0]?.ID;
        if (!id) {
          return false;
        }
        const message = await (await request.get(`${MAILPIT}/message/${id}`)).json();
        link = /href="([^"]+)"/.exec(message.HTML)?.[1]?.replaceAll('&amp;', '&');
        return Boolean(link);
      },
      { timeout: 30_000 },
    )
    .toBe(true);

  await page.goto(link!);
  await page.getByLabel('New password').fill('Changed123!dev');
  await page.getByLabel('Confirm password').fill('Changed123!dev');
  await expectAccessible(page);
  await page.getByRole('button', { name: 'Set password' }).click();
  await page.waitForURL('**/login**');

  await signIn(page, email, 'Changed123!dev');
  await expect(page.getByRole('button', { name: 'Create space' })).toBeVisible();

  // Members have no administration access.
  await page.goto('/admin');
  await expect(page.getByRole('alert')).toBeVisible();

  // The reset link works only once.
  await page.goto(link!);
  await expect(page.getByRole('heading', { name: 'Reset link invalid' })).toBeVisible();
});

test('a transition that needs input opens a dialog and stays blocked until filled', async ({ page }) => {
  await signIn(page, ADMIN.email, ADMIN.password);
  const token = await page.evaluate(() => {
    const key = Object.keys(sessionStorage).find((item) => item.startsWith('oidc.user:'))!;
    return JSON.parse(sessionStorage.getItem(key)!).access_token as string;
  });
  const headers = { Authorization: `Bearer ${token}` };
  const post = async (path: string, data: unknown) => {
    const response = await page.request.post(path, { headers, data });
    expect(response.ok(), await response.text()).toBe(true);
    return response.json();
  };

  const key = `T${suffix()}`;
  const space = await post('/api/v1/project', { name: `Flow ${key}`, key, type: 0 });
  const types = await (await page.request.get(`/api/v1/issue/types/${space.id}`, { headers })).json();
  const task = types.find((type: { name: string }) => type.name === 'Task');
  const statuses = await (await page.request.get('/api/v1/status', { headers })).json();
  const id = (name: string) => statuses.find((status: { name: string }) => status.name === name).id;
  const workflow = await post('/api/v1/workflow', {
    name: `Review ${key}`,
    definition: {
      initialStatusId: id('To Do'),
      statusIds: [id('To Do'), id('Done')],
      transitions: [
        {
          name: 'Close',
          fromStatusId: null,
          toStatusId: id('Done'),
          rules: [
            { kind: 1, key: 'comment-required' },
            { kind: 1, key: 'required-field', config: { fields: ['dueDate'] } },
          ],
        },
      ],
    },
  });
  await post(`/api/v1/workflow/${workflow.id}/publish`, {});
  await post('/api/v1/project-workflow/set', { projectId: space.id, issueTypeId: task.id, workflowId: workflow.id });
  await post('/api/v1/issue', { projectId: space.id, typeId: task.id, title: 'Needs input' });

  await page.goto(`/issues/${key}-1`);
  await page.getByRole('button', { name: 'Status: To Do' }).click();
  await page.getByRole('menuitem', { name: /^Close/ }).click();
  const dialog = page.getByRole('dialog');
  const confirm = dialog.getByRole('button', { name: 'Close', exact: true });
  await expect(dialog).toContainText(`${key}-1 moves from To Do to Done.`);
  await expect(confirm).toBeDisabled();
  await expectAccessible(page);

  await dialog.getByRole('button', { name: 'Due date' }).click();
  await page.getByRole('button', { name: /^20 / }).click();
  await expect(confirm).toBeDisabled();
  await dialog.getByLabel('Comment (required)').fill('Shipped');
  await confirm.click();

  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByRole('article').getByText('Shipped')).toBeVisible();
  await expect(page.getByText('Done', { exact: true }).first()).toBeVisible();
});

test('run a sprint: plan by dragging, work the board, complete', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page, ADMIN.email, ADMIN.password);
  const token = await page.evaluate(() => {
    const key = Object.keys(sessionStorage).find((item) => item.startsWith('oidc.user:'))!;
    return JSON.parse(sessionStorage.getItem(key)!).access_token as string;
  });
  const headers = { Authorization: `Bearer ${token}` };
  const post = async (path: string, data: unknown) => {
    const response = await page.request.post(path, { headers, data });
    expect(response.ok(), await response.text()).toBe(true);
    return response.json();
  };
  const key = `S${suffix()}`;
  const space = await post('/api/v1/project', { name: `Scrum ${key}`, key, type: 0 });
  const types = await (await page.request.get(`/api/v1/issue/types/${space.id}`, { headers })).json();
  const task = types.find((type: { name: string }) => type.name === 'Task');
  for (const title of ['First', 'Second', 'Third']) {
    await post('/api/v1/issue', { projectId: space.id, typeId: task.id, title });
  }

  const drag = async (from: string, to: string) => {
    const source = await page.locator(from).boundingBox();
    const target = await page.locator(to).boundingBox();
    await page.mouse.move(source!.x + 200, source!.y + 12);
    await page.mouse.down();
    await page.mouse.move(source!.x + 200, source!.y + 30, { steps: 5 });
    await page.mouse.move(target!.x + 200, target!.y + 6, { steps: 10 });
    await page.waitForTimeout(150);
    await page.mouse.up();
  };

  await page.goto(`/spaces/${key}/backlog`);
  await expect(page.getByTestId(`row-${key}-3`)).toBeVisible();
  await expectAccessible(page);

  // Reorder: third goes first.
  const saved = page.waitForResponse((response) => response.url().includes('/move-issue/'));
  await drag(`[data-testid="row-${key}-3"]`, `[data-testid="row-${key}-1"]`);
  await saved;
  await expect(page.getByTestId('list-backlog').locator('[data-testid^=row-]').first()).toHaveAttribute('data-testid', `row-${key}-3`);
  await page.reload();
  await expect(page.getByTestId('list-backlog').locator('[data-testid^=row-]').first()).toHaveAttribute('data-testid', `row-${key}-3`);

  // Create a sprint and plan into it by drag and by the keyboard menu.
  await page.getByRole('button', { name: 'Create sprint' }).click();
  await page.getByRole('dialog').getByLabel('Name').fill('Sprint 1');
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: 'Sprint 1' })).toBeVisible();
  await page.getByRole('button', { name: `Actions for ${key}-1` }).click();
  await page.getByRole('menuitem', { name: 'Move to Sprint 1' }).click();
  await expect(page.locator(`[data-testid^=list-]:has([data-testid="row-${key}-1"])`)).not.toHaveAttribute('data-testid', 'list-backlog');
  await drag(`[data-testid="row-${key}-2"]`, `[data-testid="row-${key}-1"]`);
  await expect(page.getByTestId('list-backlog').locator('[data-testid^=row-]')).toHaveCount(1);

  // Start it.
  await page.getByRole('button', { name: 'Start sprint' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('button', { name: 'Start', exact: true })).toBeDisabled();
  await dialog.getByRole('button', { name: 'Start date' }).click();
  await page.getByRole('button', { name: /^10 / }).click();
  await dialog.getByRole('button', { name: 'End date' }).click();
  await page.getByRole('button', { name: /^20 / }).click();
  await expectAccessible(page);
  await dialog.getByRole('button', { name: 'Start', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Complete sprint' })).toBeVisible();

  // Board: drag a card to Done, the move survives a reload.
  await page.goto(`/spaces/${key}/board`);
  await expect(page.getByTestId(`card-${key}-1`)).toBeVisible();
  await expectAccessible(page);
  const card = await page.getByTestId(`card-${key}-1`).boundingBox();
  await page.mouse.move(card!.x + 40, card!.y + 20);
  await page.mouse.down();
  await page.mouse.move(card!.x + 40, card!.y + 40, { steps: 5 });
  await expect(page.getByTestId('zone-Done')).toBeVisible();
  const zone = await page.getByTestId('zone-Done').boundingBox();
  await page.mouse.move(zone!.x + 40, zone!.y + 30, { steps: 10 });
  await page.waitForTimeout(300);
  const moved = page.waitForResponse((response) => response.url().includes('/transition/execute/'));
  await page.mouse.up();
  expect((await moved).ok()).toBe(true);
  await expect(page.getByTestId('column-Done').getByTestId(`card-${key}-1`)).toBeVisible();
  await page.reload();
  await expect(page.getByTestId('column-Done').getByTestId(`card-${key}-1`)).toBeVisible();

  // Keyboard path: open the card and change status from the panel.
  await page.getByTestId(`card-${key}-2`).focus();
  await page.keyboard.press('Enter');
  await page.getByRole('button', { name: 'Status: To Do' }).click();
  await page.getByRole('menuitem', { name: /^In Progress/ }).click();
  await expect(page.getByRole('button', { name: 'Status: In Progress' })).toBeVisible();
  await page.getByRole('button', { name: 'Close', exact: true }).click();
  await expect(page.getByTestId('column-In Progress').getByTestId(`card-${key}-2`)).toBeVisible();

  // WIP limit warning.
  await page.goto(`/spaces/${key}/settings/board`);
  await expect(page.getByRole('heading', { name: 'Board' })).toBeVisible();
  await expectAccessible(page);
  await page.getByRole('spinbutton', { name: 'Max In Progress' }).fill('0');
  await page.getByRole('button', { name: 'Save changes' }).click();
  await expect(page.getByText('Changes saved')).toBeVisible();
  await page.goto(`/spaces/${key}/board`);
  await expect(page.getByTestId('column-In Progress')).toContainText('Over limit');

  // Complete: unfinished work returns to the backlog, the sprint stays readable.
  await page.getByRole('button', { name: 'Complete sprint' }).click();
  await expect(page.getByRole('dialog')).toContainText('1 done, 1 not done.');
  await page.getByRole('dialog').getByRole('button', { name: 'Complete sprint' }).click();
  await expect(page.getByText('No active sprint.')).toBeVisible();
  await page.goto(`/spaces/${key}/backlog`);
  await page.getByRole('button', { name: /Closed sprints/ }).click();
  await page.getByRole('button', { name: 'Sprint 1' }).click();
  await expect(page.getByRole('dialog')).toContainText('First');
  await expect(page.getByRole('dialog')).toContainText('Second');
  await page.keyboard.press('Escape');

  // Epics.
  await page.goto(`/spaces/${key}/epics`);
  await page.getByRole('button', { name: 'Create epic' }).click();
  await page.getByRole('dialog').getByLabel('Title').fill('Launch');
  await page.getByRole('dialog').getByRole('button', { name: 'Create', exact: true }).click();
  await page.waitForURL(`**/issues/${key}-4`);
  await page.goto(`/spaces/${key}/epics`);
  await expect(page.getByRole('cell', { name: 'Launch' })).toBeVisible();
  await expectAccessible(page);
});

test('a blocked drop returns the card, and other sessions update live', async ({ page }) => {
  test.setTimeout(60_000);
  await signIn(page, ADMIN.email, ADMIN.password);
  const token = await page.evaluate(() => {
    const key = Object.keys(sessionStorage).find((item) => item.startsWith('oidc.user:'))!;
    return JSON.parse(sessionStorage.getItem(key)!).access_token as string;
  });
  const headers = { Authorization: `Bearer ${token}` };
  const post = async (path: string, data: unknown) => {
    const response = await page.request.post(path, { headers, data });
    expect(response.ok(), await response.text()).toBe(true);
    const body = await response.text();
    return body ? JSON.parse(body) : null;
  };
  const key = `B${suffix()}`;
  const space = await post('/api/v1/project', { name: `Rules ${key}`, key, type: 0 });
  const types = await (await page.request.get(`/api/v1/issue/types/${space.id}`, { headers })).json();
  const task = types.find((type: { name: string }) => type.name === 'Task');
  const statuses = await (await page.request.get('/api/v1/status', { headers })).json();
  const id = (name: string) => statuses.find((status: { name: string }) => status.name === name).id;
  const workflow = await post('/api/v1/workflow', {
    name: `Strict ${key}`,
    definition: {
      initialStatusId: id('To Do'),
      statusIds: [id('To Do'), id('In Progress'), id('Done')],
      transitions: [
        { name: 'Start', fromStatusId: id('To Do'), toStatusId: id('In Progress'), rules: [] },
        { name: 'Finish', fromStatusId: id('In Progress'), toStatusId: id('Done'), rules: [] },
      ],
    },
  });
  await post(`/api/v1/workflow/${workflow.id}/publish`, {});
  await post('/api/v1/project-workflow/set', { projectId: space.id, issueTypeId: task.id, workflowId: workflow.id });
  const issue = await post('/api/v1/issue', { projectId: space.id, typeId: task.id, title: 'Strict' });
  const sprint = await post('/api/v1/sprint', { projectId: space.id, name: 'One' });
  await post(`/api/v1/sprint/move-issue/${issue.id}`, { sprintId: sprint.id });
  await post(`/api/v1/sprint/${sprint.id}/start`, { name: 'One', startDate: '2026-11-01', endDate: '2026-11-14' });

  await page.goto(`/spaces/${key}/board`);
  const card = await page.getByTestId(`card-${key}-1`).boundingBox();
  await page.mouse.move(card!.x + 40, card!.y + 20);
  await page.mouse.down();
  await page.mouse.move(card!.x + 40, card!.y + 40, { steps: 5 });
  await expect(page.getByTestId('zone-Done')).toContainText('No transition');
  // The source card is intentionally dim while it is dragged, so only the drop zone is audited here.
  const zoneAudit = await new AxeBuilder({ page }).include('[data-testid="zone-Done"]').withTags(WCAG).analyze();
  expect(zoneAudit.violations).toEqual([]);
  const zone = await page.getByTestId('zone-Done').boundingBox();
  await page.mouse.move(zone!.x + 40, zone!.y + 30, { steps: 10 });
  await page.waitForTimeout(200);
  await page.mouse.up();
  await expect(page.getByText('This issue cannot move to that status.')).toBeVisible();
  await expect(page.getByTestId('column-To Do').getByTestId(`card-${key}-1`)).toBeVisible();

  // Another session moves the issue: this page follows without a reload.
  await page.waitForTimeout(1000);
  const available = await (await page.request.get(`/api/v1/transition/available/${issue.id}`, { headers })).json();
  const fresh = await (await page.request.get(`/api/v1/issue/by-key?key=${key}-1`, { headers })).json();
  await post(`/api/v1/transition/execute/${issue.id}`, { transitionId: available[0].id, concurrencyStamp: fresh.concurrencyStamp });
  await expect(page.getByTestId('column-In Progress').getByTestId(`card-${key}-1`)).toBeVisible({ timeout: 20_000 });
  await page.screenshot({ path: process.env.E2E_SHOT ?? 'test-results/board.png' });
});

test('a board with 500 issues stays usable', async ({ page }) => {
  test.setTimeout(180_000);
  await signIn(page, ADMIN.email, ADMIN.password);
  const token = await page.evaluate(() => {
    const key = Object.keys(sessionStorage).find((item) => item.startsWith('oidc.user:'))!;
    return JSON.parse(sessionStorage.getItem(key)!).access_token as string;
  });
  const headers = { Authorization: `Bearer ${token}` };
  const key = `L${suffix()}`;
  const space = await (await page.request.post('/api/v1/project', { headers, data: { name: `Large ${key}`, key, type: 0 } })).json();
  const types = await (await page.request.get(`/api/v1/issue/types/${space.id}`, { headers })).json();
  const task = types.find((type: { name: string }) => type.name === 'Task');
  const sprint = await (await page.request.post('/api/v1/sprint', { headers, data: { projectId: space.id, name: 'Big' } })).json();
  for (let batch = 0; batch < 10; batch++) {
    await Promise.all(
      Array.from({ length: 50 }, async (_, index) => {
        const issue = await (await page.request.post('/api/v1/issue', { headers, data: { projectId: space.id, typeId: task.id, title: `Issue ${batch}-${index}` } })).json();
        await page.request.post(`/api/v1/sprint/move-issue/${issue.id}`, { headers, data: { sprintId: sprint.id } });
      }),
    );
  }
  await page.request.post(`/api/v1/sprint/${sprint.id}/start`, { headers, data: { name: 'Big', startDate: '2026-11-01', endDate: '2026-11-14' } });

  const started = Date.now();
  await page.goto(`/spaces/${key}/board`);
  await expect(page.locator('[data-testid^=card-]')).toHaveCount(500, { timeout: 30_000 });
  const loaded = Date.now() - started;

  const typed = Date.now();
  await page.getByLabel('Search issue').fill('Issue 3-7');
  await expect(page.locator('[data-testid^=card-]')).toHaveCount(1);
  const filtered = Date.now() - typed;

  console.log(`500 cards: load ${loaded} ms, filter ${filtered} ms`);
  expect(loaded).toBeLessThan(10_000);
  expect(filtered).toBeLessThan(1_500);
});

test('an expired access token renews silently', async ({ page }) => {
  await signIn(page, ADMIN.email, ADMIN.password);
  const expired = await page.evaluate(() => {
    const key = Object.keys(sessionStorage).find((item) => item.startsWith('oidc.user:'))!;
    const user = JSON.parse(sessionStorage.getItem(key)!);
    const before = user.access_token;
    user.expires_at = 1;
    sessionStorage.setItem(key, JSON.stringify(user));
    return { key, before };
  });
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Spaces' })).toBeVisible();
  await expect
    .poll(() => page.evaluate((key) => JSON.parse(sessionStorage.getItem(key)!).access_token, expired.key))
    .not.toBe(expired.before);
});

test('Vietnamese UI has no accessibility regressions', async ({ page }) => {
  await signIn(page, ADMIN.email, ADMIN.password);
  await page.goto('/account/profile');
  await page.getByLabel('Language').click();
  await page.getByRole('option', { name: 'Tiếng Việt' }).click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: 'Hồ sơ' })).toBeVisible();
  await expectAccessible(page);
  await page.goto('/spaces');
  await expect(page.getByRole('heading', { name: 'Space' })).toBeVisible();
  await expectAccessible(page);
  await page.goto('/account/profile');
  await page.getByLabel('Ngôn ngữ').click();
  await page.getByRole('option', { name: 'English' }).click();
  await page.getByRole('button', { name: 'Lưu' }).click();
  await expect(page.getByRole('heading', { name: 'Profile' })).toBeVisible();
});
