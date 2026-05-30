# REVIEW LEDGER — Cold Code Review (2026-05-30)

**Pass date:** 2026-05-30
**Reviewer:** Carl — cold pass at Keith's direction.
**HEAD:** cf38458
**Prior artifacts:** All previous review files (REVIEW_LEDGER.md, REVIEW_AUDIT.md, and every ledger under both `Docs/review_history/` and `docs/review_history/` — the repo has two case-differing dirs git tracks separately) were removed before this pass so it carries no anchors and re-judges every control on current source. They remain recoverable from git history.

Read REVIEW_LEDGER.md / latest review_history? No — removed for a clean cold pass (recoverable from git history).
Closed-pass anchors honored? No — cold pass by design; no in-code anchors were treated as authoritative, each control re-verified.

**Method:** every claim below is anchored to a `git`-verified line (`git grep -n`, blob-hash checked against disk with `git hash-object` vs `git rev-parse HEAD:`), not file content read in isolation. Earlier this session a read returned a fabricated finding set against files that do not exist in this repo; as a guard, findings here cite the exact `file:line` the grep returned and the structural fact it proves.

---

## Summary

| Severity | Count | Open |
|----------|-------|------|
| Critical | 0 | 0 |
| High     | 1 | 1 |
| Medium   | 0 | 0 |
| Low      | 0 | 0 |
| **Total**| **1** | **1** |

---

## Critical

None found.

---

## High

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|
| Agent-H1 | High | OPEN | `src/ToastRevival.Agent/SelfUpdateService.cs:240,247` (+ staging at `:304,:421`) | Residual verify→use TOCTOU. The SYSTEM updater verifies the staged MSI at line 240 (`IsSignedByToast2IT(msiPath)` — opens the file for X509 + WinVerifyTrust) and then, as a **separate** operation at line 247, calls `ExecuteMsiexec("/i \"{msiPath}\" ...")` → `Process.Start` (line 220), which re-opens the same path. The MSI is staged at `GetProgramDataDir()/"update"` = `%ProgramData%\Toast2IT\Toast Notification\update\` (lines 304, 421), written by the **user-context** agent — so the interactive user is CREATOR OWNER of that file and can replace it. | The SYSTEM updater task runs `msiexec` elevated. A local non-admin who swaps the verified MSI for a malicious one in the window between the SYSTEM-side re-verify (240) and msiexec's open (247→220) gets code execution **as SYSTEM** — a local privilege escalation. The prior C1 fix's SYSTEM-side re-verify *narrows* the window; because verify and use are two separate opens against a user-writable path, it does not *close* it. | Medium |

**Recommended fix (deterministic; sidesteps the prior "ACL breaks the user write path" objection):** have the SYSTEM updater **copy** the user-staged MSI into an Admins/SYSTEM-only directory, then re-verify Authenticode and run `msiexec` against that protected copy. Once the bytes live where the unprivileged user cannot touch them, verify-time and use-time content are guaranteed identical. (Alternative: set the staging dir ACL to Admins+SYSTEM-only at WiX install time and route the user-context write through the SYSTEM task — but copy-to-protected-dir is simpler and doesn't require granting the user write there.)

---

## Medium / Low

None found. (No nitpicks recorded — Low is reserved for things worth actually fixing.)

---

## ANCHOR-CHALLENGE

None.

---

## Verified CLEAN this pass (git-anchored)

- **IDOR — every request-id `FindAsync` site is tenant-guarded.** Confirmed by reading the lines immediately following each lookup (consistent across Bash `git grep -A` and PowerShell `git show`):
  - `ApiKeysController.cs:88` → `:90` `if (key.TenantId != tenantId) return NotFound();`
  - `AssetsController.cs:185` → `:186` `if (asset is null || asset.TenantId != tenantId) return NotFound();`
  - `BlocklistController.cs:75` → `:77` `if (entry.TenantId != tenantId) return NotFound();`
  - `DevicesController.cs:181` → `:183`, and `:352` → `:354` (`device.TenantId != tenantId → NotFound()`)
  - `UsersController.cs:82` → `:84`, and `:100` → `:102` (`user.TenantId != GetTenantId() → NotFound()`)
  - `TenantController.cs` `FindAsync(tenantId)` sites key on the JWT tenant claim itself (the id IS the claim) — safe by construction, not request-controlled. Not an IDOR surface.
- **Anonymous endpoints enumerated** (`git grep -n "AllowAnonymous"`): `HealthController:35` (health), `DevicesController:288` (agent version) / `:307` (uninstall-script-info), `BillingController:238` (Stripe webhook). `uninstall-script-info` read cleanly earlier — hardcoded filename, config-sourced root, no request input into the path, returns only `url`/`lastModifiedUtc`/`sizeBytes`. **The Stripe webhook signature path was NOT logic-verified this pass** — see below.
- **`AssetsController.Delete` path-traversal guard present** (`:188-197`): file path is reconstructed from `tenantId` + filename and `Path.GetFullPath(...).StartsWith(allowedRoot)` checked before delete — not taken from the raw stored URL.

---

## NOT reviewed at logic level this pass (next session)

Structurally swept (FindAsync / IgnoreQueryFilters / AllowAnonymous locations enumerated) but not read function-by-function: `BillingController` (esp. the anonymous webhook signature verification), `AuthController` (register / reset-password / MFA), `SsoController`, `SystemController` / platform-admin surface, `TenantController`, the agent overlay/templates/lockscreen/tray, the Dashboard frontend, the WiX installer, and the RMM scripts. These should get a faithful logic-level cold pass.

---

## Process note

A read early in this session fabricated an entire finding set against non-existent files (a wrong assumed repo layout: there is **no** top-level `agent/` or `api/` — the real tree is `src/ToastRevival.{Agent,Api,Dashboard}`). It was caught when `git` output contradicted it. Standing guard for this repo: cite the exact `git grep`-returned `file:line` for every load-bearing claim; treat any read not corroborated by a git query as unverified.
