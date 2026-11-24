# Infrastructure setup (owner checklist)

These steps need the owner: they create cloud identities, store a secret and change repository settings, which agents may not do (CLAUDE.md). Nothing here deploys anything. Deploying (`pulumi up`) stays a separate, explicit owner action.

## 1. Pulumi Cloud

1. Create the stacks from `infra/UmbracoSample.Infra`: `pulumi stack init <org>/umbraco-sample/dev` (and `prod` later).
2. Set the real budget contact, replacing the placeholder: `pulumi config set budgetContactEmail <address> --stack dev`. Keep the committed `Pulumi.dev.yaml` value as the placeholder, or move the value to Pulumi config only.
3. Set the image once the first image is published: `pulumi config set image ghcr.io/<owner>/umbraco-sample@sha256:<digest> --stack dev`.
4. Create a Pulumi access token for CI, scoped as narrowly as your plan allows (an organization or team token limited to this project's stacks).

## 2. Azure

1. Create an app registration (or user-assigned identity) for CI previews.
2. Add a federated credential for GitHub Actions: issuer `https://token.actions.githubusercontent.com`, subject `repo:<owner>/umbraco-sample:environment:infra-preview`.
3. Grant it **Reader** on the subscription (or on the resource group once it exists). Preview needs only read access. Do not grant Contributor until deploys are designed.

## 3. GitHub repository

1. Create an environment named `infra-preview`. Restrict it to the `main` branch and pull requests from this repository if you want previews only after review.
2. In that environment, add the secret `PULUMI_ACCESS_TOKEN`.
3. Add repository variables: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `PULUMI_ORG`, and finally `PULUMI_PREVIEW_ENABLED=true`.

## 4. Check

- Run the `infra` workflow manually (Actions, infra, Run workflow). The preview job should sign in with OIDC and show the planned resources for `dev`.
- Locally: `make infra-preview STACK=dev` after `az login` and `pulumi login`.

## Before the first `pulumi up`

- Accept ADR 0010, or record the changes you want.
- Decide the "allow Azure services" SQL firewall rule (ADR 0010, Consequences).
- Wire the site to Blob storage and Application Insights (ADR 0010, Consequences).
