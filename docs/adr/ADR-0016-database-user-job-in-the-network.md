# 0016. Create the site's database user from a job inside the network

- Status: Accepted by the owner on 2026-11-25 (project thread decision, "Short-lived token from deploy")
- Date: 2026-11-25
- Decider: owner
- Clauses: ES-019, ES-024, ES-040, ES-054. Amends ADR 0010's database user step; meets condition (e) of DEV-017.

## Context

Azure has no API for contained database users, so `up` creates the site's user with SQL, as a member of the SQL administrator group (ADR 0010). SQL admits only the apps subnet. Until now the script opened a firewall rule for the deployer's public IP (looked up through api.ipify.org) while it ran. Under CD that IP belongs to a shared GitHub-hosted runner, which threat model 0002 (R4) accepted for dev only on condition that the step moves into the virtual network.

## Alternatives considered

- **A job with its own managed identity, added to the SQL administrator group by Pulumi.AzureAD.** Adding a group member as an application needs Microsoft Graph's `GroupMember.ReadWrite.All`, so the deploy identity could change membership of any group in the tenant. Rejected: a wider right than the firewall rule it replaces.
- **A job identity added to the group once by hand.** No new right for the deploy identity, but a manual step per environment.
- **A short-lived token from the deployer (chosen).** The deployer is already in the SQL administrator group, so it can get a SQL access token itself.

## Decision

A manual Container Apps job in the site's environment runs the database user tool, which ships in the site image (`/app/tools/database-user`). The job has no identity and its definition holds no token. During `up`, `scripts/run-database-user-job.sh` gets a SQL access token for the deployer (`az account get-access-token`, valid for about an hour), starts one execution with the token in its environment, and waits for it to finish. No firewall rule is opened and no IP is looked up.

## Consequences

- SQL is reached only from the apps subnet, by anyone. DEV-017's condition (e) is met; the runner's IP is never admitted.
- The token is in the start request, so the execution's definition shows it to anyone who can read the job in Azure until it expires, about an hour. Those readers are the owner and the deploy identity, who can get such a token themselves. It never reaches a log: the script writes it only to a private temporary file for the request body.
- The tool must be in the image the job starts, so the step needs the image CD just published; `up` passes the same digest to the site and the job.
- `up` now needs `jq` beside `az` (both are on GitHub-hosted runners and recent macOS).
- The step runs when the database, the site identity or the job is replaced, so the first deploy after this change runs it once.
