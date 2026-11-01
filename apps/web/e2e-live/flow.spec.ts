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

const waitForHome = (page: Page) => page.waitForURL((url) => !/\/(login|auth\/callback|connect|realms|accept-invitation)/.test(url.pathname));

const signIn = async (page: Page, email: string, password: string) => {
  await page.goto('/');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await waitForHome(page);
};

const suffix = () => Math.random().toString(36).slice(2, 7).toUpperCase();

test.describe.configure({ mode: 'serial' });

test('sign in, run a space and an issue end to end', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page, ADMIN.email, ADMIN.password);
  await expectAccessible(page);

  const key = `E${suffix()}`;
  await page.goto('/admin/spaces');
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
  // Typed key by key on purpose: an editor rebuilt on each change only fails with real typing.
  await dialog.getByRole('textbox', { name: 'Description' }).pressSequentially('First description');
  await expect(dialog.getByRole('textbox', { name: 'Description' })).toHaveText('First description');
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
  await page.getByRole('textbox', { name: 'Add a comment' }).pressSequentially('Looks good');
  await page.keyboard.press('Enter');
  await page.keyboard.type('/to-do');
  await page.getByRole('option', { name: 'To-do list' }).click();
  await page.keyboard.type('Check the release');
  await page.keyboard.press('Enter');
  await page.keyboard.press('Enter');
  await page.keyboard.type('/code');
  await page.keyboard.press('Enter');
  await page.keyboard.type('const answer = 42;');
  await expectAccessible(page);
  await page.getByRole('button', { name: 'Save' }).click();
  const saved = page.getByRole('article');
  await expect(saved.getByText('Looks good')).toBeVisible();
  await expect(saved.getByRole('checkbox')).toBeDisabled();
  await expect(saved.locator('pre')).toContainText('const answer = 42;');
  await expectAccessible(page);
  await page.getByRole('button', { name: 'Like' }).click();
  await expect(page.getByRole('button', { name: 'React with 👍' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('button', { name: 'Like' })).toHaveCount(0);

  // Mention suggestions.
  await page.getByLabel('Add a comment').click();
  await page.getByRole('textbox', { name: 'Add a comment' }).pressSequentially('@adm');
  await expect(page.getByRole('listbox').getByRole('option').first()).toBeVisible();
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
  await expect(page.locator('aside').getByPlaceholder('Unassigned')).toBeVisible();
  await page.getByRole('button', { name: 'Add subtask' }).click();
  const subtask = page.getByLabel('Subtask name');
  await expect(subtask).toBeFocused();
  await expectAccessible(page);
  await subtask.fill('Child work');
  await page.keyboard.press('Enter');
  await expect(page.getByRole('link', { name: `${key}-2` })).toBeVisible();
  await expect(page).toHaveURL(new RegExp(`/issues/${key}-1$`));
  await expect(subtask).toHaveValue('');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.getByRole('button', { name: 'Cancel' }).click();
  await expect(subtask).toHaveCount(0);
  await expect(page.getByRole('button', { name: `Assignee: Unassigned ${key}-2` })).toBeVisible();

  // A subtask name edits on click and stays a selection on drag.
  const childName = page.getByRole('button', { name: 'Child work', exact: true });
  const nameBox = (await childName.boundingBox())!;
  await page.mouse.move(nameBox.x + 2, nameBox.y + nameBox.height / 2);
  await page.mouse.down();
  await page.mouse.move(nameBox.x + 40, nameBox.y + nameBox.height / 2, { steps: 5 });
  await page.mouse.up();
  await expect(page.getByRole('textbox', { name: 'Title' })).toHaveCount(0);
  await page.mouse.click(5, 5);
  await childName.click();
  await page.getByRole('textbox', { name: 'Title' }).fill('Child work renamed');
  await page.keyboard.press('Enter');
  await expect(page.getByRole('button', { name: 'Child work renamed', exact: true })).toBeVisible();

  await page.getByRole('button', { name: 'Assign to me' }).click();
  await expect(page.getByRole('button', { name: 'Assign to me' })).toHaveCount(0);
  await page.getByRole('button', { name: new RegExp(`^Status: .* ${key}-2$`) }).click();
  await page.getByRole('menuitem').first().click();
  await expect(page.getByRole('button', { name: new RegExp(`^Status: To Do ${key}-2$`) })).toHaveCount(0);
  await expectAccessible(page);

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
  await page.getByRole('button', { name: /^Epic:/ }).click();
  await page.getByRole('button', { name: `${key}-3 Launch epic` }).click();
  await expect(page.getByRole('button', { name: `Epic: ${key}-3 Launch epic` })).toBeVisible();
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

test('the bell updates live and notifications open the issue', async ({ browser, page }) => {
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
  const key = `N${suffix()}`;
  const email = `bell-${suffix().toLowerCase()}@test.local`;
  const user = await post('/api/v1/user-admin', { name: 'Bell Person', email, password: 'Member123!dev', systemRole: 'member' });
  const space = await post('/api/v1/project', { name: `Bell ${key}`, key, type: 0 });
  await post(`/api/v1/project/${space.id}/member`, { userId: user.id, role: 'Member' });
  const types = await (await page.request.get(`/api/v1/issue/types/${space.id}`, { headers })).json();
  const task = types.find((type: { name: string }) => type.name === 'Task');
  const issue = await post('/api/v1/issue', { projectId: space.id, typeId: task.id, title: 'Ring the bell' });

  const context = await browser.newContext({ baseURL: test.info().project.use.baseURL, locale: 'en', reducedMotion: 'reduce' });
  const other = await context.newPage();
  await signIn(other, email, 'Member123!dev');
  await expect(other.getByRole('button', { name: /^Notifications/ })).toBeVisible();
  await other.waitForTimeout(1500);
  const before = await other.getByRole('button', { name: /^Notifications/ }).getAttribute('aria-label');

  const fresh = await (await page.request.get(`/api/v1/issue/by-key?key=${key}-1`, { headers })).json();
  const update = await page.request.put(`/api/v1/issue/${issue.id}`, { headers, data: { concurrencyStamp: fresh.concurrencyStamp, fields: { assignee: user.id } } });
  expect(update.ok()).toBe(true);

  await expect(other.getByRole('button', { name: /^Notifications/ })).not.toHaveAttribute('aria-label', before ?? '', { timeout: 20_000 });
  await other.getByRole('button', { name: /unread/ }).click();
  await expectAccessible(other);
  await other.getByRole('button', { name: new RegExp(`assigned ${key}-1 to you`) }).click();
  await other.waitForURL(`**/issues/${key}-1`);

  await other.goto('/notifications');
  await expect(other.getByRole('heading', { name: 'Notifications' })).toBeVisible();
  await expectAccessible(other);
  await other.goto('/account/notifications');
  await expect(other.getByLabel('Issue assigned')).toBeChecked();
  await expectAccessible(other);
  await context.close();

  await page.goto(`/issues/${key}-1`);
  await page.getByRole('button', { name: /watchers/ }).click();
  await expect(page.getByRole('button', { name: 'Stop watching' })).toBeVisible();
  await page.keyboard.press('Escape');

  await page.goto('/admin/notification-schemes');
  await expectAccessible(page);
  await page.getByRole('link', { name: 'Default' }).click();
  await expect(page.getByRole('cell', { name: 'Issue assigned', exact: true })).toBeVisible();
  await expectAccessible(page);
  await page.getByRole('button', { name: 'Edit Issue assigned' }).click();
  await expect(page.getByRole('checkbox', { name: 'Assignee' })).toBeChecked();
  await expectAccessible(page);
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
  await page.waitForURL('**/spaces/**');
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
  await page.goto('/admin/spaces');
  await expect(page.getByRole('heading', { name: 'Space' })).toBeVisible();
  await expectAccessible(page);
  await page.goto('/account/profile');
  await page.getByLabel('Ngôn ngữ').click();
  await page.getByRole('option', { name: 'English' }).click();
  await page.getByRole('button', { name: 'Lưu' }).click();
  await expect(page.getByRole('heading', { name: 'Profile' })).toBeVisible();
});

const KEYCLOAK = 'http://localhost:8180/realms/sprint-boot';

const keycloakAvailable = async () => {
  try {
    return (await fetch(`${KEYCLOAK}/.well-known/openid-configuration`)).ok;
  } catch {
    return false;
  }
};

const signInAtKeycloak = async (page: Page, username: string) => {
  await page.getByLabel(/username/i).fill(username);
  await page.getByLabel('Password', { exact: true }).fill('Sso123!dev');
  await page.getByRole('button', { name: 'Sign In' }).click();
};

test('an admin adds an OIDC provider and SSO works without a restart', async ({ page, browser }) => {
  test.setTimeout(150_000);
  test.skip(!(await keycloakAvailable()), 'Start Keycloak with the sso compose profile');

  await signIn(page, ADMIN.email, ADMIN.password);
  await page.goto('/admin/sso');
  await expect(page.getByRole('heading', { name: 'Single sign-on' })).toBeVisible();
  await expectAccessible(page);
  await page.getByRole('button', { name: 'Add provider' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Display name').fill('Company SSO');
  await dialog.getByLabel('Discovery URL').fill(`${KEYCLOAK}/.well-known/openid-configuration`);
  await dialog.getByLabel('Client ID').fill('sprint-boot');
  await dialog.getByLabel('Client secret').fill('sprint-boot-dev-secret');
  await expectAccessible(page);
  await dialog.getByRole('button', { name: 'Add provider' }).click();
  await expect(page.getByRole('cell', { name: 'Company SSO', exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Edit Company SSO' }).click();
  await page.getByRole('button', { name: 'Test connection' }).click();
  await expect(page.getByText('Connection works.')).toBeVisible();
  await expectAccessible(page);

  // An unverified IdP email must not reach the existing admin account.
  const attacker = await (await browser.newContext()).newPage();
  await attacker.goto('/login');
  await attacker.getByRole('link', { name: 'Continue with Company SSO' }).click();
  await signInAtKeycloak(attacker, 'mallory');
  await expect(attacker.getByRole('alert')).toHaveText('Your provider has not verified this email, so it cannot be linked.');
  await attacker.context().close();

  // Closed registration refuses a stranger.
  const stranger = await (await browser.newContext()).newPage();
  await stranger.goto('/login');
  await stranger.getByRole('link', { name: 'Continue with Company SSO' }).click();
  await signInAtKeycloak(stranger, 'alice');
  await expect(stranger.getByRole('alert')).toContainText('has not been invited');
  await stranger.context().close();

  // A real invitation accepted through the provider signs the person in.
  // The address is fixed by the realm, so mail from an earlier run must not be read as this one.
  await fetch(`${MAILPIT}/search?query=${encodeURIComponent('to:invitee@sso.local')}`, { method: 'DELETE' });
  await page.goto('/admin/invitations');
  await expectAccessible(page);
  await page.getByRole('button', { name: 'Invite user' }).click();
  await page.getByRole('dialog').getByLabel('Email').fill('invitee@sso.local');
  await page.getByRole('dialog').getByRole('button', { name: 'Send invitation' }).click();
  await expect(page.getByRole('cell', { name: 'invitee@sso.local', exact: true })).toBeVisible();
  await expectAccessible(page);

  const search = async () =>
    await (await fetch(`${MAILPIT}/search?query=${encodeURIComponent('to:invitee@sso.local')}`)).json() as { messages: { ID: string }[] };
  await expect.poll(async () => (await search()).messages.length, { timeout: 30_000 }).toBeGreaterThan(0);
  const found = await search();
  const mail = await (await fetch(`${MAILPIT}/message/${found.messages[0]!.ID}`)).json() as { HTML: string };
  const link = /href="[^"]*(\/accept-invitation[^"]+)"/.exec(mail.HTML)![1]!.replaceAll('&amp;', '&');

  const invitee = await (await browser.newContext()).newPage();
  await invitee.goto(link);
  await expect(invitee.getByRole('heading', { name: 'Join Sprint Boot' })).toBeVisible();
  await expectAccessible(invitee);
  await invitee.getByRole('link', { name: 'Continue with Company SSO' }).click();
  await signInAtKeycloak(invitee, 'invitee');
  await waitForHome(invitee);

  await invitee.goto(link);
  await expect(invitee.getByRole('heading', { name: 'This invitation is no longer valid' })).toBeVisible();
  await expectAccessible(invitee);
  await invitee.context().close();

  // Removing it takes effect at once: the button is gone from the sign-in page.
  await page.goto('/admin/sso');
  await page.getByRole('link', { name: 'Edit Company SSO' }).click();
  await page.getByRole('button', { name: 'Delete provider' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete provider' }).click();
  await page.waitForURL('**/admin/sso');
  const anonymous = await (await browser.newContext()).newPage();
  await anonymous.goto('/login');
  await expect(anonymous.getByRole('button', { name: 'Sign in' })).toBeVisible();
  await expect(anonymous.getByRole('link', { name: 'Continue with Company SSO' })).toHaveCount(0);
  await anonymous.context().close();
});

test('a space admin invites by email, the person accepts with a password and joins', async ({ page, browser, context }) => {
  test.setTimeout(90_000);
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  const email = `joiner-${suffix().toLowerCase()}@test.local`;
  await signIn(page, ADMIN.email, ADMIN.password);
  const key = `I${suffix()}`;
  await page.goto('/admin/spaces');
  await page.getByRole('button', { name: 'Create space' }).click();
  await page.getByLabel('Name').fill(`Space ${key}`);
  await page.getByLabel('Key').fill(key);
  await page.getByRole('dialog').getByRole('button', { name: 'Create space' }).click();
  await page.waitForURL(`**/spaces/${key}/issues`);

  await page.goto(`/spaces/${key}/settings/members`);
  await page.getByRole('button', { name: 'Invite member' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Email').fill(email);
  await expectAccessible(page);
  await dialog.getByRole('button', { name: 'Send invitation' }).click();
  await expect(page.getByText('Invitation sent.')).toBeVisible();

  await page.goto('/admin/invitations');
  await page.getByRole('button', { name: `Copy link ${email}` }).click();
  await expect(page.getByText('New link copied')).toBeVisible();
  const link = await page.evaluate(() => navigator.clipboard.readText());

  const joiner = await (await browser.newContext()).newPage();
  await joiner.goto(link);
  await expect(joiner.getByText(`invited you to the Space ${key} space as a Member.`)).toBeVisible();
  await joiner.getByLabel('Full name').fill('Joiner Person');
  await joiner.getByLabel('Password').fill('Joiner123!x');
  await joiner.getByRole('button', { name: 'Create account' }).click();
  await waitForHome(joiner);
  await expect(joiner.getByText(`Space ${key}`).first()).toBeVisible();
  await joiner.context().close();

  await page.reload();
  await expect(page.getByRole('row', { name: new RegExp(email) }).getByText('Accepted')).toBeVisible();
});

const GITEA = 'http://localhost:3300';
const GITEA_ADMIN = `Basic ${Buffer.from('sbadmin:Sbadmin123!dev').toString('base64')}`;

const giteaAvailable = async () => {
  try {
    return (await fetch(`${GITEA}/api/v1/version`)).ok;
  } catch {
    return false;
  }
};

test('a real Gitea connection links commits, pull requests and CI, and creates branches', async ({ page, context }) => {
  test.setTimeout(120_000);
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  test.skip(!(await giteaAvailable()), 'Start Gitea with the git compose profile and create the sbadmin user');

  const repoName = `repo-${suffix().toLowerCase()}`;
  const tokenResponse = await fetch(`${GITEA}/api/v1/users/sbadmin/tokens`, {
    method: 'POST',
    headers: { Authorization: GITEA_ADMIN, 'Content-Type': 'application/json' },
    body: JSON.stringify({ name: `e2e-${repoName}`, scopes: ['all'] }),
  });
  const token = (await tokenResponse.json() as { sha1: string }).sha1;
  const gitea = async <T,>(method: string, path: string, body?: unknown) => {
    const response = await fetch(`${GITEA}/api/v1${path}`, {
      method,
      headers: { Authorization: `token ${token}`, 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    expect(response.ok, `${method} ${path}`).toBe(true);
    return await response.json() as T;
  };
  await gitea('POST', '/user/repos', { name: repoName, auto_init: true, default_branch: 'main' });

  await signIn(page, ADMIN.email, ADMIN.password);
  const key = `G${suffix()}`;
  await page.goto('/admin/spaces');
  await page.getByRole('button', { name: 'Create space' }).click();
  await page.getByLabel('Name').fill(`Space ${key}`);
  await page.getByLabel('Key').fill(key);
  await page.getByRole('dialog').getByRole('button', { name: 'Create space' }).click();
  await page.waitForURL(`**/spaces/${key}/issues`);
  await page.getByRole('button', { name: 'Create', exact: true }).click();
  await page.getByRole('dialog').getByLabel('Title').fill('Connect the đồng hồ');
  await page.getByRole('dialog').getByRole('button', { name: 'Create', exact: true }).click();
  await page.waitForURL(`**/issues/${key}-1`);
  const issueUrl = page.url();

  await page.goto(`/spaces/${key}/settings/general`);
  await expect(page.getByLabel('Branch name template')).toHaveAttribute('placeholder', 'feature/{issue_key}-{issue_title}');
  await page.goto(`/spaces/${key}/settings/git`);
  await expect(page.getByText('No Git connections yet')).toBeVisible();
  await expectAccessible(page);
  await page.getByRole('button', { name: 'Add connection' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('radio', { name: /Gitea/ }).click();
  await dialog.getByLabel('Name').fill('Local Gitea');
  await dialog.getByLabel('Base URL').fill(`${GITEA}/sbadmin`);
  await dialog.getByLabel('Authentication').click();
  await page.getByRole('option', { name: 'OAuth2' }).click();
  await expect(dialog.getByRole('textbox', { name: 'Redirect URI' })).toHaveValue(/\/api\/v1\/git\/oauth\/callback$/);
  await dialog.getByRole('button', { name: 'Copy redirect URI' }).click();
  await expect(page.getByText('Redirect URI copied.')).toBeVisible();
  await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toMatch(/oauth\/callback$/);
  await dialog.getByLabel('Authentication').click();
  await page.getByRole('option', { name: 'Access token' }).click();
  await dialog.getByLabel('Access token').fill('wrong-token');
  await dialog.getByRole('button', { name: 'Test connection' }).click();
  await expect(page.getByText('Could not connect with these details.')).toBeVisible();
  await dialog.getByLabel('Access token').fill(token);
  await dialog.getByRole('button', { name: 'Test connection' }).click();
  await expect(page.getByText('Connection works.')).toBeVisible();
  await expectAccessible(page);
  await dialog.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('cell', { name: 'Connected' })).toBeVisible();
  await expectAccessible(page);

  await page.getByRole('link', { name: /Repositories Local Gitea/ }).click();
  await expect(page.getByRole('cell', { name: repoName })).toBeVisible();
  await expect(page.getByRole('cell', { name: /^acme\// })).toHaveCount(0);
  await page.getByLabel('Search repositories').fill(repoName);
  const row = page.getByRole('row', { name: new RegExp(repoName) });
  await expect(row.getByText('Active')).toBeVisible();
  await expectAccessible(page);

  await page.goto(issueUrl);
  await page.getByRole('button', { name: 'Create branch' }).click();
  const branchDialog = page.getByRole('dialog');
  await expect(branchDialog.getByLabel('Branch name')).toHaveValue(`feature/${key}-1-connect-the-dong-ho`);
  await branchDialog.getByLabel('Repository').fill(repoName);
  await page.getByRole('option', { name: new RegExp(repoName) }).click();
  await expectAccessible(page);
  await branchDialog.getByRole('button', { name: 'Create branch' }).click();
  await page.getByRole('button', { name: '1 branch', exact: true }).click();
  await expect(page.getByRole('link', { name: `feature/${key}-1-connect-the-dong-ho` })).toBeVisible();
  await page.getByRole('dialog').getByRole('button', { name: 'Close' }).click();
  const branch = `feature/${key}-1-connect-the-dong-ho`;

  await page.getByRole('button', { name: 'Create branch' }).click();
  await page.getByRole('dialog').getByLabel('Repository').fill(repoName);
  await page.getByRole('option', { name: new RegExp(repoName) }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Create branch' }).click();
  await expect(page.getByRole('dialog').getByText('A branch with this name already exists')).toBeVisible();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();

  const owner = `sbadmin/${repoName}`;
  await gitea('POST', `/repos/${owner}/contents/a.txt`, {
    content: Buffer.from('x').toString('base64'),
    message: `${key}-1 real commit`,
    branch,
  });
  await page.getByRole('button', { name: 'Development' }).click();
  await expect(page.getByRole('button', { name: '1 branch', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Development' }).click();
  await page.getByRole('button', { name: '1 commit', exact: true }).click({ timeout: 15_000 });
  await expect(page.getByRole('dialog').getByRole('tab', { name: 'Commit' })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('dialog').getByText(`${key}-1 real commit`)).toBeVisible();
  await expect(page.getByRole('dialog').getByText(`sbadmin/${repoName} · sbadmin`).first()).toBeVisible();
  await page.getByRole('dialog').getByRole('button', { name: 'Close' }).click();

  // With every webhook gone, opening the issue still finds new work.
  const hooks = await gitea<{ id: number }[]>('GET', `/repos/${owner}/hooks`);
  for (const hook of hooks) {
    await fetch(`${GITEA}/api/v1/repos/${owner}/hooks/${hook.id}`, { method: 'DELETE', headers: { Authorization: `token ${token}` } });
  }
  const polled = await gitea<{ commit: { sha: string } }>('POST', `/repos/${owner}/contents/b.txt`, {
    content: Buffer.from('y').toString('base64'),
    message: `${key}-1 polled commit`,
    branch,
  });
  await page.reload();
  await expect(page.getByRole('button', { name: '2 commits', exact: true })).toBeVisible({ timeout: 15_000 });

  await gitea('POST', `/repos/${owner}/pulls`, { title: `${key}-1 ready`, head: branch, base: 'main' });
  await page.reload();
  const pullRow = page.getByRole('button', { name: /^1 pull request/ });
  await expect(pullRow).toContainText('Open', { timeout: 15_000 });
  await pullRow.click();
  await expect(page.getByRole('link', { name: `#1 ${key}-1 ready` })).toBeVisible();
  await expect(page.getByRole('dialog').getByText(`sbadmin/${repoName}`)).toBeVisible();
  await expectAccessible(page);
  await page.getByRole('dialog').getByRole('tab', { name: 'Build' }).click();
  await expect(page.getByRole('dialog').getByText('Nothing here yet.')).toBeVisible();
  await page.getByRole('dialog').getByRole('button', { name: 'Close' }).click();

  await gitea('POST', `/repos/${owner}/statuses/${polled.commit.sha}`, { state: 'pending', context: 'ci/build', target_url: 'http://ci.local/1' });
  await page.reload();
  await expect(page.getByRole('button', { name: /^1 build/ })).toContainText('Pending', { timeout: 15_000 });
  await gitea('POST', `/repos/${owner}/statuses/${polled.commit.sha}`, { state: 'success', context: 'ci/build', target_url: 'http://ci.local/1' });
  await page.reload();
  await expect(page.getByRole('button', { name: /^1 build/ })).toContainText('Passed', { timeout: 15_000 });
  await expectAccessible(page);
});

test('create issue form, backlog cards and home follow the last space', async ({ page, context }) => {
  test.setTimeout(240_000);
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await signIn(page, ADMIN.email, ADMIN.password);
  const key = `U${suffix()}`;
  await page.goto('/admin/spaces');
  await page.getByRole('button', { name: 'Create space' }).click();
  await page.getByLabel('Name').fill(`Space ${key}`);
  await page.getByLabel('Key').fill(key);
  await page.getByRole('dialog').getByRole('button', { name: 'Create space' }).click();
  await page.waitForURL(`**/spaces/${key}/issues`);

  await page.getByRole('button', { name: 'Create', exact: true }).click();
  const dialog = page.getByRole('dialog');
  const title = dialog.getByLabel('Title');
  await expect(title).toBeFocused();
  await dialog.getByLabel('Reporter').click();
  await expect(page.getByRole('listbox').getByRole('option').first().locator('.MuiAvatar-root')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(dialog.getByText('Title is required')).toHaveCount(0);
  await title.fill('Temporary');
  await title.fill('');
  await expect(dialog.getByText('Title is required')).toBeVisible();
  await title.fill('Card row');
  const description = dialog.getByRole('textbox', { name: 'Description' });
  await description.pressSequentially('Select this sentence with the mouse');
  await description.press('Enter');
  await description.press('Enter');
  await description.pressSequentially('After a blank line');

  await expect(dialog.getByText('Description', { exact: true })).toBeVisible();
  await expect(dialog.getByRole('toolbar', { name: 'Editor tools' })).toBeVisible();
  await dialog.getByLabel('Issue type').click();
  await expect(page.getByRole('option', { name: 'Bug' }).locator('svg')).toBeVisible();
  await page.keyboard.press('Escape');
  await dialog.getByLabel('Priority').click();
  await expect(page.getByRole('option', { name: 'High', exact: true }).locator('svg')).toBeVisible();
  await page.keyboard.press('Escape');

  const labels = dialog.getByLabel('Labels');
  await labels.pressSequentially('alpha,');
  await labels.pressSequentially('beta');
  await labels.press('Enter');
  await labels.pressSequentially('gamma,');
  await expect(dialog.locator('.MuiChip-root')).toHaveCount(3);
  await dialog.locator('.MuiChip-root').nth(2).locator('.MuiChip-deleteIcon').click();
  await expect(dialog.locator('.MuiChip-root')).toHaveCount(2);

  await dialog.getByRole('button', { name: 'Start date:' }).click();
  await expect(page.locator('[aria-current="date"]')).toHaveCSS('border-radius', '50%');
  await page.getByRole('group', { name: 'Start date' }).getByRole('button', { name: /^15 / }).click();
  await dialog.getByRole('button', { name: 'Clear date: Start date' }).click();
  await expect(dialog.getByRole('button', { name: 'Clear date: Start date' })).toHaveCount(0);
  await expectAccessible(page);
  await dialog.getByRole('button', { name: 'Create', exact: true }).click();
  await page.waitForURL(`**/issues/${key}-1`);
  await expect(page.getByRole('heading', { name: 'Attachments' })).toHaveCSS('font-size', '16px');

  // A blank line in the description keeps its height once saved.
  await expect(page.getByText('After a blank line')).toBeVisible();
  const blank = page.locator('.rich-text p:empty').first();
  expect((await blank.boundingBox())!.height).toBeGreaterThan(15);

  // Sidebar edits: tags save one after another, points hold what was typed.
  await expect(page.locator('aside [data-tag]')).toHaveCount(2);
  await page.getByRole('button', { name: 'Labels' }).click();
  const sidebarLabels = page.getByRole('combobox', { name: 'Labels' });
  await sidebarLabels.pressSequentially('one,');
  await sidebarLabels.pressSequentially('two,');
  await sidebarLabels.pressSequentially('three,');
  const chips = page.locator('aside .MuiChip-root');
  await expect(chips).toHaveCount(5);
  const labelSaved = page.waitForResponse((response) => response.request().method() === 'PUT' && response.url().includes('/api/v1/issue'));
  await chips.nth(0).locator('.MuiChip-deleteIcon').click();
  await expect(chips).toHaveCount(4);
  await labelSaved;
  const points = page.getByLabel('Points');
  await points.pressSequentially('3x.5');
  await expect(points).toHaveValue('3.5');
  const pointsSaved = page.waitForResponse((response) => response.url().includes('/api/v1/issue') && Boolean(response.request().postData()?.includes('3.5')));
  await points.press('Enter');
  expect((await pointsSaved).ok()).toBe(true);
  await expect(points).toHaveValue('3.5');
  await expect(page.getByText('Someone else changed this')).toHaveCount(0);
  await expect(page.locator('aside').getByRole('combobox', { name: 'Priority' }).locator('svg')).toBeVisible();
  await expect(page.getByText(/^Created .* ago$|^Created a few seconds ago$/)).toBeVisible();
  await page.reload();
  await expect(page.locator('aside [data-tag]')).toHaveCount(4);
  await expect(page.getByLabel('Points')).toHaveValue('3.5');

  // Details folds too.
  await page.getByRole('button', { name: 'Details' }).click();
  await expect(page.getByLabel('Points')).toHaveCount(0);
  await page.getByRole('button', { name: 'Details' }).click();
  await expect(page.getByLabel('Points')).toBeVisible();

  // Description folds, and its heading is the control.
  const toggle = page.getByRole('button', { name: 'Description' });
  await toggle.click();
  await expect(page.getByText('Select this sentence with the mouse')).toHaveCount(0);
  await toggle.click();
  await expect(page.getByText('Select this sentence with the mouse')).toBeVisible();

  // Dragging selects text; only a plain click starts editing.
  const drag = async (text: string) => {
    const box = (await page.getByText(text, { exact: true }).boundingBox())!;
    await page.mouse.move(box.x + 2, box.y + box.height / 2);
    await page.mouse.down();
    await page.mouse.move(box.x + 60, box.y + box.height / 2, { steps: 5 });
    await page.mouse.up();
  };
  await drag('Card row');
  await expect(page.getByRole('heading', { name: 'Card row' })).toBeVisible();
  await drag('Select this sentence with the mouse');
  await expect(page.getByRole('textbox', { name: 'Description' })).toHaveCount(0);
  await page.mouse.click(5, 5);
  await page.getByText('Select this sentence with the mouse', { exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Description' })).toBeVisible();
  await page.getByRole('button', { name: 'Cancel' }).click();

  await page.getByRole('heading', { name: 'Card row' }).click();
  await expect(page.getByRole('textbox', { name: 'Title' })).toBeVisible();
  await expect(page.locator('label', { hasText: /^Title$/ })).toHaveClass(/sr-only/);
  await page.keyboard.press('Escape');

  await page.goto(`/spaces/${key}/backlog`);
  await expectAccessible(page);
  await page.getByRole('link', { name: `${key}-1` }).click();
  await page.waitForURL(`**/issues/${key}-1`);
  await expect(page.getByRole('heading', { name: 'Card row' })).toBeVisible();

  // Content and sidebar scroll apart, so the page itself stays put.
  const scrollers = await page.evaluate(() => {
    const main = document.querySelector('main')!;
    return { main: getComputedStyle(main).overflowY, mainScrolls: main.scrollHeight > main.clientHeight + 1 };
  });
  expect(scrollers.mainScrolls).toBe(false);

  // Pasted files go to storage, and leave it when the text drops them.
  await page.getByLabel('Add a comment').click();
  const composer = page.getByRole('textbox', { name: 'Add a comment' });
  await expect(page.locator('[contenteditable="true"]')).toBeFocused();
  const pixel = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==';
  await composer.evaluate((node, data) => {
    const bytes = Uint8Array.from(atob(data), (char) => char.charCodeAt(0));
    const clipboardData = new DataTransfer();
    clipboardData.items.add(new File([bytes], 'pasted.png', { type: 'image/png' }));
    node.dispatchEvent(new ClipboardEvent('paste', { clipboardData, bubbles: true, cancelable: true }));
  }, pixel);
  await expect(composer.locator('img')).toBeVisible();
  await page.getByRole('button', { name: 'Save' }).click();
  const pasted = page.getByRole('article').locator('img').first();
  await expect(pasted).toBeVisible();
  await expect.poll(() => pasted.evaluate((image: HTMLImageElement) => image.naturalWidth)).toBeGreaterThan(0);
  const pastedUrl = await pasted.getAttribute('src');
  await page.getByRole('article').first().getByRole('button', { name: 'More comment actions' }).click();
  await page.getByRole('menuitem', { name: 'Delete' }).click();
  await expect(page.getByRole('article').locator('img')).toHaveCount(0);
  await expect.poll(async () => (await page.request.get(pastedUrl!)).status()).toBe(404);

  // Comments read newest first.
  for (const comment of ['Older note', 'Newer note']) {
    await page.getByLabel('Add a comment').click();
    await expect(page.locator('[contenteditable="true"]')).toBeFocused();
    await page.getByRole('textbox', { name: 'Add a comment' }).pressSequentially(comment);
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('article').getByText(comment)).toBeVisible();
  }
  await expect(page.getByRole('article').first()).toContainText('Newer note');

  // Reactions: one row, an expandable set, and used ones stand out.
  await page.getByRole('button', { name: 'Add reaction' }).first().click();
  const quick = ['👍', '👎', '❤️', '🌸', '🔥', '😂', '😢', '🚀'];
  // Polled because the popover animates in.
  await expect
    .poll(async () => new Set(await Promise.all(quick.map(async (emoji) => (await page.getByRole('button', { name: `React with ${emoji}` }).boundingBox())!.y))).size)
    .toBe(1);
  await page.getByRole('button', { name: 'More reactions' }).click();
  await expect(page.getByRole('button', { name: 'React with 🐛' })).toBeInViewport();
  const trigger = (await page.locator('button[aria-label="Add reaction"]').first().boundingBox())!;
  expect((await page.getByRole('button', { name: 'React with 🐛' }).boundingBox())!.y).toBeLessThan(trigger.y);
  await expectAccessible(page);
  await page.getByRole('button', { name: 'React with 🔥' }).click();
  await page.getByRole('button', { name: 'Add reaction' }).first().click();
  await expect(page.getByRole('presentation').getByRole('button', { name: 'React with 🔥' })).toHaveAttribute('aria-pressed', 'true');
  await page.keyboard.press('Escape');

  // Any reaction of mine retires the quick Like, which only drops a 👍.
  await expect(page.getByRole('article').first().getByRole('button', { name: 'Like' })).toHaveCount(0);
  await page.getByRole('article').nth(1).getByRole('button', { name: 'Like' }).click();
  await expect(page.getByRole('article').nth(1).getByRole('button', { name: 'React with 👍' })).toContainText('1');
  await expect(page.getByRole('article').nth(1).getByRole('button', { name: 'Like' })).toHaveCount(0);

  // Long threads load in steps.
  for (let index = 1; index <= 3; index += 1) {
    await page.getByLabel('Add a comment').click();
    await expect(page.locator('[contenteditable="true"]')).toBeFocused();
    await page.getByRole('textbox', { name: 'Add a comment' }).pressSequentially(`Extra ${index}`);
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText(`Extra ${index}`)).toBeVisible();
  }
  await page.reload();
  await expect(page.getByRole('article')).toHaveCount(3);
  await page.getByRole('button', { name: 'Load more' }).click();
  await expect(page.getByRole('article')).toHaveCount(5);
  await expect(page.getByRole('button', { name: 'Load more' })).toHaveCount(0);

  const thread = page.getByRole('article').first();
  for (let index = 1; index <= 3; index += 1) {
    await thread.getByRole('button', { name: 'Reply', exact: true }).click();
    await thread.getByRole('textbox', { name: 'Reply' }).pressSequentially(`Reply ${index}`);
    await thread.getByRole('button', { name: 'Save' }).click();
    await expect(thread.getByText(index === 3 ? 'Reply 3' : `Reply ${index}`)).toBeVisible();
  }
  await expect(thread.getByText('Reply 1')).toHaveCount(0);
  await thread.getByRole('button', { name: 'Load more' }).click();
  await expect(thread.getByText('Reply 1')).toBeVisible();

  // Sprint length comes from the space and fills a new sprint.
  await page.goto(`/spaces/${key}/settings/general`);
  await page.getByLabel('Sprint length', { exact: true }).click();
  await page.getByRole('option', { name: '3 weeks' }).click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Changes saved')).toBeVisible();
  await page.goto(`/spaces/${key}/backlog`);
  await page.getByRole('button', { name: 'Create sprint' }).click();
  await expect(page.getByRole('dialog').getByLabel('Name')).toHaveValue('Sprint 1');
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText(/3 weeks · 0 issues · 0 of 0 points/)).toBeVisible();

  // The next one continues from it, even without a reload.
  await page.getByRole('button', { name: 'Create sprint' }).click();
  await expect(page.getByRole('dialog').getByLabel('Name')).toHaveValue('Sprint 2');
  await page.getByRole('dialog').getByRole('button', { name: 'Clear date: Start date' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Clear date: End date' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: 'Sprint 2' })).toBeVisible();

  // Starting one planned without dates offers them.
  await page.getByRole('button', { name: 'Start sprint' }).last().click();
  await expect(page.getByRole('dialog').getByRole('button', { name: 'Clear date: Start date' })).toBeVisible();
  await expect(page.getByRole('dialog').getByRole('button', { name: 'Clear date: End date' })).toBeVisible();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();
  await expect(page.getByRole('img', { name: 'In progress: 0' }).first()).toBeVisible();

  // A space keeps its own workflows; global ones are for use only.
  await page.goto(`/spaces/${key}/settings/workflows`);
  await expect(page.getByRole('heading', { name: 'Available workflows' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Edit Default' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Create workflow' }).click();
  await page.getByRole('dialog').getByLabel('Name').fill('Team flow');
  await page.getByRole('dialog').getByRole('button', { name: /Create/ }).click();
  await page.getByRole('link', { name: 'Edit Team flow' }).click();

  // The designer: a new status starts cut off, which blocks publishing until a transition reaches it.
  await expect(page.getByTestId('status-node-To Do')).toBeVisible();
  await expectAccessible(page);
  const details = page.getByRole('complementary', { name: 'Workflow details' });
  await page.getByRole('button', { name: 'Add status' }).click();
  await details.getByLabel('Name').fill('Review');
  await details.getByRole('button', { name: 'Add status' }).click();
  await expect(page.getByTestId('status-node-Review')).toBeVisible();
  await expect(page.getByRole('alert').filter({ hasText: 'Review cannot be reached' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Publish' })).toBeDisabled();
  await page.screenshot({ path: 'test-results/designer-broken.png' });

  // A line drawn between two statuses opens the transition form, already filled in.
  const drawTransition = async (from: string, to: string) => {
    const handle = page.getByTestId(`status-node-${from}`).locator('.react-flow__handle-right');
    await page.getByTestId(`status-node-${from}`).hover();
    const start = (await handle.boundingBox())!;
    const end = (await page.getByTestId(`status-node-${to}`).boundingBox())!;
    await page.mouse.move(start.x + 3, start.y + 3);
    await page.mouse.down();
    await page.mouse.move(end.x + 20, end.y + 40, { steps: 8 });
    await page.mouse.move(end.x + 40, end.y + 22, { steps: 4 });
    await page.mouse.up();
  };
  await drawTransition('To Do', 'Review');
  await expect(details.getByText('New transition')).toBeVisible();
  await details.getByLabel('Name').fill('Send to review');
  await details.getByRole('button', { name: 'Add transition' }).click();
  await details.getByRole('button', { name: 'Add: Validators' }).click();
  await page.getByRole('group', { name: 'Validators' }).getByRole('button', { name: 'Comment required' }).click();
  await details.getByRole('button', { name: 'Add: Conditions' }).click();
  await page.getByRole('group', { name: 'Conditions' }).getByRole('button', { name: 'Has a role' }).click();
  await details.getByLabel('Member').check();
  await expect(details.getByRole('button', { name: 'Role is Member', exact: true })).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: 'Saved' })).toBeVisible();
  await expectAccessible(page);
  await page.screenshot({ path: 'test-results/designer-transition.png' });

  // Saved as it goes: a reload brings back the draft.
  await page.reload();
  await expect(page.getByRole('button', { name: 'Send to review' }).first()).toBeVisible();
  await expect(page.getByText(/^Draft of v/)).toBeVisible();

  await page.getByRole('button', { name: 'Publish' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Publish' }).click();
  await expect(page.getByText('Workflow published')).toBeVisible();

  // Removing a status that holds issues asks where they go.
  await page.goto(`/spaces/${key}/settings/workflows`);
  await page.getByLabel('Workflows: Task').click();
  await page.getByRole('option', { name: 'Team flow' }).click();
  await expect(page.getByText('Changes saved')).toBeVisible();
  await page.getByRole('link', { name: 'Edit Team flow' }).click();
  await page.getByTestId('status-node-Review').click();
  await details.getByLabel('Start status').check();
  await drawTransition('Review', 'In Progress');
  await details.getByLabel('Name').fill('Begin');
  await details.getByRole('button', { name: 'Add transition' }).click();
  await page.getByTestId('status-node-To Do').click();
  await details.getByRole('button', { name: 'Remove status' }).click();
  await page.getByRole('button', { name: 'Publish' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Publish' }).click();
  await expect(page.getByRole('dialog').getByLabel('Removed status')).toHaveValue('To Do');
  await expectAccessible(page);
  await page.getByRole('dialog').getByLabel('Move issues to').click();
  await page.getByRole('listbox').getByRole('option', { name: 'Review' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Publish' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByText(/Workflow published|Issues are moving/)).toBeVisible();
  await page.getByRole('button', { name: 'Back to workflows' }).click();
  await expect(page.getByRole('heading', { name: 'Available workflows' })).toBeVisible();

  // Status, priority and assignee change right in the rows.
  const quickEdit = async () => {
    await page.getByRole('button', { name: new RegExp(`^Priority: .* ${key}-1$`) }).click();
    await page.getByRole('group', { name: 'Priority' }).getByRole('button', { name: 'High', exact: true }).click();
    await expect(page.getByRole('button', { name: `Priority: High ${key}-1` })).toBeVisible();
    await page.getByRole('button', { name: new RegExp(`^Priority: .* ${key}-1$`) }).click();
    await page.getByRole('group', { name: 'Priority' }).getByRole('button', { name: 'Low', exact: true }).click();
    await expect(page.getByRole('button', { name: `Priority: Low ${key}-1` })).toBeVisible();
  };
  await page.goto(`/spaces/${key}/backlog`);
  await quickEdit();
  await page.goto(`/spaces/${key}/issues`);
  await quickEdit();
  await page.getByRole('button', { name: `Assignee: Unassigned ${key}-1` }).click();
  await expect(page.getByLabel('Search people')).toBeFocused();
  await page.getByLabel('Search people').fill('adm');
  await page.getByRole('group', { name: 'Assignee' }).getByRole('button').first().click();
  await expect(page.getByRole('button', { name: `Assignee: Unassigned ${key}-1` })).toHaveCount(0);
  await page.getByRole('button', { name: new RegExp(`^Status: Review ${key}-1$`) }).click();
  await page.getByRole('menuitem').first().click();
  await expect(page.getByRole('button', { name: new RegExp(`^Status: Review ${key}-1$`) })).toHaveCount(0);

  await page.goto(`/spaces/${key}/epics`);
  await page.getByRole('button', { name: 'Create epic' }).click();
  await page.getByRole('dialog').getByLabel('Title').fill('Quick epic');
  await page.getByRole('dialog').getByRole('button', { name: 'Create', exact: true }).click();
  await page.waitForURL(`**/issues/${key}-2`);
  await page.goto(`/spaces/${key}/epics`);
  await page.getByRole('button', { name: new RegExp(`^Priority: .* ${key}-2$`) }).click();
  await page.getByRole('group', { name: 'Priority' }).getByRole('button', { name: 'High', exact: true }).click();
  await expect(page.getByRole('button', { name: `Priority: High ${key}-2` })).toBeVisible();
  await expectAccessible(page);

  // Breadcrumb: change the type, copy the link.
  await page.goto(`/issues/${key}-1`);
  await page.getByRole('button', { name: 'Issue type: Task' }).click();
  await page.getByRole('group', { name: 'Issue type' }).getByRole('button', { name: 'Bug' }).click();
  await expect(page.getByRole('button', { name: 'Issue type: Bug' })).toBeVisible();
  await page.getByRole('navigation', { name: 'breadcrumb' }).getByText(`${key}-1`).hover();
  await page.getByRole('navigation', { name: 'breadcrumb' }).getByRole('button', { name: 'Copy link' }).click();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toMatch(new RegExp(`/issues/${key}-1$`));

  // The epic field is a purple tag with a searchable picker.
  await page.getByRole('button', { name: /^Epic:/ }).click();
  await page.getByLabel('Search epics').fill('quick');
  await expectAccessible(page);
  await page.getByRole('button', { name: `${key}-2 Quick epic` }).click();
  await expect(page.getByRole('button', { name: `Epic: ${key}-2 Quick epic` })).toBeVisible();

  await page.goto(`/spaces/${key}/issues`);
  await expect(page.getByRole('columnheader', { name: 'Reporter' })).toBeVisible();
  await expect(page.getByRole('columnheader', { name: 'Sprint' })).toBeVisible();
  await expect(page.getByRole('columnheader', { name: 'Created' })).toBeVisible();
  await expectAccessible(page);
  await page.getByRole('link', { name: 'Card row' }).click();
  await page.waitForURL(`**/issues/${key}-1`);

  await page.goto('/');
  await page.waitForURL(`**/spaces/${key}/backlog`);
});

test('configure board columns from the board, five fit on a 1920px screen', async ({ page }) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 1920, height: 1080 });
  await signIn(page, ADMIN.email, ADMIN.password);
  const key = `C${suffix()}`;
  await page.goto('/admin/spaces');
  await page.getByRole('button', { name: 'Create space' }).click();
  await page.getByLabel('Name').fill(`Space ${key}`);
  await page.getByLabel('Key').fill(key);
  await page.getByLabel('Type').click();
  await page.getByRole('option', { name: 'Kanban' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Create space' }).click();
  await page.waitForURL(`**/spaces/${key}/issues`);

  await page.goto(`/spaces/${key}/board`);
  await page.getByRole('button', { name: 'More board actions' }).click();
  await page.getByRole('menuitem', { name: 'Configure Columns' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Add column' }).click();
  await dialog.getByRole('button', { name: 'Add column' }).click();
  await expect(dialog.locator('[data-testid^=column-row-]')).toHaveCount(5);
  await dialog.getByRole('checkbox', { name: 'Story points' }).check();
  await expectAccessible(page);

  const handle = (await dialog.getByRole('button', { name: 'Move column To Do' }).boundingBox())!;
  const target = (await dialog.getByTestId('column-row-In Progress').boundingBox())!;
  await page.mouse.move(handle.x + handle.width / 2, handle.y + handle.height / 2);
  await page.mouse.down();
  await page.mouse.move(handle.x + handle.width / 2, target.y + target.height / 2, { steps: 12 });
  await page.waitForTimeout(250);
  await page.mouse.move(handle.x + handle.width / 2, target.y + target.height * 0.75, { steps: 4 });
  await page.waitForTimeout(250);
  await page.mouse.up();
  await expect(dialog.locator('[data-testid^=column-row-]').nth(1)).toHaveAttribute('data-testid', 'column-row-To Do');
  await dialog.getByRole('button', { name: 'Save changes' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);

  const sections = page.locator('[data-testid^=column-]');
  await expect(sections).toHaveCount(5);
  await expect(sections.nth(1)).toHaveAttribute('data-testid', 'column-To Do');
  const row = sections.first().locator('..');
  expect(await row.evaluate((node) => node.scrollWidth <= node.clientWidth)).toBe(true);

  await page.getByRole('button', { name: 'More board actions' }).click();
  await page.getByRole('menuitem', { name: 'Configure Columns' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Hide column Done' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Save changes' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByTestId('column-Done')).toHaveCount(0);
  await expect(sections).toHaveCount(4);
});
