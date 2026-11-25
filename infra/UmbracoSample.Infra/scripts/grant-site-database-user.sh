#!/usr/bin/env bash
# Creates the site's contained database user and makes it db_owner. Azure has no API for database users, so
# `pulumi up` runs this once per database, as the deployer, who must be in the SQL administrator group (ADR 0010).
# Idempotent: safe to re-run, and a no-op if the user already exists.
#
# Needs: az (signed in), sqlcmd (go-sqlcmd), curl. The server admits only the apps subnet, so this opens a
# firewall rule for the deployer's public IP and always removes it again on exit.
set -euo pipefail

: "${SQL_SERVER_FQDN:?}" "${SQL_SERVER_NAME:?}" "${RESOURCE_GROUP:?}" "${DATABASE:?}" "${SITE_IDENTITY_NAME:?}" "${SITE_IDENTITY_CLIENT_ID:?}"

# The values are spliced into SQL, so accept only the shapes Azure produces.
[[ "$SITE_IDENTITY_NAME" =~ ^[A-Za-z0-9_-]{1,128}$ ]] || { echo "unexpected identity name: $SITE_IDENTITY_NAME" >&2; exit 1; }
[[ "$SITE_IDENTITY_CLIENT_ID" =~ ^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$ ]] || { echo "unexpected client id" >&2; exit 1; }
[[ "$DATABASE" =~ ^[A-Za-z0-9_-]{1,128}$ ]] || { echo "unexpected database name: $DATABASE" >&2; exit 1; }

for tool in az sqlcmd curl; do
  command -v "$tool" >/dev/null || { echo "$tool is required (docs/infra-setup.md)" >&2; exit 1; }
done

ip="${DEPLOYER_IP:-$(curl -fsS --max-time 10 https://api.ipify.org)}"
[[ "$ip" =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}$ ]] || { echo "could not determine the deployer's public IPv4 address; set DEPLOYER_IP" >&2; exit 1; }

rule="pulumi-deployer-$(date +%s)"
remove_rule() { az sql server firewall-rule delete -g "$RESOURCE_GROUP" -s "$SQL_SERVER_NAME" -n "$rule" -o none || echo "warning: remove firewall rule $rule by hand" >&2; }
az sql server firewall-rule create -g "$RESOURCE_GROUP" -s "$SQL_SERVER_NAME" -n "$rule" --start-ip-address "$ip" --end-ip-address "$ip" -o none
trap remove_rule EXIT

# The SID form binds the user to the identity's client ID, so SQL needs no directory lookup.
sql="IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'${SITE_IDENTITY_NAME}')
BEGIN
  DECLARE @sid nvarchar(64) = CONVERT(nvarchar(64), CONVERT(varbinary(16), CONVERT(uniqueidentifier, '${SITE_IDENTITY_CLIENT_ID}')), 1);
  EXEC (N'CREATE USER [${SITE_IDENTITY_NAME}] WITH SID = ' + @sid + N', TYPE = E');
END;
ALTER ROLE db_owner ADD MEMBER [${SITE_IDENTITY_NAME}];"

# A new firewall rule, or a paused serverless database, can take a minute to accept connections.
for attempt in 1 2 3 4 5 6 7 8 9 10 11 12; do
  if sqlcmd -S "tcp:${SQL_SERVER_FQDN},1433" -d "$DATABASE" --authentication-method ActiveDirectoryDefault -b -l 60 -Q "$sql"; then
    echo "database user ${SITE_IDENTITY_NAME} is db_owner of ${DATABASE}"
    exit 0
  fi
  echo "attempt $attempt failed; retrying in 15 seconds" >&2
  sleep 15
done
exit 1
