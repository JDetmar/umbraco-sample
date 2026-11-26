# Architecture decision records

Lightweight records of significant decisions (ES-019). Each records context, alternatives, decision and consequences. Accepted records are superseded by a new record, not rewritten.

| ADR | Decision | Status |
|---|---|---|
| [0001](ADR-0001-umbraco-17-lts.md) | Umbraco 17 LTS on .NET 10 | Accepted |
| [0002](ADR-0002-aspire-local-orchestration.md) | .NET Aspire for local orchestration and integration tests | Accepted |
| [0003](ADR-0003-pulumi-infrastructure.md) | Pulumi (C#) with Pulumi Cloud state for all Azure infrastructure | Accepted |
| [0004](ADR-0004-azure-container-apps-hosting.md) | Azure Container Apps for hosting | Accepted |
| [0005](ADR-0005-test-stack.md) | Test stack | Accepted |
| [0006](ADR-0006-dependency-policy.md) | Dependency policy | Accepted |
| [0007](ADR-0007-front-end-build.md) | Front-end build: design tokens and one stylesheet, built outside the image | Accepted |
| [0008](ADR-0008-security-headers-and-third-party-allowlist.md) | Security headers, content security policy and the third-party allowlist | Accepted |
| [0009](ADR-0009-redirect-map-and-domain-project.md) | Redirect map as a parsed file, and a domain project for real logic | Accepted |
| [0010](ADR-0010-azure-environment-design.md) | Azure environment design: Container Apps, Azure SQL, Key Vault and managed identity per environment | Accepted |
| [0011](ADR-0011-stay-on-lts-releases.md) | Stay on long-term support releases of .NET and Umbraco | Accepted |
| [0012](ADR-0012-aspire-17-and-cli.md) | Move to Aspire 17 with the CLI as a pinned local tool | Accepted |
| [0013](ADR-0013-continuous-deployment-to-dev.md) | Continuous deployment to dev | Accepted |
| [0014](ADR-0014-no-property-diff-in-ci-logs.md) | No Pulumi property diff in CI logs (amends 0013) | Accepted |
| [0015](ADR-0015-single-server-role-and-one-active-revision.md) | Single Umbraco server role and one active revision (amends 0004) | Accepted |
| [0016](ADR-0016-admin-bypass-for-a-single-maintainer.md) | Admin bypass of required checks for a single maintainer | Accepted |
| [0017](ADR-0017-database-user-job-in-the-network.md) | Create the site's database user from a job inside the network (amends 0010) | Accepted |
| [0018](ADR-0018-page-owner-and-review-date.md) | Page owner and review date, flagged by a back-office health check | Proposed |

New records copy [template.md](template.md) and are named `ADR-NNNN-short-title.md`, with the next free number; a policy test checks the name and that this index lists the file.
