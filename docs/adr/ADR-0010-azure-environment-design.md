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
- **The site identity as SQL server administrator:** no post-deploy step, but a compromise of the internet-facing site would give server-level control (create and drop databases, add users, change auditing). Rejected for an owner Entra group as administrator and a contained `db_owner` user for the site (ES-023).
- **TLS terminated in the site container** instead of Container Apps peer-to-peer encryption: needs certificate issuance and rotation inside the app; peer-to-peer encryption gives the same hop protection with a platform setting.
- **A consumption-only Container Apps environment:** simpler, but a workload-profiles environment is needed for a delegated subnet with peer-to-peer encryption, and its Consumption profile costs the same.
- **Log Analytics shared key for environment logs:** the older default, but it puts a workspace key in stack state and needs a CI identity that can read keys. Diagnostic settings send the same logs with no key.
- **Bicep instead of Pulumi:** rejected in ADR 0003.

## Decision

One Pulumi project, `infra/UmbracoSample.Infra`, with one stack per environment (`dev`, `prod`; `staging` is allowed by validation but not yet configured). Each stack declares:

[docs/architecture.md](../architecture.md) shows these resources and how they connect.

| Resource | Setting | Why |
|---|---|---|
| User-assigned managed identity | The site's only identity | No stored keys or passwords (ES-024) |
| Globally unique names | Set explicitly from a six-character hash of subscription and environment | Pulumi's random auto-name suffix would exceed the 24-character Key Vault and storage limits |
| Log Analytics and workspace-based Application Insights | 30-day retention; daily cap 1 GB in prod, 0.2 GB elsewhere; Entra-only ingestion; environment logs through a diagnostic setting | Telemetry (OPS-01) with a cost ceiling (ES-047) and no workspace key |
| Storage account and private `media` container | No shared keys, no anonymous access, HTTPS and TLS 1.2 only | Media for Umbraco.StorageProviders.AzureBlob (approved) via managed identity |
| Key Vault and two secrets | RBAC, purge protection; holds the Umbraco admin password and the imaging HMAC key, both set by the owner as Pulumi config secrets | The container references them by Key Vault URL with the site identity, so their values never appear in the app definition (ES-024); later also the marketing platform credential (threat model 0001) |
| Virtual network and one subnet (`10.40.0.0/23`) | Delegated to Container Apps, with the Microsoft.Sql service endpoint | Gives the site a network identity SQL can trust; no charge |
| Azure SQL server and serverless database | Entra-only authentication; an owner Entra group is administrator, and the site identity is a contained `db_owner` user created once after the first deploy; auto-pause after 60 minutes and the free monthly allowance outside prod | No SQL passwords; the site cannot administer the server (ES-023); dev costs close to nothing |
| SQL virtual network rule | Allows only the apps subnet; no IP firewall rules | Only the site's subnet can open a connection to SQL; see Consequences |
| Role assignments | Blob Data Contributor on the storage account, Secrets User on the vault, Metrics Publisher on Application Insights | Least privilege, each scoped to one resource |
| Container Apps environment and app | Workload-profiles environment in the apps subnet with peer-to-peer encryption; image by digest only, multiple revisions, HTTPS only, startup (five minutes, for a resuming database), liveness and readiness probes, one replica (DEV-015), scale to zero outside prod; created after the SQL network rule, database and Key Vault grant; Umbraco installs unattended on first boot | ADR 0004, ES-025, ES-049, ES-030; the public installer is never reachable |
| Consumption budget | Alerts at 80% actual and 100% forecast | ES-047 |

Settings are parsed and validated before any resource is declared (`SiteSettings.Validate`): known environment names, consistent replica bounds with at most one replica (DEV-015), a positive budget, a digest-pinned image that is not the placeholder, an owner SQL administrator group, and for prod at least one warm replica and the edge.

**The edge (Front Door Standard with WAF and HSTS) is not declared yet.** A stack with `enableEdge: true` fails before declaring anything, so prod cannot be deployed without its edge (DEV-010). Inside the environment, peer-to-peer encryption puts TLS on the hop from the Container Apps ingress to the site container, which closes gap DEV-013 once deployed and verified.

**Checks before deploy.** `make infra-test` restores in locked mode, checks formatting, builds, and runs Pulumi mock tests that assert the security-relevant inputs (explicit names within Azure limits, no shared keys, Entra-only SQL administered by an owner group, SQL reachable only from the apps subnet, RBAC vault, secrets only as Key Vault references, unattended install, scoped roles, digest image, HTTPS ingress, encrypted in-environment traffic, budget, tags, free-tier dev database). It needs no cloud account and runs in `make validate`, so the required `validate` check covers it. `pulumi preview` of `dev` runs in CI only after the owner enables it (`docs/infra-setup.md`), only on `main` or a manual run, because preview decrypts stack secrets; it signs in to Azure with OIDC. Nothing in CI runs `pulumi up`. The threat model for this environment is [0002](../threat-models/0002-azure-environment.md).

## Consequences

- **No storage, SQL, telemetry or workspace keys.** The site reaches SQL, storage and telemetry with its managed identity, and environment logs go through a diagnostic setting. The only secrets are the two owner-set values below; CI holds no Pulumi or Azure secret.
- **The first deploy has one manual step.** After the first `pulumi up`, a member of the SQL administrator group creates the site's contained database user; until then the site stays unready. RBAC propagation can also make the first `up` fail on the Key Vault references; a second `up` a few minutes later succeeds (`docs/infra-setup.md`).
- **SQL keeps a public endpoint, but only the apps subnet may use it.** A virtual network rule works through the public endpoint, so `publicNetworkAccess` stays enabled; every other source is refused before authentication, and authentication is Entra-only. A private endpoint is the upgrade if the endpoint itself must disappear.
- **The environment's network cannot change later.** A Container Apps environment is created in its subnet or without one; moving it means recreating the environment and its apps. The subnet is a /23 to leave room to grow.
- **Owner-identifying values are config secrets too.** The repository is public and stack files are committed, so the budget and admin emails and the SQL administrator group are set with `--secret`; the committed file holds only ciphertext, and a policy test rejects plain emails or group values in it.
- **Two owner-set credentials per stack.** `umbracoAdminPassword` and `imagingHmacKey` are Pulumi config secrets (encrypted in Pulumi Cloud state) that Pulumi writes to Key Vault. `pulumi preview` and `up` fail until they are set. Rotating either is `pulumi config set --secret` and `up`; the admin password only matters for the first install, after which it is changed in the back office.
- **The site switches to Azure services by configuration alone, once it supports them.** The stack sets the blob endpoint and container name, and the Application Insights connection string. A site image that includes pull request 29 (`MediaStorage`, `WorkloadIdentity`) then stores media in Blob storage and exports traces and metrics to Application Insights with the managed identity. An older image ignores the settings and keeps media on ephemeral container disk, so the stack must pin an image built after that change.
- **CI gets its Pulumi token by OIDC exchange** (`pulumi/auth-actions`, approved by the owner on 2026-11-24). Pulumi Cloud issues a short-lived token only to this repository's `infra-preview` environment, so no long-lived token is stored (closes DEV-016 once the first preview runs). The token can still decrypt stack secrets for its lifetime, which is why preview runs only on reviewed code.
- **Expected cost** (see `docs/plan.md`): dev close to zero when idle (scale to zero, free SQL allowance, capped logs); prod about $30-40/month without the edge and $60-90 with Front Door Standard. The budget alert is the backstop.
- Revisit when the edge is added, when a second region is needed, when the SQL public endpoint must go, or when the threat model adds the marketing platform credential.
