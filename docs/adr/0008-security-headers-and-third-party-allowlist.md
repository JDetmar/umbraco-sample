# 0008. Security headers, content security policy and the third-party allowlist

- Status: Proposed
- Date: 2026-11-24
- Decider: owner
- Clauses: ES-019, ES-026; framework TST-09, TST-10, CMP-02

## Context

Public pages render content that back-office users write, and marketing sites tend to collect third-party tags over time. The framework requires security-header checks (TST-09). It also requires a third-party allowlist where unapproved tags fail the build (TST-10, CMP-02). Rich text is not yet sanitized server-side (DEV-007), so a strict content security policy is also the main browser-side defence against stored script.

Umbraco's back office (`/umbraco`) ships its own scripts and styles and is not built by this repository. Back-office preview renders site pages in a same-origin frame and adds a same-origin script (`/umbraco/website/preview.js`) that styles itself with adopted stylesheets, which `style-src` does not govern.

## Alternatives considered

- **Headers at the edge only (Front Door rules).** These cannot be tested locally or in CI, and they drift from the code that needs them.
- **HSTS from the app** (`ForwardedHeaders` with `X-Forwarded-Proto`, plus `UseHsts`). This is testable in CI, but it only works if forwarded headers are trusted from exactly the edge. Deferred to Phase 5 with the edge design (DEV-010).
- **A headers package (for example NetEscapades.AspNetCore.SecurityHeaders).** It is well made, but it would be a new dependency for about 100 lines of code that are easy to test. It can be adopted later if the policy grows (nonces, reporting).
- **The allowlist in configuration.** This is flexible, but an environment variable could then add a tracker without review. The allowlist exists to make every new third party a reviewed change.
- **A nonce-based policy.** Not needed: the site has no inline script or style, and a test keeps it that way.

## Decision

- Middleware (`Security/SecurityHeadersMiddleware.cs`) runs before Umbraco and adds headers to every response, including static files and error pages:
  - **Everywhere:** `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin` and `X-Frame-Options: SAMEORIGIN`.
  - **Site pages also get:**
    - a `Permissions-Policy` that denies sensors, camera, microphone, payment and USB;
    - `Cross-Origin-Opener-Policy: same-origin`;
    - a content security policy: `default-src 'self'`, no `unsafe-inline` or `unsafe-eval`, `object-src 'none'`, `base-uri 'self'`, `form-action 'self'` and `frame-ancestors 'self'`.
  - **Not sent:** the `Server` header.
- The policy is built from `Security/ThirdPartyAllowlist.cs`.
  - Each entry is an exact https origin, with a purpose and the resource kinds it may serve. It goes only into those directives.
  - Validation rejects wildcards, plain http, paths and entries without a purpose.
  - The list is empty today.
- **Exemptions.**
  - `/umbraco` paths get the general headers and only a minimal policy (`object-src 'none'; base-uri 'self'; frame-ancestors 'self'`), because Umbraco owns the back office's scripts (DEV-012).
  - Preview is **not** exempt. Its script is same-origin and its styles are adopted stylesheets, so it works under the site policy. An earlier draft dropped the policy when a preview cookie was present; review showed anyone can send that cookie, so the exemption was removed.
  - Framing is same-origin, not `none`, because preview frames site pages.
- **Tests.**
  - Unit tests cover the policy builder, the validation and the exemptions.
  - A shared extractor (`tests/Shared/ResourceReferences.cs`) finds what markup and stylesheets make the browser load. It covers:
    - every `src`, `srcset`, `poster`, `data`, form `action` and `formaction`, `base`, and meta refresh;
    - `<link>` by `rel` and `as`;
    - CSS `url()` and `@import`.
    It handles double-quoted, single-quoted and unquoted attributes and entity-encoded values, and compares scheme, host and port.
  - Fast unit tests prove the extractor reports each kind of hostile markup and allows same-site markup.
  - Integration tests run it on every seeded page and its same-origin stylesheets against the page's own policy. They also assert the headers on pages, static files, health endpoints, 404 responses, preview-cookie requests and the back office.
  - They check that no page has inline script (JSON-LD data blocks excepted), style blocks, style attributes or inline event handlers.
  - The extractor is regex-based, not a browser. A browser test that listens for policy violations would be stronger, and it can be added to the Playwright suite.

## Consequences

- **Adding any third-party script, font, image host or frame is a code change.** It needs an allowlist entry with a purpose, a reviewed pull request and, for a tracker, the consent and privacy review the owner requires.
- Components cannot use inline `style` attributes; layout goes in the stylesheet. Umbraco's Block Grid partials use inline custom properties. They are unused today and would need rework before Block Grid is adopted.
- **HSTS is not sent by the site** (ledger DEV-010). The container sees plain HTTP behind the edge, so Front Door sets HSTS in Phase 5 (ADR 0004), and the Phase 5 smoke test asserts it.
- **The policy has no `report-to` endpoint yet** (DEV-011). Violations in production are invisible until one is added with the Application Insights work.
- **Umbraco's built-in 404 page uses an inline style block, so it renders unstyled** (DEV-012). A custom 404 page is planned before the first deployment.
- Revisit if a third party requires inline script (prefer nonces) or the back office needs a policy of its own.
