// Visual regression on the fixtures (TST-12). Baselines are committed and were produced in the same pinned image.
// Images are masked: fixture media point at files that do not exist (docs/components/README.md).
import { expect, test } from '@playwright/test';
import { pages } from './pages.mjs';

for (const { name, path } of pages) {
  test(`${name} looks as approved`, async ({ page }) => {
    await page.goto(path);

    await expect(page).toHaveScreenshot(`${name}.png`, { fullPage: true, mask: [page.locator('img')] });
  });
}
