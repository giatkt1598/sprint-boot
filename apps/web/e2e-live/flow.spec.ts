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
