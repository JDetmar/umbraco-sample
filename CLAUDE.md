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
| Build | `make build` |
| Fast tests | `make test` |
| Fast validation before handoff | `make validate` |
| Run locally (Aspire: site and SQL Server) | `make run` |
| Apply formatting | `make format` |

Prerequisites: the .NET SDK in `global.json`, Docker or Podman, and Node.js 22.

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
| `tests/` | Test projects |
| `docs/adr/` | Architecture decision records (ES-019) |
| `docs/engineering-conformance.yml` | Conformance ledger: evidence and open deviations (ES-038) |
| `docs/dependencies.md` | Dependency register with ES-015 justifications |

## Rules that are easy to miss

- Every new dependency needs an entry in `docs/dependencies.md` and owner approval before it is merged (ES-015).
- Package versions live only in `Directory.Packages.props`; lock files are committed and CI restores in locked mode (ES-013).
- Third-party GitHub Actions are pinned to a full commit SHA with the tag in a comment, and every workflow declares least-privilege `permissions` (ES-017).
- No secrets in source, config or CI definitions (ES-024). Local secrets go in user secrets.
- Development and CI use synthetic content only; production data never enters them (ES-027).
- Significant decisions get an ADR in `docs/adr/` with context, alternatives, decision and consequences (ES-019). Supersede accepted ADRs rather than rewriting them.
- A known gap is recorded in the ledger, never implied away (ES-038). Agents never approve their own exception or accepted risk.

## Agent delegation envelope (ES-040, ES-054)

Delegated by the owner on 2026-11-24. Authorization is enforced by GitHub permissions and branch protection, not by this text.

Agents MAY, without asking:
- Create branches, push commits and open pull requests.
- Merge their own pull request into `main` when all of these hold:
  1. Every required CI check is green.
  2. A reviewer in a separate agent context, given only the diff, this file and the standard, has reviewed the change, and every finding is fixed or answered in the PR (ES-028: the same agent context is never the sole author and reviewer).
  3. The PR body lists the ES clauses and framework IDs the change touches, with the evidence for each.

Agents MUST NOT, without the owner's explicit approval:
- Deploy to any Azure environment or run `pulumi up`.
- Change repository settings, branch protection, secrets or GitHub App permissions.
- Approve an exception, accepted risk or emergency in the ledger.
- Add a dependency that is not already approved in `docs/dependencies.md`.
- Rewrite published history on `main`.

Version-control history and PR records are the action receipts (ES-056). The owner can revoke this envelope at any time by editing this section.
