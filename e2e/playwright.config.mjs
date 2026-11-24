import { defineConfig, devices } from '@playwright/test';

// Runs inside the pinned Playwright image (scripts/e2e.sh), so browsers and fonts match the committed screenshots.
export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  // ES-007: no automatic retries; a flaky test is a defect to fix, not to retry away.
  retries: 0,
  // ES-043: bounded waits everywhere.
  timeout: 30_000,
  expect: {
    timeout: 5_000,
    // An absolute budget: a ratio over a full page lets a whole recoloured link row through.
    toHaveScreenshot: { maxDiffPixels: 50, animations: 'disabled' },
  },
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:43835',
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 } } },
    { name: 'mobile', use: { ...devices['Pixel 7'] } },
  ],
});
