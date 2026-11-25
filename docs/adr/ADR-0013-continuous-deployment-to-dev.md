# 0013. Continuous deployment to dev

- Status: Proposed (direction chosen by the owner on 2026-11-24; the deploy identity's rights wait on threat model 0002, R4)
- Date: 2026-11-24
- Decider: owner
- Clauses: ES-011, ES-017, ES-019, ES-024, ES-030, ES-040, ES-048, ES-054; framework ENV-01, OPS-01
- Supersedes: the "only the owner runs `pulumi up`" part of ADR 0010, for the dev stack only

## Context

ADR 0010 kept every `pulumi up` on the owner's machine. The first dev deploy showed what that costs: the owner copies the image digest into stack config, installs tools, runs `up`, and repeats it after each fix. Every other part of the repository is automated and checked in CI, so a hand-run deploy is the odd one out, and a stale local stack file could quietly roll dev back to an older image.

## Alternatives considered

- **Keep `up` on the owner's machine:** no standing deploy identity, but every deploy is manual, the image digest is copied by hand, and dev drifts from `main`.
- **CD for dev and prod, with a required reviewer on prod:** the end state, but prod has no edge yet (DEV-010) and does not exist; deciding its gate now is premature.
- **CD for dev only (chosen):** every `main` revision that passed all gates is deployed to dev by digest and smoke-tested. Prod keeps the owner-run `up` until it is designed.

## Decision

- A `deploy-dev` job in `ci.yml` runs after `publish`, only on `main`, only when the owner sets `CD_DEV_ENABLED=true`. It signs in to Azure and Pulumi Cloud with OIDC (no stored secret), runs `pulumi up` for dev with `SITE_IMAGE` set to the digest `publish` just pushed, then runs `scripts/smoke.sh` against `siteUrl`.
- Pulumi operations on the dev stack run one at a time: the deploy and the infra preview share `concurrency: pulumi-dev`, never cancelled midway. A run deploys only if its commit is still the head of `main`, so an older run that reaches the job late cannot roll dev back.
- Dev's committed stack file pins no image. The image comes from `SITE_IMAGE` (CD, or `scripts/published-image.sh` for a local preview), so a stale file cannot roll dev back. Prod still pins its image in stack config.
- The job runs in a GitHub environment `dev` restricted to `main`, with no required reviewer: merging to `main` is the approval. An agent merging its own pull request under the delegation envelope would therefore deploy dev, which the envelope does not yet allow (it forbids agents to deploy to Azure without the owner's approval). So `CD_DEV_ENABLED` stays unset until the owner amends the envelope (ES-040, ES-054); agents cannot widen it themselves.
- The deploy identity is a separate app registration from the preview's Reader identity, federated only to `environment:dev` of this repository. It needs Contributor on the subscription (the stack creates its own resource group and budget), Role Based Access Control Administrator limited by condition to the three data-plane roles the stack grants, and membership of the SQL administrator group so the database user step can run. Those rights are the risk R4 in threat model 0002, for the owner to accept. The workflow only targets the dev stack, but the credentials do not keep it there: an organization Pulumi token and subscription-scope roles would also reach a prod stack in the same Pulumi organization and subscription.

## Consequences

- Dev always runs what `main` last published, and every deploy is a CI run with its log, diff and smoke result (ES-056).
- A merged change that is malicious or broken reaches dev without a further human step. Review before merge (ES-028), the gates before `publish`, and the smoke test are the controls; dev holds only synthetic content (ES-027). Review before merge is not yet enforced for admins (DEV-001).
- A failed smoke test leaves the new revision in place; there is no automatic rollback. To recover, fix forward, or re-run the `deploy-dev` job of the last good run (Actions, that run, Re-run jobs), which deploys its digest again once it is the newest run allowed to deploy (see `docs/infra-setup.md`).
- The deploy identity is a standing, privileged identity. It is usable only from a `main` run in the `dev` environment, and only for this repository's subject.
- The owner no longer runs `up` for dev. Running it locally still works (set `SITE_IMAGE`), but CD is the normal path.
- Revisit when prod is designed (its own environment with a required reviewer), when the edge is added, or if the deploy identity's rights can be narrowed to the resource group.
