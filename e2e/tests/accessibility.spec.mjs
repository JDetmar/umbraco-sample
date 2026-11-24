// Automated accessibility checks in the gate (TST-13, ES-036): WCAG 2.2 A and AA rules on every page.
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { pages } from './pages.mjs';

for (const { name, path } of pages) {
  test(`${name} has no WCAG 2.2 A/AA violations`, async ({ page }) => {
    await page.goto(path);

    const results = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
      .analyze();

    const summary = results.violations.map((v) => `${v.id}: ${v.help} (${v.nodes.map((n) => n.target.join(' ')).join(', ')})`);
    expect(summary).toEqual([]);
  });
}
