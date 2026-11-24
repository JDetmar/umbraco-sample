// Critical journeys (TST-11): what a visitor and a keyboard user must always be able to do.
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

test('keyboard focus is always visible', async ({ page }) => {
  await page.goto('/fixture-maximal/');

  const link = page.locator('.c-cta__action a');
  await link.focus();
  const outline = await link.evaluate((el) => getComputedStyle(el).outlineStyle);
  expect(outline).not.toBe('none');
});
