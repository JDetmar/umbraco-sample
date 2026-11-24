# 0012. Move to Aspire 17 with the CLI as a pinned local tool

- Status: Accepted
- Date: 2026-11-24
- Decider: owner
- Clauses: ES-019, ES-013, ES-014, ES-015
- Supersedes: the version paragraph of ADR 0002 (Aspire 13.6.x, `AspireUseCliBundle=false`)

## Context

ADR 0002 pinned Aspire to 13.6.x because 17.0 was under a week old. Dependabot then proposed Aspire 17. Aspire 17 runs on .NET 10, so it does not conflict with ADR 0011 (stay on LTS). It does change the build: an AppHost on `Aspire.AppHost.Sdk` 17 fails with ASPIRE013/ASPIRE009 unless it can find the Aspire CLI, and `AddProject<T>` is obsolete in favour of `AddDotnetProject` from `Aspire.Hosting.Dotnet`.

## Alternatives considered

- **Stay on 13.6.x:** no change now, but the 13 line stops getting fixes and the move only gets larger.
- **Require a machine-wide Aspire CLI install:** simplest csproj, but the version on each machine and CI runner drifts and is not pinned (ES-013).
- **Pin the CLI as a local dotnet tool (chosen):** `aspire.cli` in `.config/dotnet-tools.json` with `rollForward: false`. `make restore` (and so `make bootstrap` and CI) runs `dotnet tool restore`, and the AppHost sets `AspireCliPath` to the restored package for the current platform.

## Decision

Aspire 17.0.0 for the AppHost SDK, the hosting packages and the CLI tool, all at one version. The AppHost uses `AddDotnetProject`. A repository policy test fails if the tool manifest, the AppHost's `AspireCliVersion`, its SDK, `global.json` and every `Aspire.*` package version do not match.

## Consequences

- One more pinned tool to restore; `make bootstrap` handles it, and the dependency register lists it.
- Aspire upgrades now touch five places; the policy test catches a partial bump, so a Dependabot pull request that moves only the packages fails CI and is redone by hand.
- `AspireCliPath` covers osx-arm64, osx-x64, linux-arm64, linux-x64, win-arm64 and win-x64. musl-based Linux (Alpine) is not supported for the AppHost; another platform needs a line in the AppHost project. The build stops with a clear message when the platform is not mapped or the tool was not restored.
- Dependabot updates the tool manifest and `global.json` but not the AppHost's `Sdk` attribute or `AspireCliVersion`, so an Aspire update pull request is finished by hand; the policy test keeps it red until then.
- `dotnet run` on the AppHost goes through the Aspire CLI, which builds and runs the Debug configuration and ignores `-c Release --no-build`. CI starts the stack for end-to-end tests with plain `dotnet run`, like `make run`. The end-to-end and Lighthouse runs therefore test a Debug build of the site; the site has no Debug-only code or configuration, and the CI `make build` step still compiles Release.
- The SDK warns (ASPIRE012) that `Aspire.AppHost.Sdk` is obsolete in favour of `Microsoft.NET.Sdk` with explicit `Aspire.Hosting.AppHost` references. That migration is a follow-up; it would also remove one of the version locations above.
