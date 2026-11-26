# Traceability

Where each requirement stands: every clause of the engineering standard (ES-001 to ES-061) and every requirement in [the framework](engineering-rigor-framework.md), with the evidence or the ledger entry behind it. The [conformance ledger](engineering-conformance.yml) is the source of truth for the standard (ES-038). This page never claims more than the ledger does, and a policy test (`TraceabilityPolicyTests`) keeps the two in step: every deviation the ledger links to a clause is named on that clause's row. ADRs cited here may still be Proposed; the [ADR index](adr/README.md) has their status.

How to read the status column:

- **Standard clauses** use the ledger's own status (`compliant`, `deviation`, `unknown`, `not_applicable`) for clauses the ledger assesses, and `not assessed` for the rest. A clause that is not assessed is not a failure: the build-out assesses clauses as the work reaches them. The related work listed for those is a pointer, not a claim.
- **Framework requirements** use `met`, `partly`, `gap` (not started or not met), `planned` (a phase in [the plan](plan.md) covers it), `deferred` (waiting for a trigger, ES-060), `not applicable` (with the reason), or `outside the code` (a people or process commitment this repository cannot show on its own).

## Engineering standard

| Clause | Topic | Status | Evidence or ledger entry |
|---|---|---|---|
| ES-001 | Behavior and oracles | deviation | DEV-018: the database user tool's SQL is checked by text, not parsed by SQL Server. |
| ES-002 | Evidence quality | compliant | Property tests (CsCheck) and weekly mutation testing (Stryker.NET) of the domain; see the ledger. |
| ES-003 | Trustworthy signals | not assessed | No test is quarantined; six CI checks are required on `main` (ruleset main-2), though admins can bypass them (DEV-001). |
| ES-004 | Boundaries and intent | not assessed | Component contract tests (`tests/UmbracoSample.Web.Tests/Components`). |
| ES-005 | Determinism | not assessed | Visual baselines on seeded content; Lighthouse performance scores only warn because they vary (`e2e/lighthouserc.cjs`). |
| ES-006 | Real contracts | deviation | DEV-018. Integration tests run the real stack through Aspire (`tests/UmbracoSample.Integration.Tests`). |
| ES-007 | Hostile inputs | compliant | Generated hostile inputs and a retained corpus for the redirect parser; see the ledger. |
| ES-008 | Deliberate design feedback | not assessed | |
| ES-009 | Safety net | not assessed | |
| ES-010 | Scope | not assessed | |
| ES-011 | Mainline and release identity | deviation | DEV-001: admins can bypass required checks. |
| ES-012 | Authoritative gate | deviation | DEV-001. `make` targets are the gate CI runs (`.github/workflows/ci.yml`). |
| ES-013 | Pinned builds | compliant | Central package versions, committed lock files, SDK and Actions pinned; see the ledger. |
| ES-014 | Compatibility and recovery | deviation | DEV-003 (Umbraco upgrade migrations), DEV-005 (uSync property removal). |
| ES-015 | Ownership tradeoff | compliant | [Dependency register](dependencies.md) with owner approvals. |
| ES-016 | Vulnerability response | compliant | `make vuln`, `make frontend-audit`, Dependabot and CodeQL; see the ledger. |
| ES-017 | Build and tool privileges | compliant | SHA-pinned Actions and least-privilege workflow permissions; see the ledger. |
| ES-018 | Executable boundaries | compliant | NetArchTest rule that keeps the domain framework-free. |
| ES-019 | Durable decisions | compliant | [ADRs](adr/README.md). |
| ES-020 | Cohesion and effects | compliant | Domain project with no package references, checked by a test. |
| ES-021 | State and abstraction | compliant | Redirect rules parsed into precise types (ADR 0009). |
| ES-022 | Value integrity | not assessed | Planned with forms: idempotency key and double-submit tests (Phase 4, threat model 0001). |
| ES-023 | Threats and authorization | deviation | DEV-017. Threat models 0001 (lead form) and 0002 (Azure environment). |
| ES-024 | Credentials | deviation | DEV-001 (admin bypass), DEV-004 (no image-URL signing key), DEV-016 (preview token, closing), DEV-017 (standing deploy identity). Managed identities, Key Vault, OIDC to Azure and Pulumi, no stored cloud secrets. |
| ES-025 | Sensitive data | deviation | DEV-010 (no HSTS yet), DEV-013 (plain HTTP inside the Container Apps environment). |
| ES-026 | Boundaries and audit | deviation | DEV-004 (unsigned image URLs), DEV-007 (rich text not sanitized server-side), DEV-012 (back office and 404 outside the strict policy). |
| ES-027 | Environment isolation | compliant | Synthetic seed content only in dev and CI. DEV-002 (copying public production content to staging) is a parked proposal while dev is the only environment. |
| ES-028 | Final review and authorization | not assessed | Delegation envelope in `CLAUDE.md`: an independent reviewer context for every agent pull request. |
| ES-029 | Review focus | not assessed | |
| ES-030 | Diagnostic signals | deviation | DEV-011 (no CSP reporting), DEV-015 (single replica). OpenTelemetry to Application Insights. |
| ES-031 | Recovery | not assessed | Media soft delete declared; weekly database restore test in dev (`restore-test.yml`, ADR 0019, OPS-05), not yet proved by a run. |
| ES-032 | Learning | not assessed | |
| ES-033 | Reader and contract | not assessed | One page per component in [docs/components](components/README.md). |
| ES-034 | Failure semantics | not assessed | The site refuses to start on an invalid redirect map (ADR 0009). |
| ES-035 | Accessible behavior | deviation | DEV-007. axe scans of every seeded page and keyboard journeys in `e2e/`. |
| ES-036 | Verification | deviation | DEV-008: no periodic manual assistive-technology check yet. |
| ES-037 | Applicable completion | compliant | See the ledger. |
| ES-038 | Honest conformance | not assessed | The [ledger](engineering-conformance.yml) and this page. |
| ES-039 | Shared quality bar | not assessed | `CLAUDE.md` applies to human and agent contributors alike. |
| ES-040 | Accountable delegation | not assessed | Delegation envelope in `CLAUDE.md`. DEV-017: the deploy identity is broader than its job (accepted for dev, with conditions). |
| ES-041 | Production AI configuration and evals | not assessed | The site has no AI features. |
| ES-042 | Untrusted context and development access | not assessed | |
| ES-043 | Deadlines and cancellation | not assessed | Planned with the forms client (Phase 4). |
| ES-044 | Safe retries | not assessed | Planned with the forms client (Phase 4). |
| ES-045 | Overload | not assessed | |
| ES-046 | Lifecycle and failure testing | not assessed | Planned with the forms client (Phase 4). |
| ES-047 | Bounded execution | not assessed | DEV-015 (single replica, accepted for dev) applies. Azure budget with alerts in the stack (`infra/UmbracoSample.Infra/SiteInfrastructure.cs`); Lighthouse byte and request budgets. |
| ES-048 | AI and telemetry budgets | not assessed | Log Analytics with a 30-day retention cap. |
| ES-049 | Inventory and immutable promotion | unknown | DEV-014 (image not signed or attested). One image built per commit and promoted by digest. |
| ES-050 | Attestations and verification | deviation | DEV-014: the published image is not signed or attested. |
| ES-051 | Actionable vulnerability evidence | not assessed | SBOM produced by the image job. |
| ES-052 | Execution interface | compliant | Makefile targets, run the same way locally and in CI. |
| ES-053 | Isolated development | compliant | `make validate` before handoff; Aspire runs the stack locally. |
| ES-054 | Capabilities and approval | deviation | DEV-001 (admin bypass), DEV-017 (standing deploy identity). |
| ES-055 | Preview and reversibility | not assessed | Every change is one revertible pull request. `pulumi preview` runs only after merge on `main` or by hand, prints no diff (ADR 0014), and the dev deploy runs `up` without a preview step; a preview on every pull request (Phase 5) is not built. |
| ES-056 | Receipts and resumability | not assessed | Pull requests and version history are the receipts (`CLAUDE.md`). |
| ES-057 | Failure containment | not assessed | |
| ES-058 | Ownership, purpose, and contracts | not assessed | Planned with forms: the site keeps no submissions (Phase 4). |
| ES-059 | Retention and deletion propagation | not assessed | Planned with forms (Phase 4). |
| ES-060 | Triggered adoption | not assessed | Pull request preview environments are deferred until their trigger exists (ENV-04). |
| ES-061 | Bench verification | not_applicable | No hardware; see the ledger. |

## Framework requirements

| ID | Requirement | Status | Evidence or ledger entry |
|---|---|---|---|
| ARC-01 | Pages built from reusable components | partly | Hero, card grid, call to action and rich text as Block List components (`src/UmbracoSample.Web/Components`). The form block comes with forms (Phase 4). |
| ARC-02 | Components versioned, documented and tested once, reused everywhere | partly | [Component pages](components/README.md) and contract tests for empty, minimal and maximal content. Components are versioned only with the repository, not individually. |
| ARC-03 | Content model in source control | met | uSync files in `src/UmbracoSample.Web/uSync/v17`. |
| ARC-04 | Schema flows up through Git; content authored only in production | partly | Schema flows through Git and uSync; dev has only synthetic content. Production content authoring starts with a production environment, which is deferred (dev only for now). |
| ARC-05 | Design tokens synced from the design tool into code | partly | Tokens in `frontend/tokens` build the stylesheet and are contrast-tested (ADR 0007). No sync from a design tool. |
| ARC-06 | Forms native in the site, posting to the marketing automation platform | planned | Phase 4, after the owner reviews threat model 0001. |
| ARC-07 | Analytics and A/B test hooks in components | gap | Not started; no tracker is on the site, and any would need the third-party allowlist (ADR 0008). |
| ARC-08 | Clean structured data for AI search engines | met | JSON-LD on every page (`src/UmbracoSample.Web/StructuredData`), unit tests and `StructuredDataTests` (integration). |
| ENV-01 | All Azure resources as code; no manual changes | partly | Pulumi C# stack (ADR 0010) and CD for dev (ADR 0013). The scheduled drift check (`pulumi refresh --preview-only`) is not built yet (Phase 5). |
| ENV-02 | Dev, staging and production with a promotion path | deferred | Owner decision, 2026-11-25: dev only for now (recorded in [the plan](plan.md), Phase 5). Trigger: the owner plans staging or production. The image is promoted by digest, so a later stage reuses the same artifact. |
| ENV-03 | Every deployment automated and reversible | partly | CD deploys dev from `main` after every gate (ADR 0013). Rollback follows ES-014: roll forward by default, previous revision only when compatible. |
| ENV-04 | A preview environment per pull request | deferred | Under ES-060 until its trigger exists. |
| ENV-05 | Feature flags | gap | Not started. |
| ENV-06 | Schema drift fails the build | met | `/health/schema` and the integration test `Schema_matches_the_uSync_files_after_first_boot`; the deploy smoke test checks `/health/schema`. |
| TST-01 | Lint, format and type checks on every commit | met | `make format-check`, warnings as errors, analyzers, stylesheet rules (`make frontend-test`). |
| TST-02 | Unit tests with a coverage bar | partly | Unit tests on every commit; weekly mutation testing of the domain fails below 75%. Coverage is not reported yet. The plan reads TST-02 under ES-002: coverage as a diagnostic with a floor only on critical modules ([plan](plan.md), "Where the framework and standard differ"). |
| TST-03 | Bundle size and image optimization limits | partly | Stylesheet size limit (`frontend/scripts/check-bundle-size.mjs`) and Lighthouse byte budgets, including 300 KB of images per page. No image format or responsive-image check. |
| TST-04 | Dependency, static code and secret scanning | met | `make vuln`, `make frontend-audit`, CodeQL, gitleaks (`make secrets`), Dependabot. As every gate, bypassable by admins (DEV-001). |
| TST-05 | Contract tests: every component with empty, minimal and maximal content | met | `ComponentContractTests` and the integration fixture pages. |
| TST-06 | Schema drift detection | met | As ENV-06. |
| TST-07 | Form contract tests against a sandbox | planned | Phase 4: a WireMock stub with documented fidelity limits. |
| TST-08 | Every legacy URL resolves to a live page | met | `redirects.csv` parsed at startup, property and corpus tests, and `RedirectTests` following every rule. |
| TST-09 | Security headers and content security policy checks | met | `SecurityHeadersTests` and `SecurityTests` (integration); the deploy smoke test checks the policy. |
| TST-10 | Tracker allowlist; unapproved tags fail the build | met | `ThirdPartyAllowlist.cs` (ADR 0008) and `Pages_load_nothing_the_policy_does_not_allow`. |
| TST-11 | Visual regression | partly | Visual baselines of the fixture pages (`e2e/tests/visual.spec.mjs`). There are no real pages yet, only synthetic ones. |
| TST-12 | End-to-end tests on critical paths | partly | Navigation, skip link, links and 404 journeys (`e2e/tests/journeys.spec.mjs`). Search and forms are not built yet. |
| TST-13 | Automated accessibility scans against WCAG 2.2 AA | met | axe on every seeded page (`e2e/tests/accessibility.spec.mjs`), a required check. |
| TST-14 | Performance budgets | met | Lighthouse CI with byte and request budgets (`e2e/lighthouserc.cjs`). |
| TST-15 | SEO checks | partly | Lighthouse SEO category at 0.9 or above, canonical link, structured data tested. No broken-link crawl yet. |
| TST-16 | Production smoke tests with automatic rollback | partly | `scripts/smoke.sh` after every dev deploy. Rollback follows ES-014 (see ENV-03); production is deferred. |
| TST-17 | Cross-browser and mobile visual checks | gap | Not started. |
| CMP-01 | Components meet WCAG 2.2 AA | partly | Images without alt text are not rendered, heading rules in the blocks, contrast-tested tokens, axe scans. DEV-008: no manual assistive-technology check yet. |
| CMP-02 | Accessibility and tracker gates on every pull request | partly | The e2e and integration jobs are required checks (ruleset main-2); admins can bypass them (DEV-001), and the ledger records the accessibility gate under DEV-008. |
| CMP-03 | Legal and privacy approval for sensitive content changes | outside the code | An editorial workflow commitment; Umbraco workflow configuration would be the place to enforce it. |
| CMP-04 | Authors guided to alt text and plain language | partly | The image alt-text field explains why it matters, and images without it are not rendered; the field is not mandatory. No plain-language guidance yet. |
| CMP-05 | Scheduled accessibility, cookie and tracker scans | gap | Not started; scans run only on pull requests and `main`. |
| CMP-06 | Consent banner blocks tags until opt-in | not applicable | The site loads no trackers or third-party tags (ADR 0008); applies when the first one is allowed. |
| CMP-07 | Yearly manual accessibility audit | gap | DEV-008. |
| OPS-01 | Web application firewall in front of the site | planned | Front Door with WAF rules (Phase 5, ADR 0010). |
| OPS-02 | Third-party penetration test | outside the code | |
| OPS-03 | Real user monitoring, uptime checks and alerting | partly | Server-side OpenTelemetry to Application Insights. An availability test of `/health/ready` from three regions with an email alert is declared for always-on stacks (`uptimeTest`, on in prod, mock-tested in `SiteInfrastructureTests`); it is off in dev, which scales to zero, and prod is not deployed, so no alert has fired yet; prod waits on the edge (DEV-010) and the owner's environment plan. No real user (browser) monitoring. |
| OPS-04 | SLOs for availability and page speed | gap | Not started. |
| OPS-05 | Backups with tested restores | planned | Media: blob and container soft delete declared, 7 days in dev and 30 in prod (`Media_soft_delete_is_declared`); it does not cover deleting the storage account itself. Database: Azure SQL automatic backups. A weekly restore test in dev (`restore-test.yml`, ADR 0019) restores the database to a copy, checks it inside the network (`RestoreCheck`) and deletes it, timing the restore; no run has proved it yet. Media restore is not exercised: the deploy identity has no data-plane role on storage. |
| OPS-06 | Incident runbooks and on-call | outside the code | |
| OWN-01 | One team owns the repository, environments and pipeline; no direct production access | partly | Infrastructure only through Pulumi and the pipeline; dev deploys only through CD (ADR 0013). Production is deferred. |
| OWN-02 | All work arrives as pull requests and merges after review | partly | Every change is a pull request with an independent review (delegation envelope); not enforced for admins (DEV-001). |
| OWN-03 | Named product owner and marketing counterpart | outside the code | |
| OWN-04 | Legal and privacy own regulated content approval | outside the code | |
| OWN-05 | A durable product team with a roadmap | outside the code | |
| GOV-02 | Every page has an owner and a review date | partly | Content owner and review date on every page type (`contentGovernance` composition, ADR 0018), kept there by `GovernanceSchemaTests`. Optional in the editor, so detected rather than enforced. |
| GOV-03 | Expired or ownerless pages flagged | partly | The Content governance health check flags ownerless, undated, overdue and due-soon pages on demand (`PageReviewTests`, `GovernanceReportTests`). Not yet on a schedule. |
| GOV-04 | ADRs for major choices | met | [ADRs](adr/README.md); a policy test keeps the index complete. |
| GOV-05 | Coding standards and definition of done, enforced in review | partly | `CLAUDE.md`, the engineering standard and the review rule in the delegation envelope. Review is not enforced for admins (DEV-001). |
| GOV-06 | What belongs on the site versus a no-code tool | outside the code | |
| MSR-01 | DORA metrics | gap | Not started; deploy history is in the Actions runs of `ci.yml`. |
| MSR-02 | Business outcome metrics | outside the code | |
| MSR-03 | A/B testing through components | gap | As ARC-07. |
| AI-01 | AI review of every pull request | partly | Agent pull requests get an independent reviewer context (delegation envelope). Not automated for every pull request. |
| AI-02 | AI-assisted test generation for new components | outside the code | A way of working, not something the repository can show. |
| AI-03 | AI checks on content before publish | gap | Not started. |
| AI-05 | Site readable by AI search engines | met | As ARC-08. |
