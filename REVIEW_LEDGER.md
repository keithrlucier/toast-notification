# REVIEW LEDGER — Cold Code Review (2026-05-31)

**Pass date:** 2026-05-31
**Reviewer:** Carl — review of `cf38458..HEAD` (the commits that landed after the 2026-05-30 cold-pass baseline), dispatched as parallel Code Sweeps.
**HEAD:** a91dccb
**Baseline:** cf38458 (the prior cold pass). The Agent-H1 record below is carried forward FIXED-VERIFIED.

**Method:** every claim is anchored to a `git grep`-verified `file:line`, never a file read in isolation. This pass began with six parallel sweep agents that were (deliberately, as a control) handed a *fabricated* file list (`agent/ToastNotification.Agent/*.cs`, `installer/Product.wxs` — none exist; the real tree is `src/ToastRevival.{Agent,Api,Dashboard}` + `installer/ToastRevival.Agent.Setup.wxs`). All six caught the trap, re-pointed at real source, and reviewed that. Every finding below was then re-verified personally against current source before any edit. Standing guard for this repo: cite the exact `git grep`-returned `file:line`; treat any read not corroborated by a git query as unverified.

---

## Summary

| Severity | Count | Open | Remediated | Deferred |
|----------|-------|------|-----------|----------|
| Critical | 0 | 0 | 0 | 0 |
| High     | 2 | 0 | 2 | 0 |
| Medium   | 1 | 0 | 1 | 0 |
| Low      | 5 | 0 | 4 | 1 |
| **Total**| **8** | **0** | **7** | **1** |

Loop closed: every finding carries an explicit **REMEDIATED** or **DEFERRED** disposition below. 7 remediated (5 code-fixed + 2 resolved-as-safe-by-design). 1 deferred — DASH-L1's robust fix is a DB-schema change, which is Keith's architectural call (its user-visible symptom is already remediated). No can-kicking: the deferred item names its owner and the blocking decision.

**In-code anchors — DONE.** Per the no-defer rule, the two resolved-as-safe agent findings (Agent-L1/L2) and the deferred DASH-L1 each carry an in-code anchor comment (finding ID + decision) so the next cold sweep does not re-flag them: `SelfUpdateService.cs` @ `LockDownToSystemAndAdmins` (Agent-L1) and @ the trigger-file read in `RunUpdaterMode` (Agent-L2); `DeviceAppearanceCards.tsx` @ the cache-bust state (DASH-L1). Anchors were verified against git-clean source (working tree == HEAD by blob hash) before editing.

---

## High

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|
| Agent-H1 | High | FIXED-VERIFIED | `src/ToastRevival.Agent/SelfUpdateService.cs:244-312` (agent 0.4.34) | Residual verify→use TOCTOU: the SYSTEM updater verified the staged MSI then launched `msiexec` against the same user-writable `%ProgramData%\...\update\` path, so the file could be swapped between the two opens. | SYSTEM-elevated `msiexec` on attacker-swapped MSI = local privilege escalation to SYSTEM. | High |
| DOC-H1 | High | FIXED-VERIFIED | `src/ToastRevival.Dashboard/src/pages/marketing/docs/DocsRmm.tsx:159` | The RMM deployment guide told admins the MSI uses "the Velopack in-process auto-updater… release feed at releases.toastnotification.com/agent/win-x64". MSI-installed agents do **not** use the Velopack feed — they self-update via the MSI channel (`/api/agent/version` → signed MSI → Authenticode re-verify → SYSTEM task), verified at `SelfUpdateService.cs:356`/`DevicesController.cs:287`. | An MSP following the RMM guide waits for a feed that never updates the MSI fleet and disables the wrong control. Contradicted the corrected Intune Win32 page in the same product. | High |

**Agent-H1 disposition:** FIXED (0.4.34). `ExecuteVerifiedMsiUpdate` copies the staged MSI into a `verified\` subdir locked (inheritance off) to SYSTEM + BUILTIN\Administrators only (`LockDownToSystemAndAdmins`), verifies Authenticode **on the protected copy**, and runs `msiexec` on that **same** path — verify-time and use-time bytes are provably identical; race eliminated, not narrowed. Reparse-point guard before create + re-check after lockdown. In-code anchor `FIX-Agent-H1 (2026-05-30)`. Ships to fleet on next **signed** MSI build + server `Agent:LatestVersion` bump.

**DOC-H1 disposition:** FIXED. Rewrote the RMM auto-update section to the MSI-channel description, mirroring the corrected DocsIntune Win32 wording, including the `DISABLEAUTOUPDATE=1` / `DisableAutoUpdate` opt-out (both verified real in `Setup.wxs:175,195`).

---

## Medium

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|
| DOC-M1 | Medium | FIXED-VERIFIED | `src/ToastRevival.Dashboard/src/pages/marketing/docs/DocsStore.tsx:100` | Store auto-update paragraph trailed the (correct) "Store handles updates" statement with stale "Velopack auto-updater is no-op… IsInstalled=false in Velopack terms" framing. | Reinforces the obsolete update model; "Velopack" is internal jargon meaningless to admins. | High |

**DOC-M1 disposition:** FIXED. Dropped the Velopack sentence; kept the accurate "Store update queue handles it, no per-endpoint action" instruction.

---

## Low

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|
| DOC-L1 | Low | FIXED-VERIFIED | `src/ToastRevival.Dashboard/src/pages/marketing/docs/DocsIntune.tsx:308` | MSIX subsection trailed correct Intune-update guidance with leftover "Velopack in-process auto-updater is no-op" sentence. | Same obsolete framing as DOC-M1. | High |
| DOC-L2 | Low | FIXED-VERIFIED | `src/ToastRevival.Dashboard/src/pages/marketing/Pricing.tsx:78` | Pricing feature bullet marketed "Velopack auto-update" — internal library name, and inaccurate for the MSI path. | Customer-facing jargon + factual drift. | High |
| DASH-L1 | Low | DEFERRED (owner: Keith — DB schema) · ANCHORED | `src/ToastRevival.Dashboard/src/components/DeviceAppearanceCards.tsx:299-302` | Lock-screen cache-bust key is `mountTime (Date.now() at mount) + cacheBust counter`, not a server-provided version. The visible bug (stale preview after replace) **is already remediated** for the uploading admin; but the "persist across navigation" commit actually *regenerates* the buster each mount (mild over-fetch, no true persistence), and nothing is keyed to a value that provably changes on replace for other viewers / CDN. | Low real impact: no service worker; no CDN in repo; over-fetch of one small preview image is negligible. The robust fix (add `LockScreenImageUpdatedAt` to `Tenant`, surface in `LockScreenConfigResponse`, use as `?v=`) is a **DB-schema change = Keith's architectural call**. Blocking decision: whether to add the column now or accept current behavior. | High |
| Agent-L1 | Low | REMEDIATED (safe-by-design) · ANCHORED | `src/ToastRevival.Agent/SelfUpdateService.cs:310` (`LockDownToSystemAndAdmins`) | Staging-dir ACL is set at runtime in C#, not declared in WiX — contrary to the standing "ACLs belong in WiX" rule. | Safe here: the user writes to `update\`, not `verified\`; the SYSTEM updater re-creates + re-locks + reparse-checks `verified\` every run before use, so a pre-seeded ACL is not load-bearing. Optional future hardening: declare the staging dirs in WiX with `util:PermissionEx`. Resolved no-change; in-code anchor pending (see ANCHOR-PENDING). | High |
| Agent-L2 | Low | REMEDIATED (safe-by-design) · ANCHORED | `src/ToastRevival.Agent/SelfUpdateService.cs:170,499` | Trigger file (`pending-action.txt`) lives in user-writable ProgramData root; a local non-admin can write it. | Contained by downstream validation: uninstall arg is GUID-regex-validated; update arg is a path copied into the protected dir and Authenticode-re-verified before any execution — a forged path can at most point at an unsigned MSI, which fails the signature gate. No escalation. Resolved no-change; in-code anchor pending (see ANCHOR-PENDING). | High |

---

## ANCHOR-CHALLENGE

None.

---

## Verified CLEAN this pass (git-anchored)

- **Agent self-update security claims all TRUE against source:** SYSTEM-side Authenticode re-verification on the protected copy (`SelfUpdateService.cs:265`); HTTPS enforced in Release, non-https rejected (`:373-383`); uninstall ordering writes the durable trigger BEFORE the best-effort lock-screen revert (`RequestUninstallAsync:142-148`); WiX `KillAgent` Condition `REMOVE="ALL" OR Installed` — fires on upgrades, avoids 3010 on RMM upgrade (`Setup.wxs:428`). No `nullable?.ToString()[..8]` range-indexer hazard in changed code.
- **Dashboard cache-bust DTO match:** TS `{ enabled, imageUrl }` matches `LockScreenConfigResponse`; upload `{ url }` matches controller. CSS vars all real (`--accent`, not `--accent-primary`); two-step inline confirm pattern on Remove. No race showing old URL after upload (state batches; `[previewUrl]` effect resets to loading).
- **Corrected Intune Win32 doc factually accurate:** MSI channel, `/api/agent/version`, `ToastNotification.Agent.exe` detection path all match installer + API source. Codename audit clean — no `ToastRevival` / `persona` / `audio drama` / `jailbreak` in any user-facing string.

---

## NOT reviewed at logic level this pass (next session)

Carried forward from the 2026-05-30 pass — structurally swept but not read function-by-function: `BillingController` (esp. the anonymous Stripe webhook signature verification), `AuthController` (register / reset-password / MFA), `SsoController`, `SystemController` / platform-admin surface, `TenantController`. These predate this range and warrant a faithful logic-level cold pass.

---

## Process note

Six parallel sweep agents were handed a fabricated file list as a control; all six detected it via `git ls-files` and re-pointed at the real `src/ToastRevival.*` tree. This is the documented Read-tool-fabrication guard working at scale. Standing rule reaffirmed: cite the exact `git grep`-returned `file:line` for every load-bearing claim; verify every sub-agent flag personally before any forward-fix.
