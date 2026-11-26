#!/usr/bin/env bash
# Weekly restore test of the dev database (ADR 0019, ES-031, OPS-05):
# 1. Restores the database as it was an hour ago to a copy named <database>-restore-test, and times it.
# 2. Checks the copy from the database user job inside the network (SQL admits only the apps subnet; ADR 0017), with a
#    read-only command that refuses any database not named *-restore-test.
# 3. Deletes the copy, whatever happened. Any copy a failed run left behind is deleted first.
# It only ever creates, reads or deletes the copy. Prints no secret: the SQL token goes only into a private body file.
# Inputs (stack outputs): RESOURCE_GROUP, SQL_SERVER_FQDN, DATABASE, JOB_ID. Needs az.
set -euo pipefail

: "${RESOURCE_GROUP:?}" "${SQL_SERVER_FQDN:?}" "${DATABASE:?}" "${JOB_ID:?}"
api="api-version=2024-03-01"

check() { [[ "$2" =~ $3 ]] || { echo "$1 has an unexpected format" >&2; exit 1; }; }
check RESOURCE_GROUP "$RESOURCE_GROUP" '^[A-Za-z0-9_.()-]{1,90}$'
check SQL_SERVER_FQDN "$SQL_SERVER_FQDN" '^[a-z0-9-]{1,63}\.database\.windows\.net$'
check DATABASE "$DATABASE" '^[A-Za-z0-9_-]{1,115}$'
check JOB_ID "$JOB_ID" '^/subscriptions/[0-9a-f-]{36}/resourceGroups/[A-Za-z0-9_.()-]+/providers/Microsoft\.App/jobs/[a-z0-9-]+$'
server="${SQL_SERVER_FQDN%%.*}"
copy="${DATABASE}-restore-test"
summary="${GITHUB_STEP_SUMMARY:-/dev/null}"

# Deleting a database that does not exist succeeds, so no existence check can skip a delete. The first call waits,
# so the restore can reuse the name; the one on exit does not, so a cancelled run (which gets seconds) still sends it.
delete_copy() { az sql db delete -g "$RESOURCE_GROUP" -s "$server" -n "$copy" --yes "$@"; }
cleanup() { delete_copy --no-wait && echo "deletion of ${copy} requested"; }

delete_copy
trap cleanup EXIT

# An hour back exercises point-in-time restore. It fails, as it should be reported, if the database is younger than that.
point="$(date -u -d '1 hour ago' +%Y-%m-%dT%H:%M:%SZ)"
started=$SECONDS
# Serverless with a short auto-pause, so a copy a failed run leaves behind costs little until the next run deletes it.
az sql db restore -g "$RESOURCE_GROUP" -s "$server" -n "$DATABASE" --dest-name "$copy" --time "$point" \
  --edition GeneralPurpose --compute-model Serverless --family Gen5 --capacity 1 --auto-pause-delay 60 \
  --backup-storage-redundancy Local --output none
restore_seconds=$(( SECONDS - started ))
echo "restored ${DATABASE} as of ${point} to ${copy} in ${restore_seconds}s"

image="$(az rest --method get --url "${JOB_ID}?${api}" --query "properties.template.containers[0].image" -o tsv)"
check IMAGE "$image" '^[a-z0-9./:_-]+@sha256:[0-9a-f]{64}$'

# The job definition's container, kept in step with SiteInfrastructure.cs (a test checks both), with the check command.
container_name="database-user"
container_command='["dotnet", "/app/tools/database-user/UmbracoSample.Infra.DatabaseUser.dll", "restore-check"]'

token="$(az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv)"
[[ "$token" =~ ^[A-Za-z0-9._-]+$ ]] || { echo "could not get a SQL access token" >&2; exit 1; }
body="$(mktemp)"
chmod 600 "$body"
trap 'rm -f "$body"; cleanup' EXIT
printf '{"containers":[{"name":"%s","image":"%s","command":%s,"resources":{"cpu":0.25,"memory":"0.5Gi"},"env":[{"name":"SQL_SERVER_FQDN","value":"%s"},{"name":"DATABASE","value":"%s"},{"name":"SQL_ACCESS_TOKEN","value":"%s"}]}]}' \
  "$container_name" "$image" "$container_command" "$SQL_SERVER_FQDN" "$copy" "$token" > "$body"
unset token

execution="$(az rest --method post --url "${JOB_ID}/start?${api}" --body "@${body}" --query id -o tsv)"
rm -f "$body"
[[ "$execution" == */executions/* ]] || { echo "the check job did not start" >&2; exit 1; }
echo "started check ${execution##*/}"

status=""
for _ in $(seq 1 72); do
  status="$(az rest --method get --url "${execution}?${api}" --query properties.status -o tsv)"
  case "$status" in
    Succeeded)
      total_seconds=$(( SECONDS - started ))
      echo "restore test passed: restore ${restore_seconds}s, restore and check ${total_seconds}s"
      printf '## Restore test\n\n| Database | Point in time | Restore | Restore and check |\n|---|---|---|---|\n| %s | %s | %ss | %ss |\n' \
        "$DATABASE" "$point" "$restore_seconds" "$total_seconds" >> "$summary"
      exit 0 ;;
    Failed|Stopped|Degraded)
      echo "restore check ${status}: the copy is missing content or could not be read; see the job's console logs in Log Analytics" >&2
      exit 1 ;;
  esac
  sleep 10
done
echo "restore check still ${status:-unknown} after twelve minutes" >&2
exit 1
