# 0004. Azure Container Apps for hosting

- Status: Accepted
- Date: 2026-11-24
- Decider: owner
- Clauses: ES-019, ES-014, ES-047, ES-049

## Context

The site runs on a low-cost Azure subscription, so cost is a design constraint. The same immutable artifact must be promoted between environments (ES-049), and a failed rollout must be containable without breaking data compatibility (ES-014).

## Alternatives considered

- **App Service (Linux, container):** mature and simple; deployment slots need Standard tier or above, which costs more than the whole lean target.
- **Azure Kubernetes Service:** far more control than needed and a standing cluster cost.
- **Virtual machines:** no reason to own the operating system.

## Decision

Azure Container Apps on the consumption plan. Production keeps one small replica warm because Umbraco cold starts are slow; dev and staging scale to zero. Releases are container images identified by digest and promoted unchanged. A failed compatible release is contained by shifting traffic back to the previous revision.

## Consequences

- Revisions give traffic shifting without paid slots.
- Umbraco must be configured for a load-balanced-safe, stateless container: media in Blob storage, no local file state that matters.
- Scale-to-zero environments have cold starts of tens of seconds; acceptable for dev and staging.
- Expected cost is recorded in `docs/plan.md` under Hosting cost and checked against an Azure budget alert.
