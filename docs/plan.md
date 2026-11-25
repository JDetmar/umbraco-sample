# Umbraco sample: build-out plan

Approved by the owner on 2026-11-24. Changes to the plan are made by pull request.

## Goal

A public reference repository that shows a corporate CMS site built inside engineering guardrails. Every requirement in `docs/engineering-rigor-framework.md` (OWN/ARC/ENV/TST/CMP/OPS/GOV) and every applicable clause of the engineering standard (ES-001 to ES-061) points to real evidence: code, a test, a CI check, or a document. Gaps are recorded honestly in the conformance ledger rather than implied.

## Assumptions

- Umbraco 17 LTS on .NET 10.
- .NET Aspire for local orchestration: one AppHost runs the site, a SQL Server container, Azurite for media and the MAP stub. SQL Server everywhere (no SQLite), so local and CI match Azure SQL.
- Pulumi (C#, Azure Native provider) for all Azure infrastructure, with state and config secrets in Pulumi Cloud. Aspire is not used for deployment.
- Real deploys to a low-cost Azure subscription, so cost is a design constraint (see Hosting cost).
- GitHub Actions for CI, since the repo is on GitHub.
- Marketing automation is a stub "MAP" service standing in for any marketing automation platform.
- Repo is public and purely technical: no organisation-specific names, history or business context.
- Solo operating model (standard, Applicability): the owner is final reviewer and approver of deviations. An agent authors; the owner reviews every PR (ES-028: the same agent context must not be sole author and reviewer).

## Phase 0: Guardrails before code

| Deliverable | Evidence for |
|---|---|
| `docs/engineering-conformance.yml` from the ledger template; every clause starts `unknown` | ES-038 |
| `CLAUDE.md` / `AGENTS.md`: canonical commands, repo rules, and the agent's delegated envelope (may push branches and open PRs; may not merge, deploy or change settings) | ES-052, ES-053, ES-054, ES-040 |
| Canonical command interface (`./build.sh` or `just`): bootstrap, build, test, validate (fast), run; CI calls the same commands | ES-052 |
| Aspire AppHost (`dotnet run --project src/AppHost`) as the canonical local run, plus a seed step with synthetic content only | ES-053, ES-027, ES-006 |
| `global.json` SDK pin, NuGet lock files, `package-lock.json` | ES-013 |
| ADR-0001 platform (Umbraco), ADR-0002 local orchestration (Aspire), ADR-0003 IaC (Pulumi, state backend, secrets), ADR-0004 hosting (Container Apps vs App Service), ADR-0005 test stack, ADR-0006 dependency policy | ES-019, GOV-04 |
| `docs/dependencies.md`: justification per dependency (health, footprint, licence, security, exit cost) for owner approval | ES-015 |
| PR template with the definition of done; CODEOWNERS; branch protection on `main` | ES-037, ES-011, OWN-02, GOV-05 |
| CI skeleton: Actions pinned to commit SHAs, least-privilege `permissions:`, format/lint/analyzers as errors, secret scan (gitleaks), CodeQL, Dependabot | ES-012, ES-016, ES-017, ES-024, TST-01, TST-04 |

## Phase 1: Walking skeleton

A deployed, tested, observable "hello page" before any real features (ES-019 SHOULD).

- Umbraco site, uSync for content types and data types (ARC-03), uSync drift check in CI (ENV-06, TST-06).
- OpenTelemetry instrumentation, health endpoints, one smoke test (ES-030).
- Build once into an immutable container image with a digest and SBOM; the same image is promoted to every environment (ES-049).
- Deploy target depends on your answer about Azure (question 3).

## Phase 2: Component library

- Block List / Block Grid components: Hero, Card grid, CTA, Rich text, Form (ARC-01, ARC-02).
- Each component ships with docs, fixtures and contract tests for empty, minimal and maximal content (TST-05, ES-001).
- Design tokens JSON to CSS variables (ARC-05).
- Required alt text and heading-structure rules built into the blocks (CMP-01, CMP-04, ES-035).
- No invented domain layers: view models stay thin. A domain project exists only for real logic (redirect rules, form validation), and an architecture test (NetArchTest) proves it never references Umbraco (ES-018, ES-020).

## Phase 3: Quality gates

- Playwright end-to-end tests on critical journeys, axe scans in the authoritative gate, visual regression on fixtures (TST-11, TST-12, TST-13, ES-036, ES-006).
- Redirect map: parsed into precise types, property-based tests, a retained fuzz corpus (TST-08, ES-021, ES-002, ES-007).
- Security headers/CSP and tracker allowlist checks (TST-09, TST-10).
- Lighthouse budgets (TST-14, ES-047).
- Coverage reported as a diagnostic, with a floor only on critical modules (ES-002; see "Where the framework and standard differ").
- Fast lane target of about 10 minutes (ES-012).

## Phase 4: Forms to the MAP

- Threat model **before** implementation, for owner approval: public input, personal data, rate limits, bots (ES-023, ES-026).
- The site does not store submissions; it forwards to the MAP and keeps nothing, which keeps ES-058/059 small (ES-025 minimisation).
- A submitted lead counts as "value" under the standard (a record promised never to be lost), so: idempotency key, concurrent double-submit test, partial-failure test (ES-022).
- MAP client: deadlines, bounded retries with jitter, explicit retry ownership, outcome reconciliation on timeout (ES-043, ES-044, ES-046).
- Contract tests against a WireMock stub with documented fidelity limits (TST-07, ES-006).
- No personal data in logs or traces; verified by test (ES-025).

## Phase 5: Azure infrastructure

- Pulumi C# project (`infra/`) for the container host, Azure SQL, Blob storage for media, Front Door with WAF, Key Vault and Application Insights via the OpenTelemetry exporter (ENV-01, OPS-01, OPS-03).
- One Pulumi stack per environment (dev, staging, prod); provider and package versions pinned with lock files (ES-013).
- `pulumi preview` runs on every PR and its diff is posted for review; `pulumi up` runs only from the pipeline after merge (ES-055, ES-012). No manual changes: a scheduled `pulumi refresh --preview-only` flags drift (ENV-01).
- GitHub to Azure via OIDC federated credentials; no stored cloud secrets (ES-024). Pulumi config secrets encrypted by Pulumi Cloud (ES-024).
- An Azure budget with alerts is part of the stack (ES-047).
- Dev, staging and prod with a pipeline promotion path (ENV-02, ENV-03).
- Managed identity and Key Vault; no long-lived secrets (ES-024).
- Backups with a scripted restore test (OPS-05, ES-031).
- PR preview environments deferred under ES-060 until their trigger exists (ENV-04).

## Hosting cost

Rough list prices, to confirm with the Azure pricing calculator before Phase 5. Infrastructure is designed so expensive pieces are config toggles.

| Piece | Lean choice | Approx. per month |
|---|---|---|
| Site host | Container Apps consumption; prod keeps 1 small replica warm (Umbraco cold starts are slow), dev and staging scale to zero | $25-35 prod, ~$0 non-prod |
| Database | Azure SQL free offer or serverless with auto-pause | $0-10 per env |
| Container registry | GitHub Container Registry (public image) | $0 |
| Media, Key Vault, App Insights | Blob storage, Key Vault, App Insights free ingestion tier | a few dollars |
| Edge and WAF | Front Door Standard with custom WAF rules (rate limiting, geo); managed OWASP rule sets need Premium | ~$35-40 (Premium ~$330, off by default) |

Expected total: about $60-90/month with Front Door Standard in prod, or about $30-40 without it. Front Door Premium would be a toggle for demos only. Running Standard instead of Premium means OPS-01 is only partly met, recorded in the ledger as a gap with the toggle as its fix.

## Phase 6: Governance features

- Every page has an owner and a review date, with a backoffice dashboard flagging expired or ownerless pages (GOV-02, GOV-03).
- Structured data (JSON-LD) on pages (ARC-08). Done: `StructuredData/PageStructuredData.cs`, its unit tests and `StructuredDataTests` (integration).
- Backoffice audit events forwarded to an append-only log store (ES-026).
- `docs/traceability.md` maps every framework ID and ES clause to its evidence, or to a ledger entry. Done; `TraceabilityPolicyTests` keeps it complete and in step with the ledger.

## Where the framework and standard differ

The standard governs; the framework text gets corrected.

1. **TST-02 "minimum coverage bar on new code"** conflicts with ES-002 (coverage is diagnostic only). Plan: report coverage, and floor only critical modules.
2. **ENV-03 / TST-16 "every deployment can be rolled back" / "automatic rollback"**: ES-014 makes roll-forward the default and allows automated rollback only when the previous artifact stays compatible. Plan: revision traffic shift back for compatible releases; roll forward otherwise.

## Where CMS practice departs from the standard (deviations for the owner to approve)

1. **Production content copied to lower environments (ES-027).** CMS teams routinely restore published content to staging to test against real pages. ES-027 forbids raw production data in dev/test. Proposal: an accepted risk limited to published, public content only, with members, users, form data and audit logs excluded and scrubbed. Dev and CI stay on synthetic seed content.
2. **Umbraco upgrade migrations (ES-014).** Umbraco's own database migrations on a version upgrade are not expand/contract and can't be rolled back by shifting traffic to the previous revision. Proposal: an accepted risk with compensating controls (pre-upgrade backup, restore rehearsal in staging, upgrades as their own release). Our own content-type changes do follow expand/contract: uSync never deletes in the same release that stops using a property.
3. **Content authored only in production (ARC-04) vs version control (ES-011).** Not a deviation: content is data, not source. Recorded as `not_applicable` with that reason, so it isn't mistaken for a gap.

## Decisions that need the owner

- Approve new dependencies (ES-015); the initial list is approved in `docs/dependencies.md`.
- Approve significant ADRs (ES-019, ES-040).
- Approve the forms threat model before Phase 4 starts (ES-023).
- Approve or reject deviations 1 and 2 above (ES-038; an agent cannot approve its own).
- Whether the engineering standard itself is published in the public repo, or only referenced by version.
