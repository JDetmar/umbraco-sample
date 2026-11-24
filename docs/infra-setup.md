# Infrastructure setup (owner checklist)

These steps need the owner: they create cloud identities, store a secret and change repository settings, which agents may not do (CLAUDE.md). Nothing here deploys anything. Deploying (`pulumi up`) stays a separate, explicit owner action.

## 1. Pulumi Cloud

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
5. Make the GHCR package public once CI first publishes it from main (GitHub, your profile, Packages, umbraco-sample, Package settings, Change visibility). New packages start private, and a public package lets Container Apps pull without a stored registry credential. Then set the image, using the digest from the main CI run's summary: `pulumi config set image ghcr.io/<owner>/umbraco-sample@sha256:<digest> --stack dev`.
6. Create a Pulumi access token for CI, scoped as narrowly as your plan allows (a team token with read access to this project's stacks if your plan has it), with a 90-day expiry. Put a reminder in your calendar to rotate it (DEV-016).

## 2. Azure

1. Create an app registration (or user-assigned identity) for CI previews.
2. Add a federated credential for GitHub Actions: issuer `https://token.actions.githubusercontent.com`, subject `repo:<owner>/umbraco-sample:environment:infra-preview`.
3. Grant it **Reader** on the subscription (or on the resource group once it exists). Preview needs only read access: the stack reads no keys. Do not grant Contributor until deploys are designed.

## 3. GitHub repository

1. Create an environment named `infra-preview`, restricted to the `main` branch, with yourself as a required reviewer. The workflow previews only `main` or a manual run, because preview decrypts stack secrets; the environment rules enforce that even if the workflow changes.
2. In that environment, add the secret `PULUMI_ACCESS_TOKEN`.
3. Add repository variables: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `PULUMI_ORG`, and finally `PULUMI_PREVIEW_ENABLED=true`.

## 4. Check

- Run the `infra` workflow manually (Actions, infra, Run workflow). The preview job should sign in with OIDC and show the planned resources for `dev`.
- Locally: `make infra-preview STACK=dev` after `az login` and `pulumi login`.

## Before the first `pulumi up`

- Read [threat model 0002](threat-models/0002-azure-environment.md) and decide its open risks.

- Accept ADR 0010, or record the changes you want.
- Merge the site's Blob storage and Application Insights wiring (pull request 29) and let main publish an image that includes it.

## The first `pulumi up` (owner only)

1. `pulumi up --stack dev`. If the Container App fails with a Key Vault 403, RBAC has not propagated yet: wait five minutes and run `pulumi up` again.
2. Create the site's database user. Sign in to the `umbraco` database (Azure portal query editor, or `sqlcmd -G` against the `sqlServer` output) as a member of the SQL administrator group and run, with the `siteIdentity` output as the name:
   ```sql
   CREATE USER [umbraco-sample-dev-id] FROM EXTERNAL PROVIDER;
   ALTER ROLE db_owner ADD MEMBER [umbraco-sample-dev-id];
   ```
   Until this runs, the site cannot connect and stays unready. Umbraco then installs unattended on the next start: restart the revision if it gave up.
3. Open the `siteUrl` output, sign in with `adminEmail` and the admin password, and change the password.

