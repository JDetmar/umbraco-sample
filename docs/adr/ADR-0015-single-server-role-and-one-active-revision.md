# 0015. Single Umbraco server role and one active revision

- Status: Accepted by the owner on 2026-11-25 (project thread decision, "One active revision")
- Date: 2026-11-25
- Decider: owner
- Clauses: ES-019, ES-014, ES-030. Amends ADR 0004's rollback by traffic shift.

## Context

ADR 0004 contains a failed compatible release by shifting traffic back to the previous revision, so the container app ran in multiple-revisions mode and kept older revisions active.

Every revision shares one database. Umbraco elects server roles through that database, so on 2026-11-25 a new dev revision started as a Subscriber while the previous one was still running, and uSync skipped its startup import (dev served "No published content"). The site is one replica by design (DEV-015), so it now declares itself the single Umbraco server.

A single server always runs the uSync startup import. With older revisions still active, any older replica that starts (a traffic shift back, or a request to its own revision URL, which is public) would import its older uSync files and roll the shared schema back, and in dev the content too. Traffic shifting is also a weaker rollback than ADR 0004 assumed: the new revision's import has already changed the shared database by the time traffic moves back.

## Alternatives considered

- **Keep multiple revisions, elected roles.** The newest revision can start as a Subscriber and skip its import, as happened. Rejected.
- **Keep multiple revisions, single role.** Fixes the import, but an older revision that wakes rolls the database back. Rejected.
- **Deactivate old revisions in the deploy job.** Same effect as single-revision mode with more moving parts. Rejected.
- **One active revision, single role (chosen).**

## Decision

The site registers Umbraco's `SingleServerRoleAccessor` (`ServerRoleComposer`), and the container app runs with `ActiveRevisionsMode.Single`, so each deploy deactivates the previous revision. Rollback is a redeploy of the last good digest, or a revert, as ADR 0013 already describes for dev.

## Consequences

- Each new revision imports its uSync files at startup, and no older revision runs against the same database.
- No instant traffic-shift rollback. Recovery takes a deploy (minutes, not seconds). A deactivated revision can still be activated by hand, and it will import its own older uSync files when it starts.
- Before prod, revisit both choices together: more than one replica needs SchedulingPublisher and Subscriber roles (DEV-015), and a zero-downtime rollback for prod needs a design that keeps the shared database compatible (ES-014).
