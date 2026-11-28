# 0021. One Data Protection key ring in Blob storage, wrapped by a Key Vault key

- Status: Proposed (awaiting the owner's acceptance)
- Date: 2026-11-27
- Decider: owner (the packages and this option were approved in the project thread on 2026-11-27, recorded in pull request 63; acceptance of this record is pending)
- Clauses: ES-019, ES-015, ES-023, ES-024, ES-030
- Amends: ADR 0010 (role assignments: adds a fourth, key-scoped role). That also widens the deploy identity's role condition, a review trigger of DEV-017 (threat model 0002, R4).

## Context

ASP.NET Core Data Protection protects back-office sign-in cookies, antiforgery tokens and the lead form's render tokens.
Its keys are kept on each container's disk by default. So every new revision, restart or second replica gets new keys.
Editors are signed out, any form open in a browser stops working, and a render token signed by one replica does not read on another.
Threat model 0001 (R3) requires a shared key ring before the form is enabled on any deployed environment. DEV-015 names the missing ring as one reason the site runs one replica.

## Alternatives considered

- **Blob storage, keys wrapped by a Key Vault key** (`Azure.Extensions.AspNetCore.DataProtection.Blobs` and `.Keys`). This is Microsoft's documented pattern and reuses the storage account and the vault the site already has. The keys at rest are encrypted by a key whose private part never leaves Key Vault. The cost is two packages and one more role for the site.
- **Blob storage only.** One package. The keys are protected only by storage encryption at rest and by who can read the container. Anyone with read access to the blob can forge tokens.
- **SQL Server through the official EF Core provider** (`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`, `PersistKeysToDbContext`). EF Core and its SQL Server provider already come with Umbraco, but the provider package itself is new, and this option still has drawbacks. It adds a table and a migration of ours to the database Umbraco owns. It couples the key ring to database restores: a restore brings back an older ring, so cookies and tokens issued since then stop reading. And the keys still need wrapping, so it would need the Key Vault package too. Blob plus Key Vault keeps the ring out of Umbraco's schema and restores, for the same two packages.

## Decision

The site keeps its key ring in one blob, `dataprotection/keys.xml`, in a private container of the site's storage account.
The keys in it are wrapped by an RSA 3072 key, `dataprotection`, in the site's Key Vault.
The site reaches both with its managed identity. Its access is as follows:
- On the storage account, it has the media role it already held.
- On that one key only, it has *Key Vault Crypto Service Encryption User*: get, wrap and unwrap. It cannot create, delete or export keys.

The application name is fixed (`umbraco-sample`) in every environment, including development and CI, so every replica and revision reads the same keys.

The site turns this on when both `DataProtection:KeyRingBlobUri` and `DataProtection:KeyVaultKeyUri` are set. Infrastructure sets both. With neither set (development, CI), the framework's default store and protection are used, under the same fixed application name. The site refuses to start in any of these cases:
- only one of the two is set;
- either URI carries a key, SAS token, query or fragment, or uses plain http;
- the blob URI does not name a container and a blob;
- the key URI is not exactly `https://<vault>/keys/<name>`. A versioned key URI is refused, because it would pin new keys to one version and defeat rotation.

Outside development, the lead form refuses to start without the ring.

The key is created through Azure Resource Manager, so the deploy identity needs no data-plane right on the vault. The deploy identity's role assignment condition must allow the new role (`docs/infra-setup.md`).

## Consequences

- New revisions and restarts are expected to keep antiforgery and render tokens readable and editors signed in. This is verified after the first dev deploy, not before: Umbraco's back-office token server may also hold keys of its own per process, and if so, some sign-in tokens would still not survive a restart. R3 is met once dev runs this. Enabling the form still waits on the edge (R2).
- A second replica still waits on the other DEV-015 items: server roles and the uSync import.
- Rotation:
  - Data Protection rotates its own keys every 90 days by default. Each new key is wrapped when it is written.
  - The wrapping key does not rotate automatically. This is a choice, not an Azure limit: Resource Manager accepts a rotation policy, but none is set, so that rotation stays a visible owner step. Rotate it yearly, like the imaging key, or on suspected exposure (`docs/infra-setup.md`). A rotation makes a new version in Key Vault; the versionless URI means new keys use it.
  - A new version wraps only Data Protection keys created after it. Keys already in `keys.xml` stay wrapped by the old version until they expire (90 days by default). On suspected exposure, rotate the wrapping key, then revoke all Data Protection keys so that new ones are made at once: this signs every editor out and voids any open form. Keep the old version enabled while keys it wrapped are still in `keys.xml`, because loading the ring may need to unwrap them; Data Protection does not remove expired or revoked keys on its own. That is right for routine rotation. After a compromise of the wrapping key itself, once the revoked keys are no longer needed, replace `keys.xml` with a fresh ring (signing everyone out once) so that the compromised version can be disabled rather than left enabled.
  - Resource Manager creates a key once and returns an existing key unchanged on later deploys, so changing the key's type or size in code does nothing to an existing key. Resource Manager also cannot delete vault keys, so `pulumi destroy` or a rename leaves the key behind.
  - Key Vault cannot delete a single key version, only disable it. Disabling an old version, or deleting the whole key, makes the keys it wrapped unreadable. Soft delete and purge protection (30 days) let a deleted key be recovered, but nothing guards against disabling a version. Disabling is a data-plane key-management operation (Key Vault Crypto Officer or Administrator); today only the owner holds such a role. The deploy identity cannot: it has management-plane Contributor and may assign only the four roles in its condition, none of which manages keys.
  - The blob is guarded by the storage account's blob soft delete (7 days in dev, 30 in prod), which covers an accidental overwrite or delete. The site's account-wide Storage Blob Data Contributor role (the media role) can write it.
- The application name is set in this host, not in a shared library. A second host with its own storage account and vault (ADR 0020) is separate anyway; if `KeyRing` ever moves to a shared library, each host must set its own name, so that two sites never read each other's keys.
- The fixed application name also changes Umbraco's application discriminator, which it uses for some local temporary paths. They move once on the first deploy, which is harmless.
- A key-ring failure, such as an unpropagated role or an unreachable vault, shows up only on first use: back-office and form requests fail while `/health/ready` stays green. A readiness check that loads the ring is a possible follow-up.
- Changing the application name, or moving the blob, orphans the ring, with the same effect as today's restarts.
