#!/usr/bin/env bash
# Creates the site's contained database user and makes it db_owner. Azure has no API for database users, so
# `pulumi up` runs this once per database, as the deployer, who must be in the SQL administrator group (ADR 0010).
# Idempotent. The SQL runs in the UmbracoSample.Infra.DatabaseUser tool (the site's own SQL driver), so the only
# tools needed are the ones `up` already uses: dotnet and a signed-in az.
#
# The server admits only the apps subnet, so this opens a firewall rule for the deployer's public IP and always
# removes it again on exit.
set -euo pipefail

: "${SQL_SERVER_FQDN:?}" "${SQL_SERVER_NAME:?}" "${RESOURCE_GROUP:?}" "${DATABASE:?}" "${SITE_IDENTITY_NAME:?}" "${SITE_IDENTITY_CLIENT_ID:?}"

ip="${DEPLOYER_IP:-$(curl -fsS --max-time 10 https://api.ipify.org)}"
[[ "$ip" =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}$ ]] || { echo "could not determine the deployer's public IPv4 address; set DEPLOYER_IP" >&2; exit 1; }

rule="pulumi-deployer-$(date +%s)"
remove_rule() { az sql server firewall-rule delete -g "$RESOURCE_GROUP" -s "$SQL_SERVER_NAME" -n "$rule" -o none || echo "warning: remove firewall rule $rule by hand" >&2; }
az sql server firewall-rule create -g "$RESOURCE_GROUP" -s "$SQL_SERVER_NAME" -n "$rule" --start-ip-address "$ip" --end-ip-address "$ip" -o none
trap remove_rule EXIT

dotnet run --project "$(dirname "$0")/../../UmbracoSample.Infra.DatabaseUser" -c Release
