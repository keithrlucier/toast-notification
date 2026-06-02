# XT-3 — Design Specification

Bind a single-use enrollment token to a hardware-backed identifier so a spent token can
re-enroll only the machine that first consumed it. Backward-compatible with the existing fleet.

---

## Identifier choice

**Primary: `MachineGuid`** — `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`.

| Candidate | Readable from agent's LeastPrivilege user context? | Stable across reboot / user change | Survives MSI uninstall→reinstall (legit path) | Notes |
|---|---|---|---|---|
| **MachineGuid** | **Yes** — plain world-readable HKLM value | Yes | Yes (OS-level GUID, MSI never touches it) | **Recommended primary** |
| Machine SID (`S-1-5-21-…`) | Hard — needs SAM/LSA access, often elevation | Yes | Yes | The RMM lock-screen scripts read SIDs, but from SYSTEM/admin against loaded hives — not the agent's context |
| Entra device SID (`S-1-12-1-…`) | No — needs Graph/Intune or HKEY_USERS hive access | Per Entra registration | Yes | Not reachable from a user-context C# agent without extra plumbing |
| Hardware UUID (SMBIOS / `Win32_ComputerSystemProduct.UUID`) | Partial — WMI call, sometimes slow/blocked | Yes | Yes (best reimage stability) | Good **optional secondary**; some VMs randomize it on snapshot restore |

**Decision:** use `MachineGuid` as the single bound identifier. It is the only candidate the
agent can read reliably where it actually runs, it is machine-unique, and it survives the exact
reinstall path the carve-out exists for. Keith's call language ("machine SID") refers to the
lock-screen RMM work; that code path is not available to the agent, so we use `MachineGuid` and
name the stored value generically (`UsedByMachineId`) to keep the door open for a composite later
without another migration churn. **Confirm at kickoff before M2.**

> Forgery bar: spoofing `MachineGuid` requires `HKLM` **write** on the original machine (to read
> it) plus full control of a second machine (to write it) — strictly deeper than the `HKLM`
> **read** that XT-M1 assumes. That is the point: the control raises the bar above the threat.

---

## Data model changes

### `EnrollmentToken` (`Models/EnrollmentToken.cs`)

Add one nullable column — the hardware id stamped at first consumption, alongside the existing
`UsedByDeviceName` / `UsedByUsername`:

```csharp
/// <summary>
/// XT-3 — hardware id (MachineGuid) of the device that first consumed this token.
/// Null for tokens consumed before XT-3 (or by a pre-XT-3 agent). When non-null, a
/// reinstall MUST present a matching id; null/mismatch is rejected (no downgrade).
/// </summary>
public string? UsedByMachineId { get; set; }
```

### `Device` (`Models/Device.cs`)

Add one nullable column — a snapshot for audit + a future tightening of the idempotent match:

```csharp
public string? MachineId { get; set; }   // XT-3 — MachineGuid at registration time. Nullable.
```

### `AppDbContext` (`Data/AppDbContext.cs`, `OnModelCreating`)

```csharp
// in Entity<EnrollmentToken>(e => { ... })
e.Property(t => t.UsedByMachineId).HasMaxLength(64);   // MachineGuid is a 36-char GUID; 64 is headroom
// in Entity<Device>(e => { ... })
e.Property(d => d.MachineId).HasMaxLength(64);
```

No index changes. The `(TenantId, TokenHash)` unique index remains the lookup key; the hardware
id is a comparison field, not a lookup key.

### Migration

- Name: next milestone in sequence — **`M18_HardwareTokenBinding`** (latest today is
  `M17_EnrollmentTokens`). Format `{timestamp}_M18_HardwareTokenBinding`.
- Two `AddColumn` ops: `EnrollmentTokens.UsedByMachineId` (nullable `varchar(64)`),
  `Devices.MachineId` (nullable `varchar(64)`). Both **nullable, additive** — no backfill.
- Applies on startup via `db.Database.Migrate()` (`Program.cs`). Because columns are nullable
  and additive, an old API instance and a new one can run against the migrated schema during a
  rolling deploy without error.
- Use a predicated/idempotent shape consistent with house migrations; never edit an already-run
  migration.

---

## DTO change

`RegisterDeviceRequest` (`DTOs/DeviceDtos.cs`) — add one optional field at the end (positional
record; append so old agents that omit it still deserialize):

```csharp
public record RegisterDeviceRequest(
    [Required] Guid TenantId,
    [Required] string DeviceName,
    [Required] string Username,
    string? OsVersion = null,
    string? AgentVersion = null,
    string? EnrollmentKey = null,
    string? MachineId = null);   // XT-3 — MachineGuid; null from pre-XT-3 agents
```

---

## Agent change (`ToastRevival.Agent`)

Compute `MachineGuid` and include it in the registration payload (`AgentClient.cs`,
`RegistrationService.RegisterAsync`). Read it once at registration:

```csharp
using Microsoft.Win32;

static string? ReadMachineGuid()
{
    try
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key  = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return key?.GetValue("MachineGuid") as string;   // null-safe; degrade to null on any failure
    }
    catch { return null; }   // never block registration on id read
}
```

Add `MachineId = ReadMachineGuid()` to the `RegisterDeviceRequest` the agent posts. **The agent
must never fail registration because the id could not be read** — a null id degrades to current
behavior. Bump the agent version (`ToastRevival.Agent.csproj`) so fleet telemetry can gate M5.

---

## Server gate (`PassesEnrollmentGateAsync`)

Add a `string? machineId` parameter (passed from `req.MachineId` at the `Register` call site).

### First consumption (atomic claim)

In the single-statement `ExecuteUpdateAsync` claim, stamp the id alongside the existing fields —
**no extra round-trip, the claim stays atomic** (preserves XT-1 / XT-L1):

```csharp
.ExecuteUpdateAsync(s => s
    .SetProperty(t => t.UsedAt, now)
    .SetProperty(t => t.UsedByDeviceName, deviceName)
    .SetProperty(t => t.UsedByUsername, username)
    .SetProperty(t => t.UsedByMachineId, machineId));   // XT-3 — null from old agents
```

A token claimed by an old agent stores `UsedByMachineId = null` → it is *unbound* and degrades to
`(DeviceName, Username)` behavior on reinstall (rollout grace). A token claimed by an XT-3 agent
is *bound*.

### Reinstall carve-out — the security-critical logic {#server-gate}

Replace the two-line tuple check with a **null-tolerant, no-downgrade** comparison:

```csharp
// XT-3 — hardware-bound reinstall gate.
//   (DeviceName, Username) must still match (existing behavior), AND:
//   - token UNBOUND  (UsedByMachineId null/empty, i.e. consumed pre-XT-3): allow — rollout grace.
//   - token BOUND    (UsedByMachineId set): the presented machineId MUST equal it. A null or
//                     mismatched id is REJECTED. This is the no-downgrade rule — an attacker
//                     cannot bypass the binding by simply omitting the id.
bool deviceNameMatches = string.Equals(token.UsedByDeviceName, deviceName, StringComparison.Ordinal);
bool usernameMatches   = string.Equals(token.UsedByUsername,   username,   StringComparison.Ordinal);

bool machineMatches = string.IsNullOrEmpty(token.UsedByMachineId)
    ? true                                                                   // unbound (legacy) token
    : !string.IsNullOrEmpty(machineId)                                       // bound: presenter must
        && string.Equals(token.UsedByMachineId, machineId, StringComparison.Ordinal);

return deviceNameMatches && usernameMatches && machineMatches;
```

**The bug to avoid:** do **not** write `machineId is null => true`. That lets an attacker on a
different machine omit the id and pass against a *bound* token — a downgrade bypass. Null
tolerance keys off **`token.UsedByMachineId`** (was the token ever bound?), never off the
presented value.

### `Register` endpoint

- Pass `req.MachineId` into `PassesEnrollmentGateAsync`.
- Stamp the snapshot on the `Device` row in both branches: `existing.MachineId = req.MachineId`
  (re-register) and `device.MachineId = req.MachineId` (new device).
- **Keep the idempotent device match on `(TenantId, DeviceName, Username)` for now.** Tightening
  it to require a hardware-id match is deferred to M5 (post-rollout), so re-imaged/old devices
  are not locked out mid-transition.

---

## Dashboard surface (admin visibility)

- `EnrollmentTokenDto` (`DTOs/EnrollmentTokenDtos.cs`): add `string? UsedByMachineId`.
- `ListEnrollmentTokens` (`DevicesController`): project `t.UsedByMachineId` into the DTO.
- Dashboard `EnrollmentTokens.tsx`: for a *used* token, show whether it is hardware-bound
  ("Bound to this device" vs. "Legacy (name match only)"). UI only — no gate logic in the client.
  Follow existing ToastRevival CSS (no new tokens; `--accent`, `.field` pattern).

---

## Backward-compatible rollout (phases)

| Phase | Who sends id | Token binding | Reinstall gate | XT-M1 status |
|---|---|---|---|---|
| **Today** | nobody | none | `(name, username)` | OPEN |
| **M1–M4 shipped** | XT-3 agents only | new agents → bound; old agents → unbound | bound tokens hardware-checked; unbound fall back | OPEN but shrinking — every new XT-3 enrollment is protected |
| **M5 (gated)** | ≥ threshold of fleet | claims with null id rejected on fresh tokens | all new tokens bound; hardware required | **CLOSED** |

M5 (the actual close) is gated on **fleet telemetry** — a threshold % of active devices reporting
an XT-3 agent version. Enabling it early would reject re-imaged devices running old agents. Track
the threshold in `MILESTONES.md` M5 acceptance.

---

## Files touched (summary)

| Layer | File | Change |
|---|---|---|
| Model | `Models/EnrollmentToken.cs` | `+ UsedByMachineId` |
| Model | `Models/Device.cs` | `+ MachineId` |
| Schema | `Data/AppDbContext.cs` | 2 `HasMaxLength` props |
| Migration | `Data/Migrations/…_M18_HardwareTokenBinding.cs` | 2 nullable `AddColumn` |
| DTO | `DTOs/DeviceDtos.cs` | `RegisterDeviceRequest + MachineId` |
| DTO | `DTOs/EnrollmentTokenDtos.cs` | `EnrollmentTokenDto + UsedByMachineId` |
| Server | `Controllers/DevicesController.cs` | gate signature + atomic claim `SetProperty` + carve-out logic + `Register` plumbing + list projection |
| Agent | `ToastRevival.Agent/AgentClient.cs` (+ csproj version) | `ReadMachineGuid()` + payload field |
| Dashboard | `ToastRevival.Dashboard/.../EnrollmentTokens.tsx` | bound/legacy badge |
| Tests | `tests/…/SecurityTests.cs` | see [TEST-PLAN.md](TEST-PLAN.md) |
