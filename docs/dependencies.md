# Dependency register

Each dependency records the ES-015 justification: maintenance health, transitive footprint, licence, security and exit cost. Nothing is added to the repo until it is approved here.

Health and licence notes are from current knowledge and are re-verified when each package is pinned (ES-013). Versions are pinned in lock files, `global.json` and SHA-pinned Actions (ES-017). Dependabot and vulnerability scanning cover all of them (ES-016).

Status key: **Approved** (owner approved 2026-11-24), **Rejected**. New dependencies are added as **Proposed** until approved.

## Runtime (ships in the site image)

| Dependency | Purpose | Licence | Health | Footprint | Security | Exit cost | Status |
|---|---|---|---|---|---|---|---|
| Umbraco CMS 17 (LTS) | The CMS itself | MIT | Commercially backed, LTS supported into 2028 | Large (ASP.NET Core, EF Core, ImageSharp, etc.) | Vendor security advisories, regular patch releases | Very high: it is the platform (ADR-0001) | Approved |
| uSync | Content types and data types in source control; drift check (ARC-03, ENV-06) | MPL-2.0 | Long-running, single main maintainer, tracks each Umbraco major | Small | Runs in back office only | Medium: schema files would need re-export to another tool; Umbraco's own deploy product is the paid alternative | Approved |
| Umbraco.StorageProviders.AzureBlob | Media in Blob storage | MIT | Maintained by Umbraco HQ | Small (Azure.Storage.Blobs) | Uses managed identity, no keys | Low | Approved |
| .NET Aspire ServiceDefaults (OpenTelemetry, health checks, Microsoft.Extensions.Http.Resilience) | Telemetry, health endpoints, timeouts and retries (ES-030, ES-043, ES-044) | MIT / Apache-2.0 | Microsoft and CNCF projects, active | Moderate (OpenTelemetry packages, Polly via resilience) | Well-scrutinised | Low: standard OpenTelemetry and ASP.NET Core APIs | Approved |
| Azure.Monitor.OpenTelemetry.Exporter | Send telemetry to App Insights | MIT | Microsoft, active | Small | Managed identity auth | Low: swap exporter, instrumentation unchanged | Approved |
| Azure.Identity | Managed identity credential for Blob media and Application Insights (ES-024) | MIT | Microsoft, active, the standard Azure SDK auth library | Moderate (Azure.Core, MSAL) | Microsoft security response; only `ManagedIdentityCredential` is used, never developer or interactive credentials | Low: any `TokenCredential` fits the same seams | Approved by the owner on 2026-11-24 (decision in the project thread, recorded in pull request 29). Already transitive via Microsoft.Data.SqlClient; now a direct pin |

## Local orchestration and infrastructure (not in the site image)

| Dependency | Purpose | Licence | Health | Footprint | Security | Exit cost | Status |
|---|---|---|---|---|---|---|---|
| .NET Aspire AppHost (+ SQL Server, Azurite hosting integrations) | One-command local environment (ES-052, ES-053) | MIT | Microsoft, fast release cadence | Dev-time only; needs Docker or Podman | Dev only, no production credentials | Low: replaceable by docker compose | Approved |
| Aspire.Hosting.Testing | Integration tests start the real AppHost (ES-006) | MIT | Microsoft | Dev/test only | n/a | Low | Approved (chosen over Testcontainers to avoid two orchestration tools) |
| Aspire CLI (`aspire.cli` local tool, pinned in `.config/dotnet-tools.json`) | Aspire 17 AppHost builds require the CLI; pinned as a local tool so builds never use a machine-wide install (ES-013) | MIT | Microsoft, same release train as Aspire | Dev-time only | Dev only, no production credentials | Low: version moves with the Aspire packages; a policy test keeps them equal | Approved (owner, 2026-11-24, "Adopt Aspire 17 and the CLI") |
| Aspire.Hosting.Dotnet | `AddDotnetProject` replaces the obsolete `AddProject<T>` in Aspire 17 | MIT | Microsoft | Dev-time only (AppHost) | n/a | Low: part of the approved Aspire AppHost | Approved (owner, 2026-11-24, with Aspire 17) |
| Pulumi CLI + Pulumi.AzureNative (C#) | All Azure infrastructure (ENV-01) | Apache-2.0 | Commercially backed, active | Provider is large but build-time only | Preview on PR, OIDC to Azure | Medium: rewrite to Bicep or Terraform | Approved |
| Pulumi Cloud | State, config secrets, deployment history | SaaS (existing account) | Commercial service | n/a | Secrets encrypted by the service; access by token scoped to the repo's stacks | Low: `pulumi stack export` to a self-managed backend | Approved |

## Test and quality (dev/CI only)

| Dependency | Purpose | Licence | Health | Footprint | Security | Exit cost | Status |
|---|---|---|---|---|---|---|---|
| xUnit v3 (`xunit.v3.mtp-v2`) | Unit and integration test runner on Microsoft.Testing.Platform | Apache-2.0 | Active, .NET Foundation | Small | n/a | Low | Approved |
| Shouldly | Readable assertions | BSD-3 | Active | Small | n/a | Low | Approved (FluentAssertions rejected: v8 moved to a commercial licence) |
| CsCheck | Property-based tests for redirect rules and form validation (ES-002, ES-008) | Apache-2.0 | Active, small maintainer team | Tiny | n/a | Low | Approved |
| Stryker.NET | Mutation testing on critical domain logic, scheduled lane (ES-002) | Apache-2.0 | Active | Dotnet tool, CI only | n/a | Low | Approved |
| WireMock.Net | MAP stub with documented fidelity limits (TST-07, ES-006) | Apache-2.0 | Active | Small | Local only | Low | Approved |
| NetArchTest.Rules | Executable layering rule: domain never references Umbraco (ES-018) | MIT | Last release 1.3.2 in May 2021; works, but effectively unmaintained. ArchUnitNET is the fallback | Tiny | n/a | Low: a few tests | Approved (watch health) |
| @playwright/test | End-to-end tests (TST-12) and visual regression (TST-11) | Apache-2.0 | Microsoft, active | Runs against the browsers in the Playwright image below | n/a | Medium: test rewrite | Approved |
| Playwright container image (`mcr.microsoft.com/playwright`, `e2e/Dockerfile`) | The browsers and fonts for `@playwright/test`, pinned by digest and run as linux/amd64, so visual baselines match on every machine (TST-11, ES-006) | Apache-2.0 | Microsoft, published with each Playwright release | Large image (about 2 GB); used only by `make e2e` and CI, never shipped | Pulled from MCR by digest; Dependabot (docker) proposes updates; a policy test keeps its tag equal to `@playwright/test`. Dependabot opens separate npm and docker pull requests for one Playwright release; each fails the policy test alone, so combine them into one pull request | Low: `npx playwright install` with the same version | Approved (owner, 2026-11-24) |
| @axe-core/playwright | Accessibility scans in the gate (TST-13, ES-036) | MPL-2.0 | Deque, active | Small | n/a | Low | Approved |
| @lhci/cli (Lighthouse CI) | Performance budgets (TST-14) | Apache-2.0 | Google, moderate activity | Moderate; runs only in the Playwright image with its Chromium | Results stay on disk (`upload.target: filesystem`), never public storage. Its transitive basic-ftp (FTP is never used) is pinned to a patched version with an npm `override`; remove the override when upgrading to @lhci/cli 0.16.1 or later, which no longer needs it | Low | Approved |

## Front end build

| Dependency | Purpose | Licence | Health | Footprint | Security | Exit cost | Status |
|---|---|---|---|---|---|---|---|
| Style Dictionary | Design tokens JSON to CSS variables (ARC-05) | Apache-2.0 | Active | Small | Build time only. Its transitive glob and minimatch are pinned to patched versions with npm `overrides` (ADR 0007) | Low | Approved |
| HtmlSanitizer (Ganss.Xss) | Server-side allowlist sanitizing of rich-text markup (ES-026, ledger DEV-007) | MIT | Long-running, widely used, single main maintainer, regular releases | Small (AngleSharp) | Its purpose is security; tracks AngleSharp advisories | Low: the sanitizer sits behind Umbraco's IHtmlSanitizer interface | **Proposed: needs owner approval** |
| Vite | Bundle and minify CSS/JS, bundle-size limits (TST-03) | MIT | Very active | Moderate npm tree | Build time only; lockfile pinned | Low: Rolldown and Lightning CSS (what Vite 8 uses) can run directly | Approved |

## CI tooling (Actions pinned to commit SHAs, least-privilege permissions)

| Dependency | Purpose | Licence | Notes | Status |
|---|---|---|---|---|
| actions/checkout, setup-dotnet, setup-node | Standard build steps | MIT | GitHub-maintained | Approved |
| github/codeql-action | Static analysis (TST-04) | MIT | Free for public repos | Approved |
| gitleaks (CLI binary, checksum-verified) | Secret scanning gate (ES-024) | MIT | CLI rather than gitleaks-action, which needs a licence for organisation accounts | Approved |
| pulumi/actions | Preview on PR, up after merge | Apache-2.0 | | Approved |
| azure/login | OIDC sign-in, no stored secrets | MIT | | Approved |
| anchore/syft | SBOM for the container image (ES-049) | Apache-2.0 | | Approved |
| Dependabot | Update discovery (ES-016) | GitHub built-in | | Approved |

## Deliberately not used

| Candidate | Why not |
|---|---|
| Umbraco Forms | Commercial licence; native forms posting to the MAP keep the separation in ARC-06 |
| Moq / NSubstitute | ES-004 prefers behavioural fakes; add only if a seam genuinely needs one |
| FluentAssertions | Commercial licence from v8 |
| Testcontainers | Duplicates Aspire.Hosting.Testing |
| SQLite provider | SQL Server everywhere matches production (ES-006) |
| Pulumi ESC | Not needed yet; Pulumi config secrets cover current needs |
