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

| Severity | Count | Open | Fixed |
|----------|-------|------|-------|
| Critical | 0 | 0 | 0 |
| High     | 1 | 0 | 1 |
| Medium   | 0 | 0 | 0 |
| Low      | 0 | 0 | 0 |
| **Total**| **1** | **0** | **1** |

All findings from this cold pass are now closed. Agent-H1 fixed in agent 0.4.34 (TOCTOU eliminated via copy-to-SYSTEM/Admin-only-dir). Ships to fleet on next signed MSI build.

---

## Critical

None found.

---

## High

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|
| Agent-H1 | High | FIXED-VERIFIED | `src/ToastRevival.Agent/SelfUpdateService.cs:244-312` (agent 0.4.34) | Residual verify→use TOCTOU. The SYSTEM updater verified the staged MSI and then, as a **separate** op, launched `msiexec` against the same `%ProgramData%\Toast2IT\Toast Notification\update\` path — a directory the **user-context** agent writes to (interactive user = CREATOR OWNER), so the file could be swapped between the two opens. | The SYSTEM updater runs `msiexec` elevated; a local non-admin swapping the verified MSI in the race window gets code execution **as SYSTEM** — local privilege escalation. | High |

**Disposition:** FIXED (0.4.34). `ExecuteVerifiedMsiUpdate` now calls `CopyToProtectedDir` FIRST — copies the staged MSI into a `verified\` subdir whose ACL is reset (inheritance disabled) to Full Control for **SYSTEM + BUILTIN\Administrators only** (`LockDownToSystemAndAdmins`), then verifies Authenticode **on the protected copy** and runs `msiexec` on that **same** path. Because the unprivileged user cannot write into the protected dir, verify-time and use-time bytes are provably identical — the race is **eliminated**, not narrowed. Hardened against the secondary vector: the parent dir is user-writable, so `CopyToProtectedDir` refuses to operate through a reparse point (deletes a pre-existing junction/symlink before create, and re-checks `FileAttributes.ReparsePoint` after lockdown). In-code anchor `FIX-Agent-H1 (2026-05-30)` at the method. Verified: agent builds Release **0 warnings / 0 errors**. NOTE: ships to the fleet only on the next **signed** MSI build (requires SafeNet token) + server `Agent:LatestVersion` bump — code fix is complete and in-repo.

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
