# XT-3 — Milestones

Build top to bottom. M1–M4 ship with **zero behavior change for the current fleet**
(null-tolerant). M5 is the milestone that actually closes XT-M1 and is **gated on fleet
telemetry** — do not start it until the gate is met. Each milestone closes only when its
acceptance criteria are met; record a closure block with the date.

Scope note: M1–M4 add no migration risk beyond two nullable columns and are independently
shippable. Prefer shipping M1+M2+M3 together (schema + agent + server) so a bound token is never
created without the gate to honor it, then M4 (dashboard) as a follow-up.

---

## M1 — Schema + migration (server only)

### Deliverables
- `EnrollmentToken.UsedByMachineId` (nullable) + `Device.MachineId` (nullable).
- `AppDbContext` `HasMaxLength(64)` on both.
- Migration `…_M18_HardwareTokenBinding` — two nullable `AddColumn`, no backfill, idempotent.

### Acceptance
- `dotnet build` green; model validates (no-DB model probe like the DGM-M2 check).
- Migration applies cleanly on a throwaway DB and is a NO-OP on second run.
- Existing `SecurityTests` enrollment-token suite still green (no behavior change).

### Code Sweep
- Step 4 (user context): the new columns are nullable — confirm every read path treats null as
  "unbound", never as a match.

### Closure
- _(date / commit / evidence)_

---

## M2 — Agent computes + sends `MachineId`

### Deliverables
- `ReadMachineGuid()` in the agent; `MachineId` added to the registration payload.
- Never fail registration on a null/failed id read (try/catch → null).
- Agent version bump (`ToastRevival.Agent.csproj`) so M5 telemetry can gate on it.
- **Confirm the identifier choice** (DESIGN-SPEC "Identifier choice") before writing this — if
  Keith overrides `MachineGuid`, this is where it changes.

### Acceptance
- Agent build green; a manual run logs a non-null `MachineGuid` on a real Windows box.
- Registration still succeeds when the registry read is forced to fail (degrades to null).
- Server (still on M1 code) accepts the new field and stores it (round-trip check).

### Code Sweep
- Step 4 (vendor-native): `MachineGuid` is the OS-native machine id — no reinvented fingerprint.
- Confirm read works in the agent's actual run context (LeastPrivilege user), not just elevated.

### Closure
- _(date / commit / evidence)_

---

## M3 — Server gate honors the binding (null-tolerant, no-downgrade)

### Deliverables
- `PassesEnrollmentGateAsync` takes `machineId`; atomic claim stamps `UsedByMachineId`.
- Reinstall carve-out uses the null-tolerant / no-downgrade logic from DESIGN-SPEC `#server-gate`.
- `Register` plumbs `req.MachineId` to the gate and snapshots `Device.MachineId` in both branches.
- Idempotent device match **unchanged** (stays `(TenantId, DeviceName, Username)` until M5).

### Acceptance
- New tests (TEST-PLAN) green: bound-token same-machine reinstall allowed; bound-token
  different/absent id rejected; unbound (legacy) token still falls back to name+username.
- The atomic claim remains a single SQL statement (no read-then-write reintroduced).
- Full `SecurityTests` enrollment suite green.

### Code Sweep
- Verify the no-downgrade rule: a **bound** token + **null** presented id must be REJECTED.
  Add an explicit test asserting this (the most likely place to get it wrong).

### Closure
- _(date / commit / evidence)_

---

## M4 — Dashboard visibility

### Deliverables
- `EnrollmentTokenDto.UsedByMachineId`; `ListEnrollmentTokens` projects it.
- `EnrollmentTokens.tsx`: used tokens show "Bound to device" vs "Legacy (name match only)".

### Acceptance
- Playwright smoke on the enrollment-tokens admin view; no console errors; existing styling
  (no new CSS vars, `--accent`, `.field` pattern).
- FE types match the backend DTO (ToastRevival standing check).

### Closure
- _(date / commit / evidence)_

---

## M5 — Harden after rollout (the actual XT-M1 close) — GATED

### Gate (do not start until met)
- Fleet telemetry shows **≥ [SET THRESHOLD, e.g. 95%]** of active devices on an XT-3 agent
  version (the M2 bump). Record the measured number in the closure.

### Deliverables
- On a **fresh** token claim, reject a registration that presents a null/empty `MachineId`
  (no more unbound tokens — every new token is hardware-bound).
- Tighten the idempotent device match to incorporate `MachineId` when present.
- Flip XT-M1 → **FIXED-VERIFIED** in `REVIEW_LEDGER.md`; remove/replace the in-code XT-M1 anchor
  in `PassesEnrollmentGateAsync` with a "closed by XT-3 M5" note.

### Acceptance
- Test: a fresh token claimed with no `MachineId` is rejected (403) once hardening is on.
- Re-imaged-device path documented: a new `MachineGuid` correctly requires a fresh token.
- Residual-risk doc updated to reflect the closed state.

### Closure
- _(date / commit / evidence / measured fleet %)_

---

## Out of scope (note, don't silently drop)
- Composite id (MachineGuid + hardware UUID): possible future hardening; would reuse the same
  `UsedByMachineId` column as a delimited value or add a second column. Not needed to close XT-M1.
- Re-imaged-device auto-recovery (re-binding without a new token): explicitly NOT done — a
  re-imaged machine is a new machine for trust purposes and should get a fresh token.
