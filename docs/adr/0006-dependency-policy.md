# 0006. Dependency policy

- Status: Accepted
- Date: 2026-11-24
- Decider: owner
- Clauses: ES-019, ES-013, ES-015, ES-016, ES-017

## Context

Every dependency is a long-term ownership cost and a supply-chain risk. The standard requires a recorded justification (ES-015), automated update discovery and vulnerability scanning (ES-016), pinned inputs (ES-013) and immutable references for CI actions and agent tooling (ES-017).

## Alternatives considered

- **Floating versions:** less churn, but builds become irreproducible (ES-013).
- **Vendor everything:** maximum control, but a heavy maintenance burden for a sample.

## Decision

- `docs/dependencies.md` is the register. A dependency is added only after its entry is approved by the owner.
- NuGet versions are centrally managed in `Directory.Packages.props`, lock files are committed and CI restores in locked mode. npm uses `package-lock.json` and `npm ci`.
- The .NET SDK is pinned in `global.json` (`latestPatch`).
- Third-party GitHub Actions are pinned to full commit SHAs with the tag in a comment; tools downloaded in CI are verified against published checksums.
- Dependabot proposes updates weekly for NuGet, npm and GitHub Actions; `make vuln` fails the build on known-vulnerable packages, including transitive ones.
- A suppressed vulnerability finding records its rationale and a review or expiry condition in the ledger (ES-016).

## Consequences

- Updates arrive as reviewable pull requests rather than silently.
- New dependencies wait for owner approval, which slows experiments; spikes stay on branches until approved.
