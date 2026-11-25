#!/usr/bin/env bash
# Prints the image main last published, pinned by digest: ghcr.io/<owner>/umbraco-sample@sha256:<digest>.
# Used by the infra preview and by `make infra-preview`, so no one copies digests by hand (ADR 0013).
# Needs docker buildx; the package is public, so no sign-in is needed.
# Usage: scripts/published-image.sh [ghcr.io/<owner>/umbraco-sample]
set -euo pipefail

repository="${1:-}"
if [[ -z "$repository" ]]; then
  origin="$(git remote get-url origin)"
  slug="$(printf '%s' "$origin" | sed -E 's#^.*github\.com[:/]##; s#\.git$##' | tr '[:upper:]' '[:lower:]')"
  repository="ghcr.io/${slug}"
fi

digest="$(docker buildx imagetools inspect "${repository}:main" --format '{{.Manifest.Digest}}')"
if [[ ! "$digest" =~ ^sha256:[0-9a-f]{64}$ ]]; then
  echo "could not read the digest of ${repository}:main (got '${digest}')" >&2
  exit 1
fi
echo "${repository}@${digest}"
