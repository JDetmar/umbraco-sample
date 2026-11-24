# 0010. Azure environment design

- Status: Proposed
- Date: 2026-11-24
- Decider: owner
- Clauses: ES-019, ES-024, ES-030, ES-047, ES-048, ES-049, ES-054; framework ENV-01, OPS-01, OPS-03

## Context

ADR 0003 chose Pulumi (C#) with Pulumi Cloud state, and ADR 0004 chose Azure Container Apps. This record decides what each environment contains, how it authenticates, how it is checked before anything is deployed, and what it costs. The subscription is low-cost, so cost is a constraint, and no agent may deploy or run `pulumi up` (CLAUDE.md).

## Alternatives considered

- **SQL logins and Key Vault secrets for the connection string:** familiar, but it puts a password in the environment and needs rotation.
- **A private endpoint and virtual network for SQL:** removes the public endpoint, but the network and endpoint cost more than the whole lean target.
- **Shared-key storage access:** simplest Umbraco setup, but a leaked key gives full access to media.
- **Bicep instead of Pulumi:** rejected in ADR 0003.

## Decision

One Pulumi project, `infra/UmbracoSample.Infra`, with one stack per environment (`dev`, `prod`; `staging` is allowed by validation but not yet configured). Each stack declares:

| Resource | Setting | Why |
|---|---|---|
| User-assigned managed identity | The site's only identity | No stored keys or passwords (ES-024) |
| Log Analytics and workspace-based Application Insights | 30-day retention, 1 GB/day cap | Telemetry (OPS-01) with a cost ceiling (ES-047) |
| Storage account and private `media` container | No shared keys, no anonymous access, HTTPS and TLS 1.2 only | Media for Umbraco.StorageProviders.AzureBlob (approved) via managed identity |
| Key Vault | RBAC, purge protection | For future secrets, such as the marketing platform credential (threat model 0001) |
| Azure SQL server and serverless database | Entra-only authentication with the site identity as administrator; auto-pause after 60 minutes and the free monthly allowance outside prod | No SQL passwords; dev costs close to nothing |
| SQL firewall rule "allow Azure services" | `0.0.0.0` | The cheapest way for Container Apps to reach SQL without a virtual network; see Consequences |
| Role assignments | Blob Data Contributor on the storage account, Secrets User on the vault, Metrics Publisher on Application Insights | Least privilege, each scoped to one resource |
| Container Apps environment and app | Image by digest only, multiple revisions, HTTPS only, liveness and readiness probes, scale to zero outside prod | ADR 0004, ES-049, ES-030 |
| Consumption budget | Alerts at 80% actual and 100% forecast | ES-047 |

Settings are parsed and validated before any resource is declared (`SiteSettings.Validate`): known environment names, consistent replica bounds, a positive budget, a digest-pinned image, and for prod at least one warm replica and the edge.

**The edge (Front Door Standard with WAF and HSTS) is not declared yet.** A stack with `enableEdge: true` fails before declaring anything, so prod cannot be deployed without its edge (DEV-010).

**Checks before deploy.** `make infra-test` restores in locked mode, checks formatting, builds, and runs Pulumi mock tests that assert the security-relevant inputs (no shared keys, Entra-only SQL, RBAC vault, scoped roles, digest image, HTTPS ingress, budget, tags, free-tier dev database). It needs no cloud account and runs on every infra change. `pulumi preview` of `dev` runs in CI only after the owner enables it (`docs/infra-setup.md`), with OIDC to Azure. Nothing in CI runs `pulumi up`.

## Consequences

- **No passwords or keys anywhere in the stack.** The site reaches SQL, storage and telemetry with its managed identity. The Log Analytics shared key is read at deploy time and passed only to the Container Apps environment, which requires it.
- **"Allow Azure services" admits any Azure tenant's traffic to the SQL endpoint.** Authentication is Entra-only, so a caller still needs a token for an identity the server trusts. This is an accepted-risk candidate for the owner; a private endpoint is the fix if the budget grows.
- **The site needs code changes before a real deploy:** register Umbraco.StorageProviders.AzureBlob with managed identity, and use the Application Insights connection string with the OpenTelemetry exporter. Until then a deployed site keeps media on the container's disk, which is lost on restart.
- **The Pulumi Cloud token is a stored secret** (`PULUMI_ACCESS_TOKEN` in a GitHub environment). Pulumi's OIDC token exchange would remove it but needs `pulumi/auth-actions`, which is not in the register yet.
- **Expected cost** (see `docs/plan.md`): dev close to zero when idle (scale to zero, free SQL allowance, capped logs); prod about $30-40/month without the edge and $60-90 with Front Door Standard. The budget alert is the backstop.
- Revisit when the edge is added, when a second region or a virtual network is needed, or when the threat model adds the marketing platform credential.
