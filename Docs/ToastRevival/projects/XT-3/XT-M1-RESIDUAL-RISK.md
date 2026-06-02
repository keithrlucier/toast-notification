# XT-M1 — Residual Risk (detailed)

> Requested by Keith (2026-06-02, phone): "I want it totally 100% documented in great detail."
> This is the standing record of the gap XT-3 closes, what it is, how bad it is, and what
> protects us until XT-3 ships. Private (mirror-excluded).

## The finding in one sentence

A **spent** single-use enrollment token can re-enroll a **different** machine, because the
"same machine" check that gates a reinstall trusts the agent-reported `(DeviceName, Username)`
tuple — software strings, not hardware.

## Where it lives

`src/ToastRevival.Api/Controllers/DevicesController.cs` → `PassesEnrollmentGateAsync`, the
"already used" reinstall carve-out. After a token is consumed, a re-presentation is allowed iff:

```csharp
return string.Equals(token.UsedByDeviceName, deviceName, StringComparison.Ordinal)
    && string.Equals(token.UsedByUsername, username, StringComparison.Ordinal);
```

Both `deviceName` (`Environment.MachineName`) and `username` (`Environment.UserName`) are sent
by the agent in `RegisterDeviceRequest` (`AgentClient.cs`). Neither is hardware-backed; both are
attacker-controllable in a forged registration request.

The same `(TenantId, DeviceName, Username)` tuple is also the key for the **idempotent device
match** in `Register` (the lookup that reuses an existing `Device` row instead of minting a new
seat). So the weakness spans both the token carve-out and the device-reuse path.

## Why the carve-out exists (do not just remove it)

The MSI wipes per-user `config.json` on uninstall, so a clean reinstall on the same machine
**must** be able to re-register. In an RMM fleet, the same deploy command (carrying the same
token) is pushed to a machine that is reinstalled/re-imaged. The carve-out is what lets that
work silently. Removing it — i.e. requiring a freshly issued token on every reinstall — is
**rejected**: it breaks silent mass deployment across hundreds of devices (every reinstall would
need a human to issue a new token). That rejection is the whole reason XT-3 exists.

## The attack path

Preconditions the attacker needs:
1. **A spent token's plaintext.** The agent stores the enrollment value the MSI wrote to
   `HKLM\SOFTWARE\Toast\...`. Reading it requires `HKLM` read access on a target machine.
2. **The original `(DeviceName, Username)`.** The Windows computer name and the enrolling
   user's name. Often guessable / discoverable (naming conventions, AD, the device list).

Then:
3. The attacker POSTs `/api/devices/register` to the tenant with the spent token and the
   original device name + username.
4. The carve-out matches → the gate passes.
5. The idempotent device match finds the **existing** `Device` row and **refreshes its
   credentials** onto the attacker's request → the attacker receives a valid **device JWT** for
   that device identity.

Net effect: **credential issuance for an existing device identity to a machine the attacker
controls.** No new seat is minted (the existing row is reused), so it is not a license-bypass;
it is a device-identity takeover.

## Severity — why it is Medium, not Critical

The gating precondition is **`HKLM` read on the target machine**, which already implies
substantial machine compromise. Specifically:

- An attacker with `HKLM` read on the box can, in most deployments, also read the agent's
  **`config.json`** (the live device JWT + signing key) directly — in which case they do not
  need to re-enroll at all.
- The **marginal** escalation XT-M1 grants is the narrow case where the attacker can read the
  machine-wide `HKLM` enrollment value but **not** the per-user `config.json` (e.g. different
  user profile / ACL split). There, re-enrollment mints a device JWT they could not otherwise
  obtain. Real, but narrow.

So: a defense-in-depth gap that turns "partial machine read" into "device credential," not an
open door from the outside. It does **not** expose cross-tenant data (tenant scope is enforced),
and it does **not** mint new seats.

## What protects us today (until XT-3)

- **Tokens are single-use and expiring.** A fresh token is consumed atomically (XT-1 / XT-L1);
  only a *spent* token is exposed to this carve-out, and only within the bound identity.
- **Tenant isolation is intact.** The token lookup, atomic claim, and re-read are all tenant-
  scoped (XT-L1/XT-L2, shipped 2026-06-02). A leaked token id cannot cross tenants.
- **Revocation works.** An admin can revoke a token; a revoked token (used or not) fails the
  gate before the carve-out is reached.
- **No new seat.** The idempotent match reuses the existing row, so the blast radius is one
  already-existing device identity, not fleet growth.
- **The anchor.** The carve-out carries an in-code XT-M1 anchor documenting the decision, so the
  gap is tracked, not silently re-flagged, and not silently "fixed" in a way that breaks RMM.

## What XT-3 changes

XT-3 adds a hardware-backed identifier (recommended: `MachineGuid`) to the binding. After XT-3,
the carve-out requires the re-enrolling machine to present the **original machine's** hardware
id — which the attacker cannot produce without booting that specific hardware (or `HKLM`
**write** to forge `MachineGuid`, a strictly deeper compromise than the `HKLM` read assumed
here). That closes the marginal escalation. See [DESIGN-SPEC.md](DESIGN-SPEC.md).

## Residual risk that remains after XT-3 (be honest)

- **During rollout**, tokens consumed by old (pre-XT-3) agents are *unbound* and still fall back
  to `(DeviceName, Username)`. The gap persists for those tokens until the fleet upgrades and M5
  (the hardening step) is enabled. This is a deliberate, time-boxed trade to avoid a flag day.
- **`MachineGuid` is forgeable by `HKLM` *write*.** An attacker with `HKLM` write on the
  original machine can read the GUID and, on a second machine they also fully control, write the
  same GUID. This is a much higher bar than XT-M1's `HKLM` read, and such an attacker already
  owns both machines — out of scope for this control.
- **Re-imaged machines** legitimately get a new `MachineGuid`; they correctly require a fresh
  token (they are, for trust purposes, a new machine). This is the intended behavior, not a gap.
