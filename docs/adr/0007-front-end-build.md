# 0007. Front-end build: design tokens and one stylesheet, built outside the image

- Status: Proposed
- Date: 2026-11-24
- Decider: owner
- Clauses: ES-013, ES-016, ES-019, ES-049, ES-052; framework ARC-05, TST-03

## Context

Components need shared visual decisions (colour, spacing, type) that designers and developers change in one place (ARC-05). The plan approved Style Dictionary for tokens and Vite for bundling. This adds an npm toolchain, which brings supply-chain risk (install scripts, floating versions) and a second runtime (Node) into the build.

## Alternatives considered

- **Hand-written CSS custom properties.** No toolchain, but tokens can't be exported to other formats (JSON for tests, design tools), and nothing checks them.
- **Build CSS inside the Dockerfile with a Node stage.** One self-contained build, but it needs a Node base image pulled from Docker Hub during every image build. Docker Hub rate-limits anonymous pulls, and the image would carry a second pinned base to maintain.
- **Commit the built stylesheet.** No Node needed to run the site, but generated files drift from their source unless another check compares them.

## Decision

- `frontend/` holds the token JSON (`tokens/`), the stylesheet sources (`src/`) and the build configuration. Style Dictionary turns tokens into CSS custom properties. Vite bundles and minifies them into `src/UmbracoSample.Web/wwwroot/dist/site.css`. That file is build output and is not committed.
- Node is pinned exactly in `.nvmrc` and `package.json` `engines`, with `engine-strict`. CI installs that version.
- npm dependencies are exact versions with a committed lock file, installed with `npm ci`, and with `ignore-scripts=true`, so no dependency runs code at install time. Policy tests enforce all three.
- `make frontend` builds the stylesheet as part of `make validate`, `make run` and `make image`, and in every CI job before the .NET build or the image build. `make build` stays .NET-only, so `make build && make test-integration` on a clean checkout needs `make frontend` first. The Dockerfile copies the built file and fails clearly if it is missing, so the image needs no Node stage. The guard checks presence only: a local `docker build` without `make image` can copy a stale stylesheet; CI always rebuilds it in the same job.
- Rules on tokens are tests, run against the built stylesheet: WCAG contrast for every colour pair, a check that every colour used for text or background is in a tested pair, and no raw colour value (hex, named, rgb, hsl, oklch, lab, hwb, color()) outside the token definitions. A bundle-size budget (10 KB gzipped) fails the build.

## Consequences

- Contributors need Node 24.22.3 as well as the .NET SDK.
- The image is built from a pre-built context, so its provenance includes the CI step that ran `make frontend`. That step runs in the same job, from the same commit.
- Dependabot proposes npm updates weekly. `make frontend-audit` fails CI on any advisory of moderate severity or above unless the ledger assesses it; each exception in `frontend/scripts/audit.mjs` names its ledger entry and expires (DEV-006).
- ARC-05 is only partly met: tokens are code, but nothing syncs them from a design tool (Figma). That sync is a separate decision if a design team needs it.
- Revisit if the site needs JavaScript bundles, or if a Node stage can use a mirrored base image.
