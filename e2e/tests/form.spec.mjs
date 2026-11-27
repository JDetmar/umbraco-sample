// The lead form's critical journeys (TST-12, threat model 0001): a keyboard user can send a lead, and a mistake is
// explained next to the field and in a summary that passes the accessibility rules. The site allows 3 posts per
// client per 10 minutes, and every browser here is the same client, so only the desktop project posts (2 posts per run).
// A second local run within 10 minutes against the same `make run` is refused with 429; the submit helper says so.
// Phone width gets the static axe scan and the visual baseline (pages.mjs), not these posting journeys.
// That a lead reaches the MAP is asserted by the integration tests (LeadFormTests), not here.
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { formPage } from './pages.mjs';

// The site refuses a post sooner than SubmissionScreen.MinimumAge (3 s) after the page was rendered; keep this above it.
const humanPause = 3_200;

test.describe.configure({ mode: 'serial' });

test.beforeEach(({}, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop', 'posts count against one shared rate limit; desktop only');
});

// Submits the form and fails with the reason if the rate limit refused the post.
async function submit(page, action) {
  await page.waitForTimeout(humanPause);
  const [response] = await Promise.all([
    page.waitForResponse((r) => r.request().method() === 'POST'),
    action(),
  ]);
  expect(response.status(), 'rate-limited: 3 posts per client per 10 minutes; restart make run or wait').not.toBe(429);
}

test('a keyboard user can send a lead and sees the thank-you message', async ({ page }) => {
  await page.goto(formPage.path);
  const form = page.getByRole('region', { name: 'Contact us' });

  // Start before Name and Tab into it: the honeypot sits before Name in the page and must be skipped.
  const name = form.getByLabel('Name', { exact: true });
  await name.focus();
  await page.keyboard.press('Shift+Tab');
  await expect(form.locator('input[name="LeaveBlank"]')).not.toBeFocused();
  await page.keyboard.press('Tab');
  await expect(name).toBeFocused();
  await page.keyboard.type('Keyboard Visitor');
  await page.keyboard.press('Tab');
  await expect(form.getByLabel('Email', { exact: true })).toBeFocused();
  await page.keyboard.type('keyboard@example.com');
  await page.keyboard.press('Tab');
  await expect(form.getByLabel('Company (optional)', { exact: true })).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(form.getByLabel('Message (optional)', { exact: true })).toBeFocused();
  await page.keyboard.press('Tab');
  const consent = form.getByRole('checkbox');
  await expect(consent).toBeFocused();
  await expect(consent).not.toBeChecked();
  await page.keyboard.press('Space');
  await expect(consent).toBeChecked();
  await page.keyboard.press('Tab');
  await expect(form.getByRole('button', { name: 'Send' })).toBeFocused();

  await submit(page, () => page.keyboard.press('Enter'));

  const thanks = page.getByRole('status');
  await expect(thanks).toHaveText('Thanks, we will be in touch.');
  await expect(thanks).toBeFocused();
  await expect(page).toHaveURL(/#f-[0-9a-f]{32}$/);
  await expect(form.getByRole('button', { name: 'Send' })).toHaveCount(0);
});

test('a mistake is explained in a summary and beside the field, and passes the accessibility rules', async ({ page }) => {
  await page.goto(formPage.path);
  const form = page.getByRole('region', { name: 'Contact us' });
  await form.getByLabel('Name', { exact: true }).fill('Mistaken Visitor');
  await form.getByLabel('Email', { exact: true }).fill('not-an-email');
  await form.getByLabel('Company (optional)', { exact: true }).fill('Kept Company');

  await submit(page, () => form.getByRole('button', { name: 'Send' }).click());

  const summary = page.getByRole('alert');
  await expect(summary).toContainText('Please check the form');
  await expect(summary).toBeFocused();
  const email = form.getByLabel('Email', { exact: true });
  await expect(email).toHaveAttribute('aria-invalid', 'true');
  await expect(email).toHaveAccessibleDescription(/^Email /);
  await expect(form.getByLabel('Company (optional)', { exact: true })).toHaveValue('Kept Company');

  await summary.getByRole('link', { name: /^Email / }).click();
  await expect(page).toHaveURL(new RegExp(`#${(await email.getAttribute('id'))}$`));
  await expect(email).toBeFocused();

  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'])
    .analyze();
  expect(results.violations.map((v) => `${v.id}: ${v.help}`)).toEqual([]);
});
