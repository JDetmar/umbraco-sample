# Infrastructure setup (owner checklist)

These steps need the owner: they create cloud identities, store a secret and change repository settings, which agents may not do (CLAUDE.md). Nothing here deploys anything. Deploying (`pulumi up`) stays a separate, explicit owner action.

## 1. Pulumi Cloud

**OIDC subjects in this guide.** This repository uses GitHub's immutable OIDC subjects (`gh api repos/<owner>/umbraco-sample/actions/oidc/customization/sub` shows `use_immutable_subject: true`), so tokens carry numeric IDs: `repo:<owner>@<owner id>/umbraco-sample@<repository id>:environment:<name>`. Get the two IDs with `gh api repos/<owner>/umbraco-sample --jq '"\(.owner.id) \(.id)"'`. A federated credential or Pulumi policy written as `repo:<owner>/umbraco-sample:...` never matches, and Azure's sign-in fails with AADSTS700213, which quotes the subject GitHub actually sent.

1. Create the stacks from `infra/UmbracoSample.Infra`: `pulumi stack init <org>/umbraco-sample/dev` (and `prod` later).
2. Set the stack's values. **This repository is public**, and `pulumi config set` writes to the committed `Pulumi.<stack>.yaml`. So everything that is a credential or identifies you or your tenant is set with `--secret`: the file then holds only ciphertext that only your Pulumi Cloud stack can decrypt, and a policy test fails if a plain email or group lands there.
   ```sh
   cd infra/UmbracoSample.Infra
   pulumi config set --secret budgetContactEmail <your address> --stack dev
   pulumi config set --secret adminEmail <your address> --stack dev            # first back-office login
   pulumi config set --secret umbracoAdminPassword --stack dev                 # prompts; changed after first sign-in
   openssl rand -base64 64 | tr -d '\n' | pulumi config set --secret imagingHmacKey --stack dev
   ```
   Lifetimes: the admin password is used only by the first install; change it in the back office after first sign-in and treat the Key Vault copy as stale (rotate it with `config set` and `up` if it may have leaked). Rotate the imaging key yearly or on suspected exposure; rotating it invalidates cached image URLs.
3. Create an Entra security group for SQL administration (you, and later anyone else who administers the database), and set it, also as secrets:
   ```sh
   pulumi config set --secret sqlAdminGroupName <group display name> --stack dev
   pulumi config set --secret sqlAdminGroupObjectId <group object id> --stack dev
   ```
4. Check before committing: `git diff infra/UmbracoSample.Infra/Pulumi.dev.yaml` should show only `secure:` values for these keys. Commit the file through a pull request so CI previews use the same config.
5. Make the GHCR package public once CI first publishes it from main (GitHub, your profile, Packages, umbraco-sample, Package settings, Change visibility). New packages start private, and a public package lets Container Apps pull without a stored registry credential. Dev takes its image from CD (section 4); for prod, set the digest from the main CI run's summary: `pulumi config set image ghcr.io/<owner>/umbraco-sample@sha256:<digest> --stack prod`.
6. Let CI sign in with OIDC instead of a stored token. In Pulumi Cloud, Settings, OIDC Issuers, register `https://token.actions.githubusercontent.com`; `pulumi/auth-actions` requests the audience `urn:pulumi:org:<your Pulumi organization>`, so the issuer must accept that audience and add one policy: allow **personal** tokens for your Pulumi user when `sub` equals `repo:<owner>@<owner id>/umbraco-sample@<repository id>:environment:infra-preview`. Until section 4 adds a second policy, nothing else can get a token; each token lasts about two hours. The workflows ask for a personal token because organization and team tokens need a paid organization plan (an individual account fails with "Org tokens are not supported for non enterprise organizations"); on an individual account `PULUMI_ORG` is your Pulumi user name. With a paid plan, a team token for a team with access to only this project's stacks is narrower: switch `requested-token-type` and `scope` in `infra.yml` and `ci.yml` to match.

## 2. Azure

1. Create an app registration (or user-assigned identity) for CI previews.
2. Add a federated credential for GitHub Actions: issuer `https://token.actions.githubusercontent.com`, subject `repo:<owner>@<owner id>/umbraco-sample@<repository id>:environment:infra-preview`.
3. Grant it **Reader** on the subscription (or on the resource group once it exists). Preview needs only read access: the stack reads no keys. Do not grant Contributor until deploys are designed.

## 3. GitHub repository

1. Create an environment named `infra-preview`, restricted to the `main` branch, with yourself as a required reviewer. The workflow previews only `main` or a manual run, because preview decrypts stack secrets; the environment rules enforce that even if the workflow changes.
2. Add repository variables: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `PULUMI_ORG`, and finally `PULUMI_PREVIEW_ENABLED=true`.

## 4. CD for dev (once; ADR 0013)

After this, every merge to `main` that passes all gates deploys dev by digest and smoke-tests it. You no longer run `up` for dev. Read threat model 0002, R4, first: these steps create the standing deploy identity it describes, and its rights reach any prod stack in the same subscription and Pulumi organization.

Do not set `CD_DEV_ENABLED` until the CLAUDE.md delegation envelope allows a merge to `main` to deploy dev: today it forbids agents to deploy to Azure, and an agent's merge would.

1. **Deploy identity.** Create a second app registration (separate from the preview's Reader identity), with a federated credential: issuer `https://token.actions.githubusercontent.com`, subject `repo:<owner>@<owner id>/umbraco-sample@<repository id>:environment:dev`.
2. **Its Azure roles**, on the subscription: **Contributor**, and **Role Based Access Control Administrator** with the condition "allow assigning only" Key Vault Secrets User, Storage Blob Data Contributor and Monitoring Metrics Publisher (the three roles the stack grants the site).
3. **SQL.** Add the deploy identity to the SQL administrator group, so the database user step can run. Use a group that administers dev only.
4. **Pulumi Cloud.** In the OIDC issuer from section 1, step 6, add a second policy: personal tokens for your Pulumi user when `sub` equals `repo:<owner>@<owner id>/umbraco-sample@<repository id>:environment:dev`.
5. **GitHub.** Create an environment named `dev`, restricted to the `main` branch, with no required reviewer (merging to `main` is the approval). Add the repository variable `AZURE_DEPLOY_CLIENT_ID` (the deploy identity's client ID), then `CD_DEV_ENABLED=true`.
6. **Check.** The next push to `main` runs `deploy (dev, pulumi up)` after `publish`; its log shows the diff and the smoke test.

**If the smoke test fails**, the new revision stays; nothing rolls back by itself. Fix forward with a new pull request, or open the last good run in Actions and re-run its `deploy (dev, pulumi up)` job. A re-run deploys only while its commit is the head of `main`, so to roll back past a newer commit, revert that commit instead.

Dev's stack file pins no image: CD passes the digest it just published as `SITE_IMAGE`. To run `up` locally anyway, set `SITE_IMAGE="$(scripts/published-image.sh)"` first.

## 5. Check

- Run the `infra` workflow manually (Actions, infra, Run workflow). The preview job should sign in with OIDC and show the planned resources for `dev`.
- Locally: `make infra-preview STACK=dev` after `az login` and `pulumi login`.

## Before the first `pulumi up`

- Read [threat model 0002](threat-models/0002-azure-environment.md) and decide its open risks.

- Accept ADR 0010, or record the changes you want.
- Merge the site's Blob storage and Application Insights wiring (pull request 29) and let main publish an image that includes it.

## The first `pulumi up` (owner only; dev's later deploys come from CD)

1. `pulumi up --stack dev`. The first run waits two minutes after granting the site's Key Vault access, so the app's first revision can read its secrets.
2. `up` creates the site's database user itself (resource `site-database-user`, script `infra/UmbracoSample.Infra/scripts/grant-site-database-user.sh`). Azure has no API for contained database users, so the script runs SQL as you, through the small `UmbracoSample.Infra.DatabaseUser` tool (the site's own SQL driver): run `up` signed in to `az` as a member of the SQL administrator group. It needs nothing beyond `dotnet` and `az`, which `up` already uses. The server admits only the apps subnet, so the script opens a firewall rule for your public IP (found with api.ipify.org, or set `DEPLOYER_IP`) and always removes it on exit. It is idempotent, rebinds the user if the site identity was replaced, and runs again only if the database or the identity is replaced. It and the one-time Key Vault wait are bash commands, so run `up` from macOS, Linux or WSL. If it fails, fix the cause and run `up` again; the site keeps restarting until the user exists. If the revision gave up meanwhile, restart it: `az containerapp revision restart -n <app> -g <resource group> --revision <latest revision>`.
   When deploys move to CD, the job needs two things for this step: the deploy identity in the SQL administrator group (its `azure/login` session is picked up), and rights to add and remove SQL firewall rules. The step re-runs only if the database or the site identity is replaced.
   Manual fallback, as a member of the SQL administrator group in the portal query editor (accept its offer to add your IP, and delete that rule afterwards), with the `siteIdentity` output as the name:
   ```sql
   CREATE USER [umbraco-sample-dev-id] FROM EXTERNAL PROVIDER;
   ALTER ROLE db_owner ADD MEMBER [umbraco-sample-dev-id];
   ```
3. Open the `siteUrl` output, sign in with `adminEmail` and the admin password, and change the password.

