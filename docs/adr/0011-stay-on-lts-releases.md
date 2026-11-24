# 0011. Stay on long-term support releases

- Status: Accepted
- Date: 2026-11-24
- Decider: owner
- Clauses: ES-019, ES-016, ES-014

## Context

ADR 0001 chose Umbraco 17 LTS on .NET 10 LTS. Dependabot then opened pull requests that move the site to .NET 11 (SDK and runtime images, `Microsoft.Extensions.*` 11.x) and Umbraco 18, both short-term support releases. Merging those would quietly undo ADR 0001 and start an upgrade cadence the owner does not want.

## Alternatives considered

- **Take every major as it ships:** newest features, but short support windows force an upgrade every year and each Umbraco major runs its own database migrations (ADR 0001, Consequences).
- **Close each such pull request by hand:** works, but Dependabot reopens them on every new release, and the rule lives only in people's heads.

## Decision

The site stays on LTS lines of .NET and Umbraco. `.github/dependabot.yml` ignores major updates for the packages whose major follows .NET or Umbraco: `Microsoft.AspNetCore.*`, `Microsoft.Extensions.*`, `Umbraco.*` and `uSync*`, and anything beyond 10.0 patches for the `dotnet/sdk` and `dotnet/aspnet` images. Minor and patch updates inside the LTS line, and updates to every other dependency, still arrive as pull requests through the normal gate.

Moving to the next LTS (.NET 12, and the Umbraco LTS built on it) is a planned upgrade with its own ADR, not a Dependabot bump.

## Consequences

- Security patches inside the LTS line keep flowing (ES-016), and `make vuln` still reports advisories on every package whatever Dependabot proposes.
- `global.json` pins the exact SDK, so a Dependabot SDK image bump fails CI until the same pull request updates `global.json`; do both together.
- A new direct dependency whose major follows .NET (for example EF Core) needs its own ignore rule.
- ADR 0010 is reserved by the infrastructure draft.
- A library that drops support for the LTS runtime (for example a future Aspire or test package) will show up as a failing Dependabot pull request; it is then declined or pinned, and noted here.
- When .NET 10 nears end of support (November 2028) and Umbraco 17 nears its end, this record is superseded by the upgrade ADR.
