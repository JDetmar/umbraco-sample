#!/usr/bin/env bash
# End-to-end, accessibility and visual tests (TST-11, TST-12, TST-13) against a running site.
# Runs inside the pinned Playwright image (e2e/Dockerfile) as linux/amd64, so browsers, fonts and rendering
# match the committed screenshots on Linux, Intel Macs and Apple Silicon (through emulation) alike.
# Usage: scripts/e2e.sh [playwright args]      e.g. scripts/e2e.sh --update-snapshots
#        scripts/e2e.sh --lighthouse            Lighthouse CI budgets (TST-14) in the same image
# Needs: Docker or Podman (CONTAINER_RUNTIME=podman), and the site running (make run).
# Works with the bash 3.2 that macOS ships.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
runtime="${CONTAINER_RUNTIME:-docker}"
image="$(sed -n 's/^FROM[[:space:]]\{1,\}//p' "$root/e2e/Dockerfile" | head -n 1)"
[[ -n "$image" ]] || { echo "No FROM line in e2e/Dockerfile" >&2; exit 1; }

# On Linux the container shares the host network, so localhost is the site. Docker Desktop and Podman machines
# run containers in a VM, where the host is host.docker.internal (host.containers.internal for Podman).
if [[ "$(uname -s)" == "Linux" ]]; then
  network=(--network host)
  default_url="http://localhost:43835"
elif [[ "$runtime" == "podman" ]]; then
  network=()
  default_url="http://host.containers.internal:43835"
else
  network=()
  default_url="http://host.docker.internal:43835"
fi

# Behind a TLS-inspecting proxy, pass its CA bundle and proxy settings through to npm in the container.
# Proxy values pass by name (-e NAME), so they never appear on the command line.
extra=()
if [[ -n "${NODE_EXTRA_CA_CERTS:-}" && -f "${NODE_EXTRA_CA_CERTS}" ]]; then
  extra+=(-v "${NODE_EXTRA_CA_CERTS}:/etc/extra-ca.crt:ro" -e NODE_EXTRA_CA_CERTS=/etc/extra-ca.crt)
fi
for name in HTTPS_PROXY HTTP_PROXY NO_PROXY https_proxy http_proxy no_proxy; do
  if [[ -n "${!name:-}" ]]; then extra+=(-e "$name"); fi
done

task=playwright
if [[ "${1:-}" == "--lighthouse" ]]; then
  task=lighthouse
  shift
fi

export E2E_BASE_URL="${E2E_BASE_URL:-$default_url}"
export CI="${CI:-}"

"$runtime" run --rm --platform linux/amd64 --ipc host \
  ${network[@]+"${network[@]}"} \
  --user "$(id -u):$(id -g)" -e HOME=/tmp -e CI -e E2E_BASE_URL \
  ${extra[@]+"${extra[@]}"} \
  -v "$root/e2e:/e2e" -w /e2e \
  "$image" \
  bash -c 'npm ci --no-audit && if [[ "$0" == lighthouse ]]; then npx lhci autorun "$@"; else npx playwright test "$@"; fi' "$task" "$@"
