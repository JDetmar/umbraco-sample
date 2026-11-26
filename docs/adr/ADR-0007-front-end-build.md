# 0007. Front-end build: design tokens and one stylesheet, built outside the image

- Status: Accepted by the owner on 2026-11-26 (project thread, "0007 approved")
- Date: 2026-11-24
- Decider: owner
- Clauses: ES-013, ES-016, ES-019, ES-049, ES-052; framework ARC-05, TST-03

## Context

Components need shared visual decisions (colour, spacing, type) that designers and developers change in one place (ARC-05). The plan approved Style Dictionary for tokens and Vite for bundling. This adds an npm toolchain, which brings supply-chain risk (install scripts, floating versions) and a second runtime (Node) into the build.

## Alternatives considered

- **Hand-written CSS custom properties.** No toolchain, but tokens can't be exported to other formats (JSON for tests, design tools), and nothing checks them.
- **Build CSS inside the Dockerfile with a Node stage.** One self-contained build, but it needs a Node base image pulled from Docker Hub during every image build. Docker Hub rate-limits anonymous pulls, and the image would carry a second pinned base to maintain.
- **Pin Node exactly in `engines` as well as `.nvmrc` (the original decision).** Every machine builds with the same Node, but every contributor must install that exact patch, and Node ships security patches every few weeks, so local builds failed with EBADENGINE on an otherwise correct Node 26. Unlike the .NET SDK, which `global.json` pins exactly because its version selects the compiler and the runtime packs baked into the output, the Node patch does not change what Style Dictionary and Vite emit; CI's exact pin is what makes the shipped stylesheet reproducible.
- **Commit the built stylesheet.** No Node needed to run the site, but generated files drift from their source unless another check compares them.

## Decision

- `frontend/` holds the token JSON (`tokens/`), the stylesheet sources (`src/`) and the build configuration. Style Dictionary turns tokens into CSS custom properties. Vite bundles and minifies them into `src/UmbracoSample.Web/wwwroot/dist/site.css`. That file is build output and is not committed.
- Node follows the active LTS line (26). CI installs the exact version in `.nvmrc`, so the authoritative build and the stylesheet in the image are reproducible. `package.json` `engines` accepts any release of that major (`>=26.0.0 <27.0.0`) with `engine-strict`, so a contributor on another 26.x is not blocked, while another major fails fast. The range deliberately includes 26.0 to 26.12, released before the line became LTS at 26.13.0, because contributors who installed Node 26 early are on those; `.nvmrc`, and so CI, is always an LTS release. A policy test keeps `engines` on the same major as `.nvmrc`, with `.nvmrc` inside the range, and checks that every CI `setup-node` step reads `.nvmrc`.
- npm dependencies are exact versions with a committed lock file, installed with `npm ci`, and with `ignore-scripts=true`, so no dependency runs code at install time. Policy tests enforce all three.
- `make frontend` builds the stylesheet as part of `make validate`, `make run` and `make image`, and in every CI job before the .NET build or the image build. `make build` stays .NET-only, so `make build && make test-integration` on a clean checkout needs `make frontend` first. The Dockerfile copies the built file and fails clearly if it is missing, so the image needs no Node stage. The guard checks presence only: a local `docker build` without `make image` can copy a stale stylesheet; CI always rebuilds it in the same job.
- Rules on tokens are tests, run against the built stylesheet: WCAG contrast for every colour pair, a check that every colour used for text or background is in a tested pair, and no raw colour value (hex, named, rgb, hsl, oklch, lab, hwb, color()) outside the token definitions. A bundle-size budget (10 KB gzipped) fails the build.

## Consequences

- Contributors need Node 26 LTS as well as the .NET SDK. A local `make image` can therefore build the stylesheet with a different 26 patch than CI; only images built by CI are release artifacts.
- Dependabot cannot update `.nvmrc`, so Node security releases are applied by hand: whoever merges Dependabot updates checks the Node release list at the same time and bumps `.nvmrc` (and the `engines` lower bound only if the old version is no longer supported).
- The image is built from a pre-built context, so its provenance includes the CI step that ran `make frontend`. That step runs in the same job, from the same commit.
- Dependabot proposes npm updates weekly. `make frontend-audit` fails CI on any advisory of moderate severity or above unless the ledger assesses it, and fails if the audit cannot run. Each exception in `frontend/scripts/audit.mjs` must name a ledger entry and expire; there are none today.
- Style Dictionary 5.8.0 depends on glob and minimatch versions with moderate advisories (GHSA-3g3m-2fjr-6pw4, GHSA-g3hv-75wg-m249). `package.json` `overrides` pins the patched glob 14.0.6 and minimatch 11.0.9; the token build and its tests pass with them. Dependabot does not bump overrides, so remove them once a Style Dictionary release depends on patched versions itself.
- ARC-05 is only partly met: tokens are code, but nothing syncs them from a design tool (Figma). That sync is a separate decision if a design team needs it.
- Revisit if the site needs JavaScript bundles, or if a Node stage can use a mirrored base image.
