# 0002. .NET Aspire for local orchestration and integration tests

- Status: Accepted
- Date: 2026-11-24
- Decider: owner
- Clauses: ES-019, ES-006, ES-030, ES-052, ES-053

## Context

Contributors and CI need one documented command that starts an isolated environment with the same database engine as production (ES-052, ES-053), and integration tests must exercise real infrastructure (ES-006).

## Alternatives considered

- **docker compose:** simple and familiar, but no typed wiring, no built-in telemetry dashboard and a second mechanism for tests.
- **SQLite locally, SQL Server in Azure:** fastest start, but tests would not exercise the production engine (ES-006).
- **Testcontainers in tests plus compose for local runs:** two orchestration tools for one job.

## Decision

An Aspire AppHost (`src/UmbracoSample.AppHost`) starts SQL Server and the site; `make run` wraps it. Integration tests start the same AppHost through `Aspire.Hosting.Testing`. Service defaults provide OpenTelemetry, health checks and HTTP resilience.

Aspire is pinned to 13.6.x, the most recent line with several weeks of releases behind it; 17.0 had been out for under a week when this was decided. The AppHost uses the NuGet-delivered orchestrator and dashboard (`AspireUseCliBundle=false`) so no separate Aspire CLI install is needed.

Aspire is not used for deployment; see ADR-0003.

## Consequences

- Docker or Podman is a prerequisite for local runs and integration tests.
- The Aspire CLI bundle will become the default in a future Aspire release; revisit this pin then.
- Local and CI databases match production's engine.
