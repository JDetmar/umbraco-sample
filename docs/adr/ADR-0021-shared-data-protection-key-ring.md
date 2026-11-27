# 0021. One Data Protection key ring in Blob storage, wrapped by a Key Vault key

- Status: Proposed (awaiting the owner's acceptance)
- Date: 2026-11-27
- Decider: owner (the packages and this option were approved in the project thread on 2026-11-27, recorded in pull request 63; acceptance of this record is pending)
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

- New revisions and restarts are expected to keep antiforgery and render tokens readable and editors signed in. This is verified after the first dev deploy, not before: Umbraco's back-office token server may also hold keys of its own per process, and if so, some sign-in tokens would still not survive a restart. R3 is met once dev runs this. Enabling the form still waits on the edge (R2).
- A second replica still waits on the other DEV-015 items: server roles and the uSync import.
- Rotation:
  - Data Protection rotates its own keys every 90 days by default. Each new key is wrapped when it is written.
  - The wrapping key itself does not rotate. Azure Resource Manager only creates the first version of a key and returns an existing key unchanged, and no rotation policy is set.
  - Rotating it is a deliberate owner step, a new version made in Key Vault. The versionless URI in configuration means new keys then use the new version, and old keys stay readable because each wrapped key records the version that wrapped it.
  - Changing the key's type or size in code does nothing to an existing key.
  - Resource Manager cannot delete vault keys, so `pulumi destroy` or a rename leaves the key behind.
  - Deleting the key, or an old version of it, makes the ring unreadable. The vault's purge protection and 30-day soft delete guard against that.
- The fixed application name also changes Umbraco's application discriminator, which it uses for some local temporary paths. They move once on the first deploy, which is harmless.
- A key-ring failure, such as an unpropagated role or an unreachable vault, shows up only on first use: back-office and form requests fail while `/health/ready` stays green. A readiness check that loads the ring is a possible follow-up.
- Changing the application name, or moving the blob, orphans the ring, with the same effect as today's restarts.
