# XT-3 — Hardware-Bound Enrollment Tokens

**Build-mode project.** Closes finding **XT-M1** from the 2026-06-02 security review by
binding a single-use enrollment token to a hardware-backed identifier so a spent token can
re-enroll **only** the physical machine that first consumed it.

| | |
|---|---|
| **Origin** | XT-M1 (OPEN) — `REVIEW_LEDGER.md`, anchored in `DevicesController.PassesEnrollmentGateAsync` |
| **Owner** | Keith (product decision made 2026-06-02 by phone) |
| **Status** | SCOPED — not started. Awaiting build-mode kickoff. |
| **Decision** | Reject "fresh token per reinstall" (breaks RMM mass deploy). Bind tokens to a hardware id instead. |
| **Privacy** | This folder is under `Docs/ToastRevival/` → 100% excluded from the public mirror. Residual-risk detail stays internal. |

## Why this exists

XT-1 made enrollment tokens single-use and bound them to the first device that redeemed them.
But "device" is identified by the **self-reported `(DeviceName, Username)` tuple** — software
strings the agent sends, not hardware. An attacker who reads a spent token out of `HKLM` and
knows the original device name + username can re-enroll a different machine under that identity.
Bounded (HKLM read already implies machine compromise) but real. See
[XT-M1-RESIDUAL-RISK.md](XT-M1-RESIDUAL-RISK.md) for the full threat model.

XT-3 adds a hardware-backed identifier to the binding so the `(DeviceName, Username)` tuple is
no longer sufficient — the re-enrolling machine must also present the original machine's
hardware id.

## The documents

| File | What it is |
|---|---|
| [XT-M1-RESIDUAL-RISK.md](XT-M1-RESIDUAL-RISK.md) | Full threat model, attack path, severity bounding, what protects us today |
| [DESIGN-SPEC.md](DESIGN-SPEC.md) | The technical design: identifier choice, schema, DTO, agent, server gate, rollout |
| [MILESTONES.md](MILESTONES.md) | The build plan, milestone by milestone, with acceptance criteria |
| [TEST-PLAN.md](TEST-PLAN.md) | The tests XT-3 must add before it ships |

## The one decision to confirm at kickoff

**Which hardware identifier?** The design recommends **`MachineGuid`**
(`HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`) as the primary, because it is the only
candidate the agent can read reliably from its **LeastPrivilege user context** without
elevation, WMI, or Entra/Graph calls — and it survives the legitimate MSI uninstall→reinstall
path. The raw machine SID and the Entra device SID (the values the lock-screen RMM scripts
read) are **not** easily reachable from the agent's run context. Rationale and alternatives
are in [DESIGN-SPEC.md](DESIGN-SPEC.md#identifier-choice). Confirm or override before M2.

## How to run this in build mode

1. Start a build-mode session pointed at `Docs/ToastRevival/projects/XT-3/`.
2. Read [DESIGN-SPEC.md](DESIGN-SPEC.md) and confirm the identifier choice (above).
3. Work [MILESTONES.md](MILESTONES.md) top to bottom — M1 (schema) → M5 (harden after rollout).
   Each milestone has acceptance criteria; close it before moving on.
4. M1–M4 ship with **zero behavior change for the existing fleet** (null-tolerant). M5 is the
   one that actually closes XT-M1, and it is **gated on fleet telemetry** (≥X% of devices on an
   XT-3 agent) — do not run M5 until that gate is met, or it breaks re-imaged-device reinstalls.

## Guardrails (do not break)

- **No flag day.** Old agents (the whole current fleet) send no hardware id. Every server change
  in M1–M4 must degrade to today's `(DeviceName, Username)` behavior when the id is absent.
- **No downgrade.** Once a token is *bound* to a hardware id, a reinstall that presents **no**
  id (or a different one) must be **rejected** — otherwise an attacker just omits the id to
  bypass the binding. Null-tolerance applies only to *unbound* (pre-XT-3) tokens. See
  [DESIGN-SPEC.md](DESIGN-SPEC.md#server-gate).
- **Keep the atomic claim atomic.** The single-statement `ExecuteUpdateAsync` token claim
  (XT-1 / XT-L1) is the "exactly one fire" guarantee — add the id as a `SetProperty`, do not add
  a second round-trip or a read-then-write.
- **Migration applies on startup.** `db.Database.Migrate()` runs on boot; new columns are
  nullable and additive so mixed old/new API instances during deploy stay compatible.
