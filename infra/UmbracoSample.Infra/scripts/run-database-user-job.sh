#!/usr/bin/env bash
# Creates the site's contained database user by starting the database user job inside the virtual network (ADR 0016).
# Azure has no API for database users. SQL admits only the apps subnet, so the SQL runs from a Container Apps job there,
# and no firewall rule is opened. The job has no identity: this script passes it a SQL access token of the deployer (a
# member of the SQL administrator group), valid for about an hour, and waits for the execution to finish.
# It prints nothing secret: the token goes only into the request body, in a private temporary file. Needs only az.
set -euo pipefail

: "${JOB_ID:?}" "${SQL_SERVER_FQDN:?}" "${DATABASE:?}" "${SITE_IDENTITY_NAME:?}" "${SITE_IDENTITY_CLIENT_ID:?}"
api="api-version=2024-03-01"

# The job definition's container, kept in step with SiteInfrastructure.cs (a test checks both).
container_name="database-user"
container_command='["dotnet", "/app/tools/database-user/UmbracoSample.Infra.DatabaseUser.dll"]'

# Every value spliced into the JSON body is checked against the shape Azure produces, so none needs escaping.
check() { [[ "$2" =~ $3 ]] || { echo "$1 has an unexpected format" >&2; exit 1; }; }
image="$(az rest --method get --url "${JOB_ID}?${api}" --query "properties.template.containers[0].image" -o tsv)"
check IMAGE "$image" '^[a-z0-9./:_-]+@sha256:[0-9a-f]{64}$'
check SQL_SERVER_FQDN "$SQL_SERVER_FQDN" '^[a-z0-9-]{1,63}\.database\.windows\.net$'
check DATABASE "$DATABASE" '^[A-Za-z0-9_-]{1,128}$'
check SITE_IDENTITY_NAME "$SITE_IDENTITY_NAME" '^[A-Za-z0-9_-]{1,128}$'
check SITE_IDENTITY_CLIENT_ID "$SITE_IDENTITY_CLIENT_ID" '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'

token="$(az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv)"
[[ "$token" =~ ^[A-Za-z0-9._-]+$ ]] || { echo "could not get a SQL access token for the deployer" >&2; exit 1; }

body="$(mktemp)"
chmod 600 "$body"
trap 'rm -f "$body"' EXIT
printf '{"containers":[{"name":"%s","image":"%s","command":%s,"resources":{"cpu":0.25,"memory":"0.5Gi"},"env":[{"name":"SQL_SERVER_FQDN","value":"%s"},{"name":"DATABASE","value":"%s"},{"name":"SITE_IDENTITY_NAME","value":"%s"},{"name":"SITE_IDENTITY_CLIENT_ID","value":"%s"},{"name":"SQL_ACCESS_TOKEN","value":"%s"}]}]}' \
  "$container_name" "$image" "$container_command" "$SQL_SERVER_FQDN" "$DATABASE" "$SITE_IDENTITY_NAME" "$SITE_IDENTITY_CLIENT_ID" "$token" > "$body"
unset token

execution="$(az rest --method post --url "${JOB_ID}/start?${api}" --body "@${body}" --query id -o tsv)"
rm -f "$body"
[[ "$execution" == */executions/* ]] || { echo "the job did not start" >&2; exit 1; }
echo "started ${execution##*/}"

# The tool retries a resuming database for up to three minutes; the replica times out at 600 s. Poll past that.
status=""
for _ in $(seq 1 72); do
  status="$(az rest --method get --url "${execution}?${api}" --query properties.status -o tsv)"
  case "$status" in
    Succeeded) echo "the site's database user is in place"; exit 0 ;;
    Failed|Stopped|Degraded) echo "database user job ${status}; see the job's console logs in Log Analytics" >&2; exit 1 ;;
  esac
  sleep 10
done
echo "database user job still ${status:-unknown} after twelve minutes" >&2
exit 1
