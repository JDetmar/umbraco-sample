# 0014. No Pulumi property diff in CI logs

- Status: Proposed
- Date: 2026-11-25
- Decider: owner
- Clauses: ES-019, ES-025, ES-056. Amends one consequence of ADR 0013.

## Context

ADR 0013 deploys dev from `main` with `pulumi up`, and says each deploy's CI log carries its diff (ES-056). This repository is public, so its Actions logs are public too.

A property diff prints every changed output of a resource. Azure stamps each resource with `systemData`, which records the sign-in name of whoever created or last changed it. On 2026-11-25 the dev deploy updated a container app that an owner had created from their own machine, and the diff printed that person's sign-in name in the public log of run 45677402486 (DEV-019). The value never leaves `systemData`, so every later deploy that touches the resource would print it again.

## Alternatives considered

- **Keep the diff and mask the value.** `::add-mask::` hides only values known before the step runs, and the sign-in names on a resource are not known in advance. Rejected: it would miss the next one.
- **Recreate resources under the deploy identity.** Removes this one name, but any resource someone changes by hand later carries their name again, and it costs a replacement with an outage. Rejected as the fix; still possible for dev if the owner wants the record cleaned.
- **Make the repository's Actions logs private.** Not possible for a public repository.
- **No property diff in CI.** The log keeps the list of operations Pulumi ran (create, update, replace, delete per resource) and the outcome; the full diff stays in Pulumi Cloud's update history, which only the stack's members can read, and `make infra-preview` shows it locally.

## Decision

Every Pulumi step in CI runs with `diff: false`, and no CI step posts a Pulumi diff or summary to a pull request comment or the job summary. A policy test (`Pulumi_runs_in_ci_never_print_a_property_diff`) fails if a workflow turns any of these on, or passes `--diff` or `--show-secrets` to the CLI.

This replaces the word "diff" in ADR 0013's first consequence: a deploy's receipt (ES-056) is its CI run, with the operations Pulumi ran and the smoke result, plus the private Pulumi Cloud update with the full diff.

## Consequences

- Public logs carry no resource property values, so no `systemData`, tags or other outputs from Azure reach them.
- Reading what a deploy changed field by field takes a Pulumi Cloud login or a local preview, not just the Actions log.
- The test checks workflow text, so a Pulumi run started another way (a script the workflow calls) is outside it; the Makefile's `infra-preview` keeps `--diff` because it runs only on a developer's machine.
