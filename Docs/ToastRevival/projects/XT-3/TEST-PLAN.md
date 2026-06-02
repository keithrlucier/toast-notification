# XT-3 — Test Plan

Extends the enrollment-token suite added for XT-L3 in
`tests/ToastRevival.Api.Tests/SecurityTests.cs`. Same harness: `SecurityHarness.SeedTenantAsync`,
a dedicated `ApiTestFactory` per register-path test (fresh `device-per-hour` window),
`SeedEnrollmentTokenAsync` for direct seeding (lowercase-hex `TokenHash`), real Postgres fixture
(`ExecuteUpdateAsync` does **not** run on EF InMemory).

To seed a *bound* token directly, set `UsedAt`, `UsedByDeviceName`, `UsedByUsername`, and
`UsedByMachineId` on the row (it represents an already-consumed token).

## Server gate (M3) — the security core

| Test | Setup | Assert |
|---|---|---|
| `BoundToken_SameMachineReinstall_Allowed` | bound token (UsedByMachineId = `G1`), register with same name+username+`MachineId=G1` | 200 |
| `BoundToken_DifferentMachineId_Rejected` | bound token (`G1`), register with same name+username but `MachineId=G2` | 403 — **the XT-M1 close** |
| `BoundToken_NullMachineId_Rejected` | bound token (`G1`), register with same name+username, **no** `MachineId` | 403 — **no-downgrade rule** |
| `UnboundLegacyToken_NameMatchReinstall_Allowed` | used token with `UsedByMachineId = null`, register with matching name+username (with or without id) | 200 — rollout grace |
| `FreshToken_NewAgentClaim_BindsMachineId` | fresh token, register with `MachineId=G1` | 200; DB row now has `UsedByMachineId = G1` |
| `FreshToken_OldAgentClaim_LeavesUnbound` | fresh token, register with no `MachineId` | 200; DB row `UsedByMachineId` stays null (unbound) |
| `ConcurrentClaims_BindWinnerMachineId` | fresh token, two concurrent registers (`G1`, `G2`) | exactly one 200; the won row's `UsedByMachineId` = the winner's id; loser 403 |

## Atomicity / regression
- The existing `EnrollmentToken_ConcurrentClaimsOfSameToken_OnlyOneWins` must still pass — the
  added `SetProperty(UsedByMachineId)` must not break the single-statement claim.
- All current XT-L3 enrollment tests stay green after the gate signature change.

## M5 (gated hardening) — add when M5 is enabled
| Test | Setup | Assert |
|---|---|---|
| `Hardening_FreshTokenClaim_NullMachineId_Rejected` | hardening on; fresh token; register with no `MachineId` | 403 (no more unbound tokens) |
| `Hardening_FreshTokenClaim_WithMachineId_Allowed` | hardening on; fresh token; register with `MachineId=G1` | 200; bound |

## Agent (M2) — manual / unit
- Unit: `ReadMachineGuid()` returns the value from a stubbed registry; returns null on a forced
  read failure (never throws).
- Manual: on a real box, registration payload carries a non-null 36-char GUID; forcing the read
  to fail still registers (degrades to null).

## Verification gates (every milestone)
- `dotnet build` green; model probe green (M1).
- Full `SecurityTests` enrollment suite green on the Postgres fixture.
- No new EF InMemory dependency introduced for the atomicity tests.
