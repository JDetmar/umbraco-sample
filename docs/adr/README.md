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
| [0007](ADR-0007-front-end-build.md) | Front-end build: design tokens and one stylesheet, built outside the image | Proposed |
| [0008](ADR-0008-security-headers-and-third-party-allowlist.md) | Security headers, content security policy and the third-party allowlist | Proposed |
| [0009](ADR-0009-redirect-map-and-domain-project.md) | Redirect map as a parsed file, and a domain project for real logic | Proposed |
| [0011](ADR-0011-stay-on-lts-releases.md) | Stay on long-term support releases of .NET and Umbraco | Accepted |
| [0012](ADR-0012-aspire-17-and-cli.md) | Move to Aspire 17 with the CLI as a pinned local tool | Accepted |

New records copy [template.md](template.md) and are named `ADR-NNNN-short-title.md`, with the next free number; a policy test checks the name and that this index lists the file.
