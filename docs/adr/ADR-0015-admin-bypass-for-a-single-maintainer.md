# 0015. Admin bypass of required checks for a single maintainer

- Status: Proposed
- Date: 2026-11-25
- Decider: owner
- Clauses: ES-011, ES-012, ES-019, ES-024, ES-028, ES-038, ES-054

## Context

Two rulesets protect `main`. Ruleset main-1 blocks deletion and force-push for everyone. Ruleset main-2 requires the six CI checks on an up-to-date branch, but repository admins can bypass it. No rule requires a pull request or an approving review (DEV-001).

The repository has one maintainer, who is the owner and final reviewer under the standard's solo operating model. A required approving review from someone other than the author cannot be met by one person, and removing the bypass would leave the owner unable to recover `main` if a required check breaks for reasons outside the change, such as a runner or registry outage.

Agents work under the owner's account, so they inherit the bypass. Their merge conditions (green checks, an independent reviewer, the clauses listed in the pull request) rest on the delegation envelope in CLAUDE.md rather than on repository settings.

## Alternatives considered

- **Remove the admin bypass.** Every merge, the owner's included, waits for green checks. A broken check that no change can fix blocks `main` until the owner edits the ruleset, which is the same bypass taken by hand.
- **Give the agent its own non-admin identity**, such as a GitHub App. The agent can no longer bypass, while the owner keeps the bypass. This costs a new identity with its own keys and permissions to manage (ES-024), and is the better control once agents merge unattended.
- **Require pull requests with one approval.** A single maintainer cannot approve their own pull request, so every agent pull request would wait for the owner, which removes the delegated merge.
- **Keep the bypass and accept the risk.** Chosen.

## Decision

Keep the admin bypass on main-2 while the repository has a single maintainer. Record it as accepted risk DEV-001 in the conformance ledger, with review triggers. Agents never use the bypass: the delegation envelope already forbids merging with a failing check, and a bypassed merge by an agent is a review trigger.

## Consequences

- The owner can always recover `main`; agents keep their delegated merge.
- Gates on `main` are enforced for non-admins only. That an agent never bypasses them is shown by pull request records, not prevented by settings.
- Revisit when a second person gets write access, when an agent gets its own identity, when an agent merges past a red check or pushes without a pull request, or when a staging or prod environment deploys from `main`.
