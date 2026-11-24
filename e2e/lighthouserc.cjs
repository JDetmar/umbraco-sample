// Lighthouse CI (TST-14, ES-047): performance budgets and category scores for the seeded pages.
// Runs inside the pinned Playwright image through scripts/lighthouse.sh, using that image's Chromium.
// Results stay on disk (.lighthouseci/); nothing is uploaded to a public server.
const { chromium } = require('@playwright/test');

const base = (process.env.E2E_BASE_URL || 'http://localhost:43835').replace(/\/$/, '');
const paths = ['/', '/fixture-empty/', '/fixture-minimal/', '/fixture-maximal/'];

module.exports = {
  ci: {
    collect: {
      url: paths.map((p) => base + p),
      numberOfRuns: 3,
      chromePath: chromium.executablePath(),
      settings: {
        // The container has no user namespace sandbox; the page under test is this site's own synthetic content.
        chromeFlags: '--headless=new --no-sandbox --disable-dev-shm-usage',
        preset: 'desktop',
        // The site is served over plain HTTP locally and in CI; HTTPS is the edge's job (DEV-010).
        skipAudits: ['is-on-https', 'redirects-http', 'uses-http2'],
      },
    },
    assert: {
      assertions: {
        // Gate on what the site controls and what is stable under CPU emulation.
        'categories:accessibility': ['error', { minScore: 0.95 }],
        'categories:best-practices': ['error', { minScore: 0.9 }],
        'categories:seo': ['error', { minScore: 0.9 }],
        // Performance scores swing with machine load (CI runners, Apple Silicon emulating amd64), so they warn;
        // the byte and request budgets below are deterministic and fail the build.
        'categories:performance': ['warn', { minScore: 0.85 }],
        'resource-summary:script:count': ['error', { maxNumericValue: 0 }],
        'resource-summary:third-party:count': ['error', { maxNumericValue: 0 }],
        'resource-summary:stylesheet:size': ['error', { maxNumericValue: 30000 }],
        'resource-summary:image:size': ['error', { maxNumericValue: 300000 }],
        'resource-summary:font:count': ['error', { maxNumericValue: 0 }],
        'total-byte-weight': ['error', { maxNumericValue: 400000 }],
        'cumulative-layout-shift': ['error', { maxNumericValue: 0.1 }],
        'largest-contentful-paint': ['warn', { maxNumericValue: 2500 }],
      },
    },
    upload: {
      target: 'filesystem',
      outputDir: '.lighthouseci/reports',
    },
  },
};
