#!/usr/bin/env bash
# End-to-end, accessibility and visual tests (TST-11, TST-12, TST-13) against a running site.
# Runs inside the pinned Playwright image so browsers and fonts match the committed screenshots on every machine.
# Usage: scripts/e2e.sh [playwright args]      e.g. scripts/e2e.sh --update-snapshots
# Needs: Docker, and the site running (make run) at E2E_BASE_URL (default http://localhost:43835).
set -euo pipefail

image="mcr.microsoft.com/playwright:v1.65.1-noble@sha256:1507cbfc550a63a6b7396791ca4f00df17beb56342d2447b3f106c306f525579"
root="$(cd "$(dirname "$0")/.." && pwd)"

# Behind a TLS-inspecting proxy, pass its CA bundle and proxy settings through to npm in the container.
extra=()
if [[ -n "${NODE_EXTRA_CA_CERTS:-}" && -f "${NODE_EXTRA_CA_CERTS}" ]]; then
  extra+=(-v "${NODE_EXTRA_CA_CERTS}:/etc/extra-ca.crt:ro" -e NODE_EXTRA_CA_CERTS=/etc/extra-ca.crt)
fi
for name in HTTPS_PROXY HTTP_PROXY NO_PROXY https_proxy http_proxy no_proxy; do
  [[ -n "${!name:-}" ]] && extra+=(-e "${name}=${!name}")
done

docker run --rm --network host --ipc host \
  --user "$(id -u):$(id -g)" -e HOME=/tmp \
  -e CI="${CI:-}" -e E2E_BASE_URL="${E2E_BASE_URL:-http://localhost:43835}" \
  "${extra[@]}" \
  -v "$root/e2e:/e2e" -w /e2e \
  "$image" \
  bash -c 'npm ci --no-audit && npx playwright test "$@"' playwright "$@"
