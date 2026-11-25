#!/usr/bin/env bash
# Creates the site's contained database user by starting the database user job inside the virtual network (ADR 0016).
# Azure has no API for database users. SQL admits only the apps subnet, so the SQL runs from a Container Apps job there,
# and no firewall rule is opened. The job has no identity: this script passes it a SQL access token of the deployer (a
# member of the SQL administrator group), valid for about an hour, and waits for the execution to finish.
# It prints nothing secret: the token goes only into the request body, in a private temporary file.
# Needs az (signed in) and jq.
set -euo pipefail

: "${JOB_ID:?}" "${IMAGE:?}" "${SQL_SERVER_FQDN:?}" "${DATABASE:?}" "${SITE_IDENTITY_NAME:?}" "${SITE_IDENTITY_CLIENT_ID:?}"
api="api-version=2024-03-01"

body="$(mktemp)"
chmod 600 "$body"
trap 'rm -f "$body"' EXIT

TOKEN="$(az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv)" \
jq -n \
  --arg image "$IMAGE" --arg fqdn "$SQL_SERVER_FQDN" --arg db "$DATABASE" \
  --arg name "$SITE_IDENTITY_NAME" --arg client "$SITE_IDENTITY_CLIENT_ID" \
  '{containers: [{
      name: "database-user",
      image: $image,
      command: ["dotnet", "/app/tools/database-user/UmbracoSample.Infra.DatabaseUser.dll"],
      env: [
        {name: "SQL_SERVER_FQDN", value: $fqdn},
        {name: "DATABASE", value: $db},
        {name: "SITE_IDENTITY_NAME", value: $name},
        {name: "SITE_IDENTITY_CLIENT_ID", value: $client},
        {name: "SQL_ACCESS_TOKEN", value: env.TOKEN}
      ],
      resources: {cpu: 0.25, memory: "0.5Gi"}
    }]}' > "$body"

execution="$(az rest --method post --url "${JOB_ID}/start?${api}" --body "@${body}" --query id -o tsv)"
rm -f "$body"
[[ "$execution" == "${JOB_ID}/executions/"* ]] || { echo "the job did not start" >&2; exit 1; }
echo "started ${execution##*/}"

# The serverless database may be resuming, and the tool retries for up to three minutes; allow ten.
status=""
for _ in $(seq 1 60); do
  status="$(az rest --method get --url "${execution}?${api}" --query properties.status -o tsv)"
  case "$status" in
    Succeeded) echo "the site's database user is in place"; exit 0 ;;
    Failed|Stopped|Degraded) echo "database user job ${status}; see the job's console logs in Log Analytics" >&2; exit 1 ;;
  esac
  sleep 10
done
echo "database user job still ${status:-unknown} after ten minutes" >&2
exit 1
