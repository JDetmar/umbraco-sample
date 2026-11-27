// Critical journeys (TST-12): what a visitor and a keyboard user must always be able to do.
// TST-12 also names search and forms: the lead form's journeys are in form.spec.mjs; the site has no search yet.
import { expect, test } from '@playwright/test';

test('home page shows the site heading and its components', async ({ page }) => {
  const response = await page.goto('/');

  expect(response?.status()).toBe(200);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Umbraco sample');
  await expect(page.locator('.c-hero')).toBeVisible();
});

test('keyboard users can skip straight to the main content', async ({ page }) => {
  await page.goto('/');

  await page.keyboard.press('Tab');
  const skipLink = page.getByRole('link', { name: 'Skip to main content' });
  await expect(skipLink).toBeFocused();
  await expect(skipLink).toBeInViewport();

  await page.keyboard.press('Enter');
  await expect(page.locator('main')).toBeFocused();
});

test('a visitor can follow a content link to another page', async ({ page }) => {
  await page.goto('/');

  await page.getByRole('link', { name: 'maximal fixture' }).click();

  await expect(page).toHaveURL(/\/fixture-maximal\/$/);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Component fixture: maximal');
});

test('card titles link to their targets', async ({ page }) => {
  await page.goto('/fixture-maximal/');

  await page.locator('.c-card').first().getByRole('link').click();

  await expect(page).toHaveURL(/\/fixture-minimal\/$/);
});

test('an unknown address returns 404', async ({ page }) => {
  const response = await page.goto('/this-page-does-not-exist/');

  expect(response?.status()).toBe(404);
});

test('keyboard focus on a link shows the design-token focus outline', async ({ page }) => {
  await page.goto('/fixture-maximal/');

  // Reach the link by keyboard, so :focus-visible applies as it does for a real keyboard user.
  const link = page.locator('.c-cta__action a');
  for (let i = 0; i < 50 && !(await link.evaluate((el) => el === document.activeElement)); i++) {
    await page.keyboard.press('Tab');
  }
  await expect(link).toBeFocused();

  // The outline must come from the tokens, not the browser default: a missing stylesheet fails this.
  const tokens = await page.evaluate(() => {
    const root = getComputedStyle(document.documentElement);
    return [root.getPropertyValue('--color-focus').trim(), root.getPropertyValue('--focus-width').trim()];
  });
  expect(tokens.every((value) => value !== '')).toBe(true);

  const expected = await page.evaluate(() => {
    const root = getComputedStyle(document.documentElement);
    const probe = document.createElement('div');
    probe.style.outlineColor = root.getPropertyValue('--color-focus').trim();
    probe.style.outlineWidth = root.getPropertyValue('--focus-width').trim();
    probe.style.outlineStyle = 'solid';
    document.body.append(probe);
    const style = getComputedStyle(probe);
    const result = { color: style.outlineColor, width: style.outlineWidth };
    probe.remove();
    return result;
  });
  const actual = await link.evaluate((el) => {
    const style = getComputedStyle(el);
    return { style: style.outlineStyle, color: style.outlineColor, width: style.outlineWidth };
  });
  expect(actual).toEqual({ style: 'solid', ...expected });
});
