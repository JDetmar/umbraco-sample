# Umbraco sample

A reference implementation of a component-based CMS website built inside explicit engineering guardrails, on Umbraco 17 LTS and .NET 10.

Every requirement in [the engineering rigor framework](docs/engineering-rigor-framework.md) is meant to point at real evidence in this repository: code, a test, a CI check or a document. Where it doesn't yet, the gap is recorded rather than implied.

- [Build-out plan](docs/plan.md)
- [Dependency register](docs/dependencies.md)

## Getting started

Prerequisites: GNU Make, the exact .NET SDK pinned in `global.json`, Docker (or Podman) and gitleaks 8.30.1.

```sh
make bootstrap
dotnet user-secrets --project src/UmbracoSample.AppHost set "Parameters:umbraco-admin-password" "<a strong local password>"
make run
```

`make run` starts the Aspire dashboard, a SQL Server container and the site, and installs Umbraco unattended with the admin user `admin@example.com`. `make validate` runs the same fast checks as CI.

See [CLAUDE.md](CLAUDE.md) for every command and the repository rules, and [docs/adr](docs/adr) for the decisions behind the stack.
