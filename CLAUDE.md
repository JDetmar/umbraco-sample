# Repository instructions

These instructions apply to every contributor, human or agent (ES-053). `AGENTS.md` points here.

## What this repository is

A public, purely technical reference implementation of a component-based CMS site on Umbraco 17 LTS and .NET 10. It demonstrates the requirements in `docs/engineering-rigor-framework.md` and the owner's engineering standard (v6.2, clauses ES-001 to ES-061).

Keep it technical. Do not add organisation names, business background, vendor-selection material or anything not needed to understand the code.

## Commands

Use the Makefile targets; CI runs the same ones (ES-052).

| Task | Command |
|---|---|
| First-time setup | `make bootstrap` |
| Build the stylesheet from design tokens | `make frontend` |
| Build | `make build` |
| Fast tests | `make test` |
| Fast validation before handoff | `make validate` |
| Integration tests (starts the Aspire stack; needs Docker) | `make test-integration` |
| End-to-end, accessibility (axe) and visual tests against a running site (`make run` first; needs Docker or Podman, `CONTAINER_RUNTIME=podman`) | `make e2e` |
| Re-approve visual baselines after an intended visual change | `make e2e-update-snapshots` |
| Lighthouse performance budgets against a running site (same image as `make e2e`) | `make lighthouse` |
| Mutation testing of the domain (Stryker.NET; weekly in CI) | `make mutation` |
| Build the container image | `make image` |
| Smoke-test a running site | `make smoke URL=<base url>` |
| Run locally (Aspire: site and SQL Server) | `make run` |
| Apply formatting | `make format` |
| Infrastructure build and Pulumi mock tests (no cloud account) | `make infra-test` |
| Preview a stack (owner sign-in to Pulumi Cloud and Azure; never `up`) | `make infra-preview STACK=dev` |

Prerequisites: GNU Make, the exact .NET SDK in `global.json` (`make bootstrap` restores the pinned local tools, including the Aspire CLI that the AppHost build needs), Node.js 26 LTS (for `make frontend`; any 26.x works locally, CI uses the exact version in `.nvmrc`), Docker or Podman, and gitleaks 8.30.1 (for `make secrets` and `make validate`).

`make run` needs one local secret, the admin password for the unattended install:

```sh
dotnet user-secrets --project src/UmbracoSample.AppHost set "Parameters:umbraco-admin-password" "<a strong local password>"
```

Agents MUST run `make validate` before handing off a change, or record why it could not complete (ES-053).

## Layout

| Path | Contents |
|---|---|
| `src/UmbracoSample.Web` | The Umbraco site |
| `src/UmbracoSample.AppHost` | Aspire orchestration for local development and integration tests |
| `src/UmbracoSample.ServiceDefaults` | Telemetry, health checks and resilience shared by services |
| `src/UmbracoSample.Domain` | Pure domain logic with no framework or package references (ADR 0009), such as the redirect map parser |
| `src/UmbracoSample.Web/redirects.csv` | The redirect map: `source,target,kind`, parsed at startup (ADR 0009) |
| `src/UmbracoSample.Web/Security` | Security headers, the content security policy and the third-party allowlist (ADR 0008) |
| `src/UmbracoSample.Web/Components` | Component view models and the mapping from blocks; views in `Views/Partials/Components` |
| `src/UmbracoSample.Web/uSync/v17` | Schema (and synthetic seed content) as code, imported on first boot and checked for drift |
| `tests/UmbracoSample.Repository.Tests` | Fast policy tests over the repository itself |
| `tests/UmbracoSample.Domain.Tests` | Property, example and corpus tests for the domain, and the architecture rule that keeps it framework-free |
| `tests/UmbracoSample.Web.Tests` | Fast unit and component contract tests |
| `tests/UmbracoSample.Integration.Tests` | Tests against the real stack through Aspire (`Category=Integration`) |
| `frontend/` | Design tokens (`tokens/`), stylesheet sources and the build (ADR 0007); output goes to `wwwroot/dist`, never committed |
| `e2e/` | Playwright journeys, axe scans and visual baselines; run by `scripts/e2e.sh` inside the image pinned in `e2e/Dockerfile` |
| `infra/` | Pulumi (C#) project and its mock tests (ADR 0010); owner setup in `docs/infra-setup.md` |
| `scripts/` | Operational scripts, such as the post-deployment smoke test |
| `docs/components/` | One page per component: fields, rules, fixtures and tests |
| `docs/architecture.md` | Architecture diagrams (Mermaid) of the deployed site and the delivery path; updated with any `infra/` change that alters them |
| `docs/adr/` | Architecture decision records (ES-019) |
| `docs/engineering-conformance.yml` | Conformance ledger: evidence and open deviations (ES-038) |
| `docs/dependencies.md` | Dependency register with ES-015 justifications |

## Rules that are easy to miss

- Every new dependency needs an entry in `docs/dependencies.md` and owner approval before it is merged (ES-015).
- Visual values come from design tokens in `frontend/tokens`; stylesheets never use raw colours, and every colour pair the components use is contrast-tested.
- npm dependencies (in `frontend/` and `e2e/`) are exact versions with a committed lock file and no install scripts.
- Visual baselines change only on purpose: run `make e2e-update-snapshots`, look at every changed image, and say why in the pull request. Baselines assume the seeded content: if you have edited content in your local back office, recreate the local database before running `make e2e`.
- `@playwright/test` and the image in `e2e/Dockerfile` are updated together; a policy test fails while their versions differ.
- No third-party script, style, font, image host or frame without an entry in `Security/ThirdPartyAllowlist.cs` (ADR 0008); the content security policy is built from it, and integration tests fail on anything else. Site markup has no inline script, `<style>`, `style` attribute or `on*` handler.
- Redirects go in `redirects.csv`, always to the final page: the site refuses to start on a chain, loop, duplicate or unsafe target, and CI follows every rule. Any input that ever breaks the parser is saved under `tests/UmbracoSample.Domain.Tests/Redirects/corpus/`.
- `UmbracoSample.Domain` never references Umbraco, ASP.NET Core or any package; an architecture test enforces it (ES-018, ES-020).
- Package versions live only in `Directory.Packages.props`; lock files are committed and CI restores in locked mode (ES-013).
- Third-party GitHub Actions are pinned to a full commit SHA with the tag in a comment, and every workflow declares least-privilege `permissions` (ES-017).
- No secrets in source, config or CI definitions (ES-024). Local secrets go in user secrets.
- Schema changes (document types, data types, templates) are made in the local backoffice under `make run`; uSync exports them on save into `src/UmbracoSample.Web/uSync/v17`, and those files are committed. Production never exports and only imports settings, and `/health/schema` reports drift.
- Umbraco writes a generated `Imaging:HMACSecretKey` into `appsettings.json` on a fresh install when none is configured; never commit it (a policy test fails if you do).
- Development and CI use synthetic content only; production data never enters them (ES-027).
- Significant decisions get an ADR in `docs/adr/` with context, alternatives, decision and consequences (ES-019). Supersede accepted ADRs rather than rewriting them.
- A known gap is recorded in the ledger, never implied away (ES-038). Agents never approve their own exception or accepted risk.

## Agent delegation envelope (ES-040, ES-054)

Delegated by the owner on 2026-11-24. Authorization must be enforced by GitHub permissions and branch protection, not by this text; until branch protection is enabled that enforcement is missing, recorded as DEV-001 in the ledger.

Agents MAY, without asking:
- Create branches, push commits and open pull requests.
- Merge their own pull request into `main` when all of these hold:
  1. Every required CI check is green.
  2. A reviewer in a separate agent context, given only the diff, this file and the standard, has reviewed the change, and every finding is fixed or answered in the PR (ES-028: the same agent context is never the sole author and reviewer).
  3. The PR body lists the ES clauses and framework IDs the change touches, with the evidence for each.

Bounds on delegated work (ES-048, ES-054):
- Blast radius: this repository only, apart from the dev environment that CD deploys from `main` (ADR 0013). No other repository, cloud subscription, package registry or external service is changed.
- Every change is a pull request that can be reverted with one revert commit; force-pushes only on the agent's own unmerged branches.
- An unattended run stops after its stated time budget (8 hours for the first build-out run), and at any point where a step needs owner approval, failing CI cannot be fixed within three attempts, or a finding cannot be resolved without changing scope.

Agents MUST NOT, without the owner's explicit approval:
- Deploy to any Azure environment or run `pulumi up`. The one exception is CD: once the owner enables it, the `deploy-dev` job deploys a `main` revision to dev after CI passes, if it is still the head of `main` (ADR 0013), so merging your own pull request can deploy dev. Prod is never deployed by an agent or by CD.
- Change repository settings, branch protection, secrets or GitHub App permissions.
- Approve an exception, accepted risk or emergency in the ledger.
- Add a dependency that is not already approved in `docs/dependencies.md`.
- Rewrite published history on `main`.

Version-control history and PR records are the action receipts (ES-056). The owner can revoke this envelope at any time by editing this section.
