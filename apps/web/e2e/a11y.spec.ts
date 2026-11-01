import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

// Screens that need no backend. Signed-in screens are checked in e2e-live.
const wcag = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

const expectNoViolations = async (page: import('@playwright/test').Page) => {
  const results = await new AxeBuilder({ page }).withTags(wcag).analyze();
  expect(results.violations.map((violation) => `${violation.id}: ${violation.nodes[0]?.html}`)).toEqual([]);
};

for (const language of ['en', 'vi']) {
  test.describe(language, () => {
    test.use({ locale: language });

    for (const path of ['/login', '/forgot-password', '/session-expired', '/reset-password', '/dev/ui']) {
      test(`${path} has no WCAG violations`, async ({ page }) => {
        await page.goto(path);
        await page.locator('main').waitFor();
        await expectNoViolations(page);
      });
    }

    test('dialog has no WCAG violations', async ({ page }) => {
      await page.goto('/dev/ui');
      await page.getByRole('button', { name: /dialog/i }).click();
      await page.getByRole('dialog').waitFor();
      await expectNoViolations(page);
    });
  });
}

test('Tailwind utilities win over MUI styles in the layer order', async ({ page }) => {
  await page.goto('/dev/ui');
  const order = await page.evaluate(() =>
    [...document.styleSheets].flatMap((sheet) =>
      [...sheet.cssRules].filter((rule) => rule.cssText.startsWith('@layer theme')).map((rule) => rule.cssText),
    ),
  );
  expect(order.join()).toContain('mui');
  await expect(page.locator('main')).toHaveCSS('padding-top', '32px');
  await expect(page.getByRole('button', { name: 'Primary', exact: true })).toHaveCSS('background-color', 'rgb(37, 99, 235)');
  await expect(page.locator('body')).toHaveCSS('font-family', /SVN-Poppins/);
});
