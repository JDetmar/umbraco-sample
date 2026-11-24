# 0001. Umbraco 17 LTS on .NET 10

- Status: Accepted
- Date: 2026-11-24
- Decider: owner
- Clauses: ES-019, ES-015, ES-020

## Context

The reference site needs a mature CMS that supports a component (block) model, keeps schema in source control, runs on the team's .NET skills and self-hosts on Azure. Long support windows reduce forced upgrades, which carry their own migration risk (see the ES-014 discussion in the plan).

## Alternatives considered

- **Umbraco latest (18, short-term support):** newest features, but support ends within months and forces an upgrade cadence the sample does not need.
- **A code-first TypeScript CMS:** strong schema-in-code story, but leaves .NET and needs a full front-end rebuild.
- **Hosted headless SaaS CMS:** less to operate, but schema lives in the vendor platform and recurring licence cost applies.

## Decision

Umbraco 17 LTS (17.8.x at the time of writing) on .NET 10 LTS. Models Builder is off (`ModelsMode: Nothing`): views read properties through small hand-written view models per component, so no generated code needs regenerating or checking in CI. Production runs with `Runtime:Mode = Production` and `UpgradeUnattended: false`, so a version upgrade only runs its migrations in a deliberate upgrade release.

## Consequences

- Block List and Block Grid support the component model (ARC-01).
- Without Models Builder, property aliases are strings in view models; the content contract tests (TST-05) catch a renamed or removed alias.
- uSync is needed for schema-in-Git (ARC-03); Umbraco's commercial deploy product is the alternative.
- Umbraco version upgrades run Umbraco's own database migrations, which are not expand/contract; this is a proposed accepted risk awaiting owner approval in the ledger.
- Umbraco officially supports SQL Server and SQLite only; production uses Azure SQL.
