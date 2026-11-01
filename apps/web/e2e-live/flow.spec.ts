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
