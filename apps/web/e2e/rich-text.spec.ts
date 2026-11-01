import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

const expectAccessible = async (page: Page) => {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze();
  expect(results.violations.map((violation) => `${violation.id}: ${violation.nodes[0]?.html}`)).toEqual([]);
};

test.beforeEach(async ({ page }) => {
  await page.goto('/dev/ui');
});

test('typing keeps every character and the focus', async ({ page }) => {
  const editor = page.getByRole('textbox', { name: 'Rich text' });
  await editor.click();
  await page.keyboard.type('Hello world');
  await expect(editor).toHaveText('Hello world');
  await expect(editor).toBeFocused();
});

const html = (page: Page) => page.getByTestId('rich-text-html');

const start = async (page: Page) => {
  const editor = page.getByRole('textbox', { name: 'Rich text' });
  await editor.click();
  return editor;
};

const slash = async (page: Page, query: string, option: string) => {
  await page.keyboard.type(`/${query}`);
  await page.getByRole('option', { name: option }).click();
};

test('the slash menu filters, works by keyboard and turns the line into a block', async ({ page }) => {
  await start(page);
  await page.keyboard.type('/head');
  await expect(page.getByRole('listbox').getByRole('option')).toHaveCount(3);
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await page.keyboard.type('Plan');
  await expect(html(page)).toContainText('<h2');
  await expect(html(page)).toContainText('Plan</h2>');
  await expect(html(page)).not.toContainText('/head');
});

for (const [query, option, expected] of [
  ['bull', 'Bulleted list', '<ul>'],
  ['num', 'Numbered list', '<ol>'],
  ['to-do', 'To-do list', 'data-type="taskList"'],
  ['quote', 'Quote', '<blockquote>'],
  ['callout', 'Callout', 'data-type="callout"'],
  ['code', 'Code block', '<pre>'],
  ['divider', 'Divider', '<hr'],
  ['table', 'Table', '<table'],
] as const) {
  test(`/${query} inserts ${option}`, async ({ page }) => {
    await start(page);
    await slash(page, query, option);
    await page.keyboard.type('x');
    await expect(html(page)).toContainText(expected);
  });
}

test('selecting text opens the format menu', async ({ page }) => {
  const editor = await start(page);
  await page.keyboard.type('Hello');
  await page.keyboard.press('Control+a');
  const menu = page.getByRole('toolbar', { name: 'Formatting' });
  await expect(menu).toBeVisible();
  await expectAccessible(page);
  await menu.getByRole('button', { name: 'Bold' }).click();
  await menu.getByRole('button', { name: 'Italic' }).click();
  await expect(html(page)).toContainText('<strong><em>Hello</em></strong>');
  await menu.getByRole('button', { name: 'Text color' }).click();
  await menu.getByRole('button', { name: 'Red' }).click();
  await expect(html(page)).toContainText('color:');
  await menu.getByRole('button', { name: 'Align center' }).click();
  await expect(html(page)).toContainText('text-align: center');

  await menu.getByRole('button', { name: 'Link', exact: true }).click();
  await menu.getByLabel('Link address').fill('example.com');
  await page.keyboard.press('Enter');
  await expect(html(page)).toContainText('href="https://example.com"');
  await expect(editor).toBeFocused();
});

test('a javascript link is refused', async ({ page }) => {
  await start(page);
  await page.keyboard.type('Hello');
  await page.keyboard.press('Control+a');
  const menu = page.getByRole('toolbar', { name: 'Formatting' });
  await menu.getByRole('button', { name: 'Link', exact: true }).click();
  await menu.getByLabel('Link address').fill('javascript:alert(1)');
  await page.keyboard.press('Enter');
  await expect(html(page)).not.toContainText('href');
});

test('markdown shortcuts work while typing', async ({ page }) => {
  await start(page);
  await page.keyboard.type('# Title');
  await page.keyboard.press('Enter');
  await page.keyboard.type('- item');
  await expect(html(page)).toContainText('<h1>Title</h1>');
  await expect(html(page)).toContainText('<ul>');
});

test('Alt and an arrow key move a block', async ({ page }) => {
  await start(page);
  await page.keyboard.type('first');
  await page.keyboard.press('Enter');
  await page.keyboard.type('second');
  await page.keyboard.press('Alt+ArrowUp');
  await expect(html(page)).toContainText('<p>second</p><p>first</p>');
  await page.keyboard.press('Alt+ArrowDown');
  await expect(html(page)).toContainText('<p>first</p><p>second</p>');
});

test('the mention menu inserts a person', async ({ page }) => {
  await start(page);
  await page.keyboard.type('@Minh');
  await page.getByRole('option', { name: 'Minh Anh' }).click();
  await expect(html(page)).toContainText('data-type="mention"');
});

test('tables have their own tools', async ({ page }) => {
  await start(page);
  await slash(page, 'table', 'Table');
  await page.getByRole('button', { name: 'Add row' }).click();
  await expect(page.locator('.rich-text tr')).toHaveCount(4);
  await page.getByRole('button', { name: 'Delete table' }).click();
  await expect(page.locator('.rich-text table')).toHaveCount(0);
});

test('an image can be inserted by address', async ({ page }) => {
  await start(page);
  await slash(page, 'image', 'Image');
  await page.getByLabel('Image address').fill('https://example.com/a.png');
  await page.getByRole('dialog').getByRole('button', { name: 'Insert' }).click();
  await expect(html(page)).toContainText('src="https://example.com/a.png"');
});

test('the open slash menu has no WCAG violations', async ({ page }) => {
  await start(page);
  await page.keyboard.type('/');
  await expect(page.getByRole('listbox')).toBeVisible();
  await expectAccessible(page);
});

test('the toolbar stays visible and formats text', async ({ page }) => {
  await start(page);
  const tools = page.getByRole('toolbar', { name: 'Editor tools' });
  await expect(tools).toBeVisible();
  await tools.getByRole('button', { name: 'Bulleted list' }).click();
  await page.keyboard.type('item');
  await expect(html(page)).toContainText('<ul>');
  await expectAccessible(page);
});
