# 0019. Scheduled restore test in dev

- Status: Accepted (owner, 2026-11-26, decision card in the project thread)
- Date: 2026-11-25
- Decider: owner
- Clauses: ES-019, ES-031, ES-047, ES-040; framework OPS-05

## Context

OPS-05 and ES-031 ask for backups with tested restores. The data that matters is in two places:

- **The Azure SQL database.** It has automatic backups with point-in-time restore. Dev uses the free offer.
- **The media blob container.** Before pull request 50 it had no soft delete, so a deleted or overwritten file could not be brought back.

A backup counts only when a restore has been shown to work. The time a restore takes is the recovery time to plan around.

The owner decided that only dev exists for now. Dev holds only synthetic content, so a restored copy carries no sensitive data.

The SQL server accepts connections only from the apps subnet (ADR 0017). A GitHub runner can therefore start a restore, but it cannot query the restored copy.

## Alternatives considered

1. **Weekly scheduled restore test in dev, run by CI.**
   - CI restores the dev database to a temporary copy from one hour earlier and times it.
   - CI starts the existing Container Apps job, inside the network, with a read-only check command. The check confirms the copy has the Umbraco schema and the seeded content.
   - CI deletes the copy in all cases and writes the timings to the run summary.
   - Pros: proves the restore end to end and measures recovery time.
   - Cons: CI creates and deletes a database outside Pulumi, using the dev deploy identity (DEV-017). It costs a few cents a run, because the copy is serverless and is deleted within the hour.
2. **Manual restore drill, documented in a runbook.**
   - Pros: no new automation rights.
   - Cons: depends on someone remembering, and the owner prefers automation to manual runbooks.
3. **Rely on Azure's backup guarantees without testing restores.**
   - Pros: no work.
   - Cons: does not meet OPS-05 or ES-031.

For media, under any option: blob and container soft delete, 7 days outside prod and 30 in prod (pull request 50). Soft delete also keeps overwritten blobs, so versioning is not needed. The restore test also restores one deleted test blob.

## Decision

Option 1, weekly, dev only. The workflow runs only on `main`, in the `dev` environment, and its own policy test limits it to these resources:

- the restore copy named `<database>-restore-test`;
- the job's check command;
- the test blob.

It never touches the live database or other blobs.

## Consequences

- OPS-05 and ES-031 gain evidence from real runs: whether restore succeeded, and how long it took.
- The dev deploy identity is used on a schedule to create and delete one database. This widens the use under DEV-017, which the owner must accept.
- If a run fails before cleanup, the copy is left behind and billed until the next run deletes it. Every run first deletes any leftover copy.
- Prod, when it exists, needs its own decision: restore into a separate resource group, and decide whether a restore test may read prod data.
- Implementation note (2026-11-26): the test blob restore is not built. The deploy identity has no data-plane role on storage and shared keys are off, so it would need a new role grant, which is the owner's call. Media soft delete is declared and mock-tested, but not exercised by a run.
