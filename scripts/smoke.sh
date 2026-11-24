#!/usr/bin/env bash
# Post-deployment smoke test (ES-030). Exits non-zero on the first failure so a pipeline can stop or roll back.
# Usage: scripts/smoke.sh https://site.example
set -euo pipefail

base_url="${1:?usage: smoke.sh <base-url>}"
base_url="${base_url%/}"
# ES-043: every request has a deadline; the retry budget covers a cold container start.
curl_opts=(--silent --show-error --fail --max-time 20 --retry "${SMOKE_RETRIES:-12}" --retry-delay 10 --retry-all-errors)

fetch() {
  curl "${curl_opts[@]}" "$base_url$1"
}

check_equals() {
  local path="$1" expected="$2" body
  body="$(fetch "$path")"
  if [[ "$body" != "$expected" ]]; then
    echo "FAIL $path: expected '$expected', got '$body'" >&2
    exit 1
  fi
  echo "ok   $path"
}

check_contains() {
  local path="$1" expected="$2" body
  body="$(fetch "$path")"
  if [[ "$body" != *"$expected"* ]]; then
    echo "FAIL $path: expected the page to contain '$expected'" >&2
    exit 1
  fi
  echo "ok   $path"
}

check_equals /health/live "Healthy"
check_equals /health/ready "Healthy"
check_equals /health/schema "Healthy"
# A marker from this site's own layout, so Umbraco's built-in "no content" page cannot pass.
check_contains / "${SMOKE_HOME_MARKER:-class=\"skip-link\"}"
echo "Smoke test passed for $base_url"
