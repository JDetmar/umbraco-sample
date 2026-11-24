#!/usr/bin/env bash
# Post-deployment smoke test (ES-030). Exits non-zero on the first failure so a pipeline can stop or roll back.
# Usage: scripts/smoke.sh https://site.example
set -euo pipefail

base_url="${1:?usage: smoke.sh <base-url>}"
base_url="${base_url%/}"
# ES-043: every request has a deadline; the retry budget covers a cold container start.
curl_opts=(--silent --show-error --fail --max-time 20 --retry "${SMOKE_RETRIES:-12}" --retry-delay 10 --retry-all-errors)

check_body() {
  local path="$1" expected="$2" body
  body="$(curl "${curl_opts[@]}" "$base_url$path")"
  if [[ "$body" != *"$expected"* ]]; then
    echo "FAIL $path: expected '$expected'" >&2
    exit 1
  fi
  echo "ok   $path"
}

check_body /health/live "Healthy"
check_body /health/ready "Healthy"
check_body /health/schema "Healthy"
check_body / "<h1>"
echo "Smoke test passed for $base_url"
