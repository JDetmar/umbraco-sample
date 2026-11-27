# 0021. One Data Protection key ring in Blob storage, wrapped by a Key Vault key

- Status: Accepted
- Date: 2026-11-27
- Decider: owner (packages approved in the project thread on 2026-11-27, recorded in pull request 63)
- Clauses: ES-019, ES-015, ES-023, ES-024, ES-030

## Context

ASP.NET Core Data Protection protects back-office sign-in cookies, antiforgery tokens and the lead form's render tokens.
Its keys are kept on each container's disk by default. So every new revision, restart or second replica gets new keys.
Editors are signed out, any form open in a browser stops working, and a render token signed by one replica does not read on another.
Threat model 0001 (R3) requires a shared key ring before the form is enabled on any deployed environment. DEV-015 names the missing ring as one reason the site runs one replica.

## Alternatives considered

- **Blob storage, keys wrapped by a Key Vault key** (`Azure.Extensions.AspNetCore.DataProtection.Blobs` and `.Keys`). This is Microsoft's documented pattern and reuses the storage account and the vault the site already has. The keys at rest are encrypted by a key whose private part never leaves Key Vault. The cost is two packages and one more role for the site.
- **Blob storage only.** One package. The keys are protected only by storage encryption at rest and by who can read the container. Anyone with read access to the blob can forge tokens.
- **Our own store in SQL Server.** No new package, but it means writing and maintaining security code of our own, and the keys still need wrapping.

## Decision

The site keeps its key ring in one blob, `dataprotection/keys.xml`, in a private container of the site's storage account.
The keys in it are wrapped by an RSA 3072 key, `dataprotection`, in the site's Key Vault.
The site reaches both with its managed identity. Its access is as follows:
- On the storage account, it has the media role it already held.
- On that one key only, it has *Key Vault Crypto Service Encryption User*: get, wrap and unwrap. It cannot create, delete or export keys.

The application name is fixed (`umbraco-sample`), so every replica and revision reads the same keys.

The site turns this on when both `DataProtection:KeyRingBlobUri` and `DataProtection:KeyVaultKeyUri` are set. Infrastructure sets both. With neither set (development, CI), the framework default is used. The site refuses to start in either of these cases:
- only one of the two is set;
- either URI carries a key or SAS token, or uses plain http.

Outside development, the lead form refuses to start without the ring.

The key is created through Azure Resource Manager, so the deploy identity needs no data-plane right on the vault. The deploy identity's role assignment condition must allow the new role (`docs/infra-setup.md`).

## Consequences

- New revisions and restarts no longer sign editors out or break open forms, and R3 is met once dev runs this. Enabling the form still waits on the edge (R2).
- A second replica still waits on the other DEV-015 items: server roles and the uSync import.
- Rotation:
  - Data Protection rotates its own keys every 90 days by default.
  - The Key Vault key is versionless in configuration, so a new version of the key wraps new keys.
  - Old keys stay readable, because each wrapped key records the version that wrapped it.
  - Deleting the Key Vault key, or an old version of it, makes the ring unreadable. The vault's purge protection and 30-day soft delete guard against that.
- Changing the application name, or moving the blob, orphans the ring, with the same effect as today's restarts.
