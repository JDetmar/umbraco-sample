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
- `make frontend` builds the stylesheet before `make build`, `make run` and `make image`. CI does the same in every job. The Dockerfile copies the built file and fails clearly if it is missing, so the image needs no Node stage.
- Rules on tokens are tests: WCAG contrast for every colour pair the components use, and no raw colour values in stylesheets. A bundle-size budget (10 KB gzipped) fails the build.

## Consequences

- Contributors need Node 24.22.3 as well as the .NET SDK.
- The image is built from a pre-built context, so its provenance includes the CI step that ran `make frontend`. That step runs in the same job, from the same commit.
- Dependabot proposes npm updates weekly. Moderate advisories in build-time dependencies are recorded in the ledger (DEV-006). `make frontend-audit` fails CI on high or critical ones.
- Revisit if the site needs JavaScript bundles, or if a Node stage can use a mirrored base image.
