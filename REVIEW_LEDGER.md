# REVIEW LEDGER — Cold Code Review (2026-05-28)

**Pass date:** 2026-05-28
**Reviewer:** Carl (cold reviewer, no prior context beyond ledger archive)
**Scope:** Net-new code since prior ledger (commit `378385d`, 2026-05-25) — M12 Device Appearance feature + 0.4.9→0.4.15 overlay iteration. 32 files changed, +4270/−22.
**Prior ledger archived to:** `docs/review_history/REVIEW_LEDGER_2026-05-25_1.md`

Read REVIEW_LEDGER.md / latest review_history? Yes
Closed-pass anchors honored? Yes — all 10 rows from 2026-05-25 spot-checked against current source; no regression of FindAsync IDOR pattern in net-new endpoints.
Files scanned: 32 (full diff against `378385d`)
Files with anchors found and respected: 1 (`AuthContext.tsx:83` — INFO-01 REJECTED-by-design anchor intact; untouched in this scope.)

---

## Summary

| Severity | Count | Open | Fixed | Rejected |
|----------|-------|------|-------|----------|
| Critical | 0     | 0    | 0     | 0        |
| High     | 0     | 0    | 0     | 0        |
| Medium   | 2     | 0    | 2     | 0        |
| Low      | 2     | 0    | 1     | 1        |
| ANCHOR-CHALLENGE | 0 | 0 | 0 | 0    |
| **Total**| **4** | **0** | **3** | **1** |

**Remediation pass complete (2026-05-28):** all 4 findings terminal. 3 FIXED-VERIFIED (Agent-M1 bitmap leak, Frontend-M1 two-step confirm, Agent-L1 Content-Type guard); 1 REJECTED-VERIFIED with in-source anchor (Api-L1 — shipped migration not edited per Anthony's standing rule, controller-side caps + admin-gate provide depth). Verification gates: `dotnet build` on Agent + Api both succeeded 0 warnings / 0 errors; `npx tsc --noEmit` on Dashboard clean.

---

## Critical

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|

None found.

---

## High

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|

None found.

---

## Medium

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|
| Agent-M1 | Medium | FIXED-VERIFIED | `src/ToastRevival.Agent/DesktopOverlayService.cs:255` | Inline scratch `Bitmap` leak in `RenderBitmap`. `using (var scratch = Graphics.FromImage(new Bitmap(1, 1)))` — the `using` disposes `Graphics`, but the inline `new Bitmap(1, 1)` is not bound to anything and never disposed. Every Apply() leaks one HBITMAP + the GDI+ ARGB buffer. | Apply() runs at agent startup and on every appearance config push from the server. Long-running endpoints with frequent admin reconfigures will accumulate GDI objects; eventually `OutOfMemoryException` from GDI+ on the next paint. **Fix shipped:** split into `using var measureBmp = new Bitmap(1, 1); using var scratch = Graphics.FromImage(measureBmp);`. Both disposed via stacked using declarations. | High |
| Frontend-M1 | Medium | FIXED-VERIFIED | `src/ToastRevival.Dashboard/src/components/DeviceAppearanceCards.tsx:391-398` | Lock-screen "Remove" button clears `imageUrl` on a single click with no two-step confirm. | A misclick removes the tenant's branded lock-screen image, and on the next Save the agent fleet will restore each device's original lock screen. **Fix shipped:** added `removeArmed` state; first click flips label to "Confirm remove?" with `var(--status-error)` color, second click clears `imageUrl`, `onBlur` resets armed state, and `useEffect` on `imageUrl` clears armed when an upload replaces the image. Matches Diana's locked two-step inline confirm pattern. | High |

---

## Low

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|
| Agent-L1 | Low | FIXED-VERIFIED | `src/ToastRevival.Agent/LockScreenService.cs:60-66` | No `Content-Type` check on the downloaded lock-screen image before persisting and handing to `LockScreen.SetImageFileAsync`. | Defense-in-depth. **Fix shipped:** added one-line guard rejecting any response whose `Content-Type` MediaType is not `image/jpeg` or `image/png`, logged via DiagLog. Symmetric with API's extension allowlist. | Medium |
| Api-L1 | Low | REJECTED-VERIFIED | `src/ToastRevival.Api/Data/Migrations/20260527195615_M12DeviceAppearance.cs:13-49` | `DesktopOverlayCustomText`, `DesktopOverlayFields`, `DesktopOverlayPosition`, `LockScreenImageUrl` added as PostgreSQL `text` (unbounded). | **REJECTED-by-design:** editing a shipped migration is prohibited (Anthony's standing rule). Every write path is admin-gated and enforces controller-side caps (CustomText<=80, JoinFields whitelists ~70 bytes of canonical keys, LockScreenImageUrl is server-constrained to `/assets/lockscreen/`). Schema cap can ride on the next net-new migration that touches these columns. Anchor written at migration class header per ANCHOR FORMAT. | Medium |

---

## ANCHOR-CHALLENGE

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|

None found.

---

## Top fixes (in order)

1. **Agent-M1** — Fix the `RenderBitmap` scratch-bitmap leak (`DesktopOverlayService.cs:255`). One line, real GDI handle accumulation on the most-painted path of the new feature.
2. **Frontend-M1** — Add two-step confirm on the lock-screen Remove button (`DeviceAppearanceCards.tsx:391-398`). Matches Diana's locked pattern; one misclick currently strips fleet branding.
3. **Agent-L1** — Add a `Content-Type` guard in `LockScreenService.ApplyAsync` for symmetry with the API's extension allowlist. Defense-in-depth only.
4. **Api-L1** — Add `HasMaxLength` to the appearance text columns next time a migration lands. Not worth a dedicated migration.

---

## Notes on what was reviewed and NOT flagged

- **IDOR regression check:** every new tenant-scoped endpoint (`/api/tenant/overlay`, `/api/tenant/lockscreen`, `/api/tenant/lockscreen-image`, `/api/devices/appearance-config`) extracts `tenantId` from JWT claims and either uses `FindAsync(tenantId)` (safe — `Tenant` has no global query filter and the lookup IS by the caller's own tenantId from a server-signed claim) or scopes via `FirstOrDefaultAsync`. No FindAsync-on-route-id pattern was introduced.
- **Lock-screen URL injection:** `NormalizeLockScreenUrlForStorage` (`TenantController.cs:394-409`) constrains stored URLs to `/assets/lockscreen/` only. An admin cannot weaponize the fleet to download from an arbitrary host. Good defense-in-depth.
- **Bitmap returned from `RenderBitmap`:** the outer `var bmp` (line 276) IS disposed by the caller at line 163 (`using var bmp = RenderBitmap(...)`). Only the inline measurement Bitmap at line 255 leaks.
- **PushLayeredBitmap GDI pairing** (`DesktopOverlayService.cs:331-366`): `CreateCompatibleDC`/`DeleteDC` paired in finally; `GetHbitmap`/`DeleteObject` paired; `SelectObject` saves+restores old handle. Clean.
- **WinForms SynchronizationContext** (per 0.4.11 hotfix): verified `Program.cs` installs `WindowsFormsSynchronizationContext` before any Post — anchor `FIX-DESKTOP-OVERLAY-001` lineage intact.
- **WorkerW abandoned path:** the v0.4.12-0.4.13 WorkerW/SetParent experiment was fully reverted in v0.4.14 — no dead code branches remain in `DesktopOverlayService.cs`. Iteration history is in the class doc comment (lines 13-37), not in code.
- **TenantController.GetLockScreen returns relative URL** while DevicesController.GetAppearanceConfig wraps with `ToPublicUrl`: this is intentional and documented inline at `TenantController.cs:352-355` ("the dashboard loads it same-origin"). Dashboard's `tenantLogoUrlForBrowser` handles the resolution. Not a defect.
- **Dashboard banned-terms check:** no "persona", "audio drama", "ToastRevival", "jailbreak" in `DeviceAppearanceCards.tsx`, `TenantSettings.tsx`, or the marketing pages. Product name is "Toast Notification" throughout.
- **CSS variables / classes:** `DeviceAppearanceCards.tsx` uses `--accent`, `--bg-tertiary`, `--text-secondary`, `--text-dim`, `--status-error`, `--radius-sm` — all valid. No `--accent-primary`, `form-label`, `form-input`, `form-select`, or `btn-danger-ghost` (the standing don't-exist list).
- **Save state independence:** `DesktopOverlayCard` and `LockScreenCard` are separate components with isolated `useState`/save/error — Diana's spec is honored.
- **localStorage JWT (INFO-01 from prior pass):** anchor at `AuthContext.tsx:83` intact, REJECTED-by-design reasoning unchanged. Not re-flagged.
