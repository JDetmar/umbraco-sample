# 0010. Azure environment design

- Status: Proposed
- Date: 2026-11-24
- Decider: owner
- Clauses: ES-019, ES-024, ES-030, ES-047, ES-048, ES-049, ES-054; framework ENV-01, OPS-01, OPS-03

## Context

ADR 0003 chose Pulumi (C#) with Pulumi Cloud state, and ADR 0004 chose Azure Container Apps. This record decides what each environment contains, how it authenticates, how it is checked before anything is deployed, and what it costs. The subscription is low-cost, so cost is a constraint, and no agent may deploy or run `pulumi up` (CLAUDE.md).

## Alternatives considered

- **SQL logins and Key Vault secrets for the connection string:** familiar, but it puts a password in the environment and needs rotation.
- **SQL firewall rule "allow Azure services" (`0.0.0.0`), no virtual network:** cheapest, but admits traffic from any Azure tenant to the SQL endpoint, leaving only authentication between the internet-facing Azure estate and the database.
- **A private endpoint for SQL:** removes the public endpoint entirely, but the endpoint and its private DNS zone cost more each month than the whole dev environment.
- **Shared-key storage access:** simplest Umbraco setup, but a leaked key gives full access to media.
- **Bicep instead of Pulumi:** rejected in ADR 0003.

## Decision

One Pulumi project, `infra/UmbracoSample.Infra`, with one stack per environment (`dev`, `prod`; `staging` is allowed by validation but not yet configured). Each stack declares:

[docs/architecture.md](../architecture.md) shows these resources and how they connect.

| Resource | Setting | Why |
|---|---|---|
| User-assigned managed identity | The site's only identity | No stored keys or passwords (ES-024) |
| Log Analytics and workspace-based Application Insights | 30-day retention, 1 GB/day cap | Telemetry (OPS-01) with a cost ceiling (ES-047) |
| Storage account and private `media` container | No shared keys, no anonymous access, HTTPS and TLS 1.2 only | Media for Umbraco.StorageProviders.AzureBlob (approved) via managed identity |
| Key Vault and two secrets | RBAC, purge protection; holds the Umbraco admin password and the imaging HMAC key, both set by the owner as Pulumi config secrets | The container references them by Key Vault URL with the site identity, so their values never appear in the app definition (ES-024); later also the marketing platform credential (threat model 0001) |
| Virtual network and one subnet (`10.40.0.0/23`) | Delegated to Container Apps, with the Microsoft.Sql service endpoint | Gives the site a network identity SQL can trust; no charge |
| Azure SQL server and serverless database | Entra-only authentication with the site identity as administrator; auto-pause after 60 minutes and the free monthly allowance outside prod | No SQL passwords; dev costs close to nothing |
| SQL virtual network rule | Allows only the apps subnet; no IP firewall rules | Only the site's subnet can open a connection to SQL; see Consequences |
| Role assignments | Blob Data Contributor on the storage account, Secrets User on the vault, Metrics Publisher on Application Insights | Least privilege, each scoped to one resource |
| Container Apps environment and app | Workload-profiles environment in the apps subnet with peer-to-peer encryption; image by digest only, multiple revisions, HTTPS only, liveness and readiness probes, scale to zero outside prod; Umbraco installs unattended on first boot | ADR 0004, ES-025, ES-049, ES-030; the public installer is never reachable |
| Consumption budget | Alerts at 80% actual and 100% forecast | ES-047 |

Settings are parsed and validated before any resource is declared (`SiteSettings.Validate`): known environment names, consistent replica bounds, a positive budget, a digest-pinned image, and for prod at least one warm replica and the edge.

**The edge (Front Door Standard with WAF and HSTS) is not declared yet.** A stack with `enableEdge: true` fails before declaring anything, so prod cannot be deployed without its edge (DEV-010). Inside the environment, peer-to-peer encryption puts TLS on the hop from the Container Apps ingress to the site container, which closes gap DEV-013 once deployed and verified.

**Checks before deploy.** `make infra-test` restores in locked mode, checks formatting, builds, and runs Pulumi mock tests that assert the security-relevant inputs (no shared keys, Entra-only SQL, SQL reachable only from the apps subnet, RBAC vault, secrets only as Key Vault references, unattended install, scoped roles, digest image, HTTPS ingress, encrypted in-environment traffic, budget, tags, free-tier dev database). It needs no cloud account and runs on every infra change. `pulumi preview` of `dev` runs in CI only after the owner enables it (`docs/infra-setup.md`), with OIDC to Azure. Nothing in CI runs `pulumi up`.

## Consequences

- **No passwords or keys anywhere in the stack.** The site reaches SQL, storage and telemetry with its managed identity. The Log Analytics shared key is read at deploy time and passed only to the Container Apps environment, which requires it.
- **SQL keeps a public endpoint, but only the apps subnet may use it.** A virtual network rule works through the public endpoint, so `publicNetworkAccess` stays enabled; every other source is refused before authentication, and authentication is Entra-only. A private endpoint is the upgrade if the endpoint itself must disappear.
- **The environment's network cannot change later.** A Container Apps environment is created in its subnet or without one; moving it means recreating the environment and its apps. The subnet is a /23 to leave room to grow.
- **Two owner-set secrets per stack.** `umbracoAdminPassword` and `imagingHmacKey` are Pulumi config secrets (encrypted in Pulumi Cloud state) that Pulumi writes to Key Vault. `pulumi preview` and `up` fail until they are set. Rotating either is `pulumi config set --secret` and `up`; the admin password only matters for the first install, after which it is changed in the back office.
- **The site switches to Azure services by configuration alone.** The stack sets the blob endpoint and container name, and the Application Insights connection string; the site then stores media in Blob storage and exports traces and metrics to Application Insights, both with the managed identity (`MediaStorage`, `WorkloadIdentity` in the site). Without those settings, as locally and in CI, media stays on disk and telemetry goes over OTLP.
- **The Pulumi Cloud token is a stored secret** (`PULUMI_ACCESS_TOKEN` in a GitHub environment). Pulumi's OIDC token exchange would remove it but needs `pulumi/auth-actions`, which is not in the register yet.
- **Expected cost** (see `docs/plan.md`): dev close to zero when idle (scale to zero, free SQL allowance, capped logs); prod about $30-40/month without the edge and $60-90 with Front Door Standard. The budget alert is the backstop.
- Revisit when the edge is added, when a second region is needed, when the SQL public endpoint must go, or when the threat model adds the marketing platform credential.
