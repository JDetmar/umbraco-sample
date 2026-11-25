# Threat model 0002: the Azure environment and its delivery path

- Status: **Open risks decided by the owner on 2026-11-24 (section 5).** R1 and R3 are accepted for dev only; R2 is fixed by OIDC.
- Date: 2026-11-24
- Scope: one environment as declared in `infra/UmbracoSample.Infra` (ADR 0010), the CI image publish and the infrastructure preview
- Clauses: ES-022, ES-023, ES-024, ES-025, ES-047, ES-049, ES-054; framework ENV-01, OPS-01, OPS-03

## 1. What is being built

One resource group per environment: a Container Apps environment in a delegated subnet running the site, Azure SQL serverless reachable only from that subnet, a private Blob container for media, Key Vault with two secrets, Log Analytics and Application Insights, a user-assigned identity, and a budget. CI publishes the site image to GHCR from `main`, and previews the stack from `main` once the owner enables it. Only the owner runs `pulumi up`. [docs/architecture.md](../architecture.md) has the diagrams.

## 2. Trust boundaries

| # | Boundary | Who is on the other side | Trust |
|---|---|---|---|
| 1 | Internet to Container Apps ingress | Anyone; until the edge exists, this includes the back office at `/umbraco` | Untrusted |
| 2 | Ingress to site container | The platform's proxy, inside the environment | Platform; TLS on the hop (peer-to-peer encryption) |
| 3 | Site to SQL, Blob, Key Vault, Application Insights | Azure services, reached with the site's managed identity | Trusted; each grant scoped to one resource |
| 4 | Apps subnet to SQL public endpoint | Only the apps subnet, by virtual network rule | Network-restricted, then Entra-only authentication |
| 5 | CI to GHCR | The `publish` job's short-lived token, `main` only | Reviewed code only |
| 6 | CI to Pulumi Cloud and Azure | Short-lived Pulumi tokens from OIDC exchange (decrypt stack secrets); the Reader identity for preview; the deploy identity for dev CD (ADR 0013) | Reviewed code only: `main` or manual run in `infra-preview`; `main` only in `dev` |
| 7 | Owner workstation to Pulumi Cloud, Azure and SQL | The owner, the only one who runs `up`; during `up`, a firewall rule admits the owner's IP to SQL while the database user script runs | Trusted; rule removed on exit, Entra-only authentication |

## 3. Assets

| Asset | Classification | Where it lives |
|---|---|---|
| Umbraco admin password (first install) | Secret | Pulumi config secret, Key Vault, referenced by the container |
| Imaging HMAC key | Secret | Pulumi config secret, Key Vault, referenced by the container |
| Pulumi access token | Secret, can decrypt both of the above | GitHub environment `infra-preview` |
| Site content and media | Internal (synthetic outside prod, ES-027) | SQL, Blob |
| Back-office accounts | Confidential | SQL |
| Telemetry and logs | Internal, may contain request paths and IPs | Log Analytics, Application Insights |

## 4. Threats and mitigations (STRIDE)

| # | Threat | Mitigation | Residual |
|---|---|---|---|
| T1 | Spoofing: a caller other than the site connects to SQL | Virtual network rule admits only the apps subnet; Entra-only authentication; no SQL logins | Anything else running in the subnet; only the site runs there |
| T2 | Elevation: a site compromise takes over the database server | The site is a contained `db_owner` user in one database; an owner group administers the server | The site can still read and change all site data |
| T3 | Information disclosure: secrets leak from configuration | Secrets are Key Vault references; values never in the app definition; mock test `Secret_values_reach_only_key_vault` | Anyone with Key Vault Secrets User or a Pulumi token for the stack can read them; CI's token is short-lived and issued only to the `infra-preview` environment |
| T4 | Information disclosure: pull-request code prints stack secrets during preview | Preview runs only on `main` or a manual run; the environment requires the owner's review | A reviewed but malicious change on `main`; mitigated by review (ES-028) |
| T5 | Tampering: a build dependency publishes its own image | Registry write token only in the `publish` job, which runs no build code, on `main` after every gate | Stacks pin digests, so a tag overwrite does not deploy |
| T6 | Spoofing or brute force on the back office before the edge exists | Unattended install (no public installer); Umbraco lockout; HTTPS ingress | No WAF or HSTS yet (DEV-010); dev only, synthetic content |
| T7 | Denial of wallet: traffic or a loop runs up cost | Scale bounded at one replica; serverless SQL with free limit and auto-pause outside prod; log cap; budget alerts | Alerts notify, they do not stop spend |
| T8 | Repudiation: an infrastructure change without a record | Every change is a reviewed pull request; Pulumi Cloud keeps update history; only the owner runs `up` | Portal changes outside Pulumi; a preview shows them as drift |
| T9 | Information disclosure: media served publicly | Container is private; no anonymous access; no shared keys | Media is served through the site, which applies Umbraco's permissions |
| T10 | Information disclosure or tampering: the database user script leaves SQL open, or is fed a crafted name | The firewall rule admits one IP, is removed on exit (trap), and is named `pulumi-deployer-*` so a leftover is easy to find; identity name, client ID and database are checked against Azure's formats before they reach SQL | A killed `up` can leave the rule behind until removed by hand; authentication is still Entra-only |

## 5. Open risks for the owner

Each needed an owner decision (accept, or fix first). Agents do not accept risks (CLAUDE.md). The owner decided R1 to R3 on 2026-11-24, in the project thread; R4 (from ADR 0013) is open.

- **R1. Public back office without WAF or HSTS in dev (T6, DEV-010).** Recommendation: accept for dev with synthetic content and forms off; prod is already blocked without the edge. **Owner decision (2026-11-24): accepted for dev only.** Prod still requires the edge.
- **R2. Long-lived Pulumi token that can decrypt stack secrets (T3, T4, DEV-016).** Recommendation: accept with a 90-day expiry and environment review until `pulumi/auth-actions` is approved. **Owner decision (2026-11-24): fix first. `pulumi/auth-actions` is approved;** the preview job exchanges its GitHub OIDC token for a short-lived Pulumi token, and no Pulumi token is stored. DEV-016 closes when the first preview runs this way.
- **R4. A standing deploy identity for dev CD (T2, T8; ADR 0013).** Open, for the owner. The `deploy-dev` job's identity has Contributor on the subscription, Role Based Access Control Administrator limited to the stack's three data-plane roles, and SQL administrator group membership. It is federated only to this repository's `dev` environment, which runs only on `main`; a merged malicious change could use it, so review before merge (ES-028) is the main control, and dev holds only synthetic content. Recommendation: accept for dev; narrow Contributor to the resource group once a bootstrap stack creates it.
- **R3. Single replica (T7, DEV-015).** Recommendation: accept for dev; revisit before prod traffic. **Owner decision (2026-11-24): accepted for dev only.** Prod traffic still waits for the DEV-015 work.
