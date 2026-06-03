# REVIEW LEDGER — Cold Code Review (2026-06-02)

```
Read REVIEW_LEDGER.md / latest review_history?   Yes (prior ledger + Docs/review_history/REVIEW_LEDGER_2026-06-02_1.md read)
Closed-pass anchors honored?                       Yes (FIX-Agent-H1, REVIEW Agent-L1, SES-2/SES-3 read; one ANCHOR-CHALLENGE filed)
Files scanned:                                     19 (post-2026-06-02-ledger diff + adjacent: agent self-update, API DevicesController + hub, dashboard Devices, RMM PowerShell scripts, WiX installer)
Files with anchors found and respected:            2 (SelfUpdateService.cs, DevicesController.cs)
```

> **Cold pass — 2026-06-02 (Carl + 3 parallel Abish review agents).** Scope: the code committed
> AFTER the prior ledger (commit `9532252`, the AUTH-H1/DGC remediation) → HEAD `2c980a2` — i.e. the
> v0.4.36/v0.4.37 work that the prior pass never saw: the agent self-update **msiexec-via-standalone-
> apply-task** rewrite (`SelfUpdateService.cs` +219), the admin **fleet update-push** (CheckForUpdate
> hub command, `DevicesController` +85 / `Devices.tsx` +76), and the **lock-screen-removal / uninstall
> PowerShell rewrites** (`Reset`/`Diagnose`/`uninstall` RMM scripts). The rest of the workspace carries
> the prior pass's dispositions (unchanged → not re-derived, per the anchor pre-pass).
>
> **Stale-stamp correction:** the prior ledger marked `SelfUpdateService.cs` and `AgentClient.cs`
> *VERIFIED-CLEAN*. Those stamps predated the rewrite (commits a3c0d80/2cca2db landed ~7h later) and are
> **invalidated**. SelfUpdateService.cs is re-opened as **Agent-M1**; AgentClient.cs's new CheckForUpdate
> handler was re-reviewed and is clean.
>
> **Remediated 2026-06-03 (team-docpro closed-loop pass).** 6 of the 7 rows reached
> terminal verified states (5 FIXED-VERIFIED, 1 REJECTED-VERIFIED); see the **Remediation
> log** section below. The one remaining row, **XT-M1**, stays **OPEN by design** — it is
> Keith's build-mode project XT-3 (machine-SID token binding), decided on the phone
> 2026-06-02. Per the standing rule, an unfixed security finding is NEVER given a terminal
> token to zero the gauge.
> Gauge after remediation: **1 OPEN** (XT-M1 only).

## Gauge rules (v5.4.25 parser — DO NOT BREAK)

Every pipe-table row whose first cell is an ID (letter + digit) counts as OPEN **unless**
the row also contains one of these uppercase terminal tokens:
`FIXED-VERIFIED` · `REMEDIATED` · `REJECTED-VERIFIED` · `VERIFIED-CLEAN`

`DEFERRED` / `BLOCKED` / `OPEN` all **count as OPEN**. An in-code anchor is NOT a terminal
state. Only code changed + verified earns a terminal token. The "Reviewed and clean" and
"ANCHOR-CHALLENGE" sections below are prose (no pipe rows) and do not affect the count.

---

## Critical

None found.

## High

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|---|---|---|---|---|---|---|
| RMM-H1 | High | FIXED-VERIFIED | `infrastructure/rmm/uninstall-toast-agent.ps1:284-294,335` | `Start-Process msiexec -PassThru` then `$proc.WaitForExit($ms)` with **no `-Wait` and no handle cache** → `$proc.ExitCode` is `$null` on Windows PowerShell 5.1 (reproduced live on 5.1.26100.8457). `switch ($null)` always hits `default`, `$msiFailure` stays `$null` (falsy), so the final `if ($msiFailure)` is never taken. The 1605 ("not installed") and 3010 ("reboot pending") branches also never fire. | A **failed** `msiexec /x` exits 0 — the RMM records SUCCESS on a broken uninstall. On a live fleet, admins believe the agent is removed when it is not. Fix shape: cache `$null = $proc.Handle` before the wait (the `-Wait -PassThru` form tests reliable). | High |
| RMM-H2 | High | FIXED-VERIFIED | `infrastructure/rmm/install-toast-agent.ps1:332-339,343` | Same `Start-Process -PassThru` + `WaitForExit($ms)` ExitCode-null shape (found via blast-radius grep from RMM-H1). A **successful** `msiexec /i` reads `$exitCode = $null` → `if ($exitCode -eq 0 -or 3010)` is false → **skips `Write-BootstrapFallback` and `Set-LockScreenPolicy`**, then exits via the `default` branch (reports failure on a successful install). `install-toast-agent.template.ps1:114` uses `-Wait -PassThru` (tested reliable) and is **not** affected. | A successful install mis-reports as failed and skips post-install config (lock-screen pin / bootstrap fallback); admins re-run, fleet state goes inconsistent. Same one-line handle-cache fix as RMM-H1. | High |

## Medium

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|---|---|---|---|---|---|---|
| Agent-M1 | Medium | FIXED-VERIFIED | `src/ToastRevival.Agent/SelfUpdateService.cs:381-393,416-434` | `EnsureProtectedVerifiedDir` deletes a pre-existing `verified\` dir **only if it is a reparse point** (:385); a non-admin who pre-creates it as a **plain** directory survives that check and stays its **owner** (`CreateDirectory` is a no-op on an existing dir). `LockDownToSystemAndAdmins` sets the DACL but **never `SetOwner`**, so the owner keeps implicit `WRITE_DAC` and can rewrite the ACL to swap the SYSTEM-executed `apply-msi.cmd` — which, unlike the MSI, is **not** Authenticode-gated. Parent dir is user-writable by design (:374). | Local non-admin → **SYSTEM code execution**. Defeats the FIX-Agent-H1 "verify-time == use-time, race eliminated" guarantee for a pre-positioned attacker. **Challenges anchor Agent-L1 — see ANCHOR-CHALLENGE below.** | Medium |
| RMM-M1 | Medium | FIXED-VERIFIED | `infrastructure/rmm/Reset-ToastLockScreen.ps1:259-263` · `uninstall-toast-agent.ps1:229-233` | After `reg load`, the dormant hive is read through the PS registry provider (`Get-ItemProperty`/`Get-Item`/`Remove-ItemProperty` in `Remove-ToastSlotsFromHive`), which caches open key handles. Cleanup does only `[gc]::Collect()` (no `WaitForPendingFinalizers()`) before `reg unload`, and the unload result is swallowed (`> $null 2>&1`, `$LASTEXITCODE` unchecked). | The handle can still be open → `reg unload` fails silently → the swept user's `NTUSER.DAT` stays mounted under `HKU\TempToast_<sid>` and **locked until reboot**, which can block that profile's next logon. Two parallel sites (the "keep in sync" blocks). | Medium |
| RMM-M2 | Medium | REJECTED-VERIFIED | `infrastructure/rmm/Reset-ToastLockScreen.ps1:23,99,179-180` | The "never black" guarantee is **not proven** in the headless SYSTEM path. With no interactive session, Step 2 (WinRT repaint) is skipped, the Toast registry triplet is removed, cache delete is deferred, and the box is *expected* to fall back to the Windows default. The intent is sound, but on a freshly-imaged Entra/Intune box where the Toast slot is the **only** slot, default-fallback vs. black at next lock is Windows-version-dependent and not proven on a real box. | This exact feature has shipped black-screen regressions before on review-alone (team signal: paper-validation misses real Entra/cache behavior). "Never black" should be confirmed on a representative Entra box, not asserted. Not a proven defect — an open confirmation item. | Medium |
| Wix-M1 | Medium | FIXED-VERIFIED | `installer/ToastRevival.Agent.Setup.wxs:312-334,438-441` | Uninstall removes `\Toast2IT\ToastNotificationAgentLogon` (UninstallScheduledTask) and `\Toast2IT\ToastNotificationUpdater` (UninstallUpdaterTask) but there is **no action to remove the new v0.4.37 `\Toast2IT\ToastNotificationApplyUpdate` task**. A remote/MSI uninstall (`msiexec /x`, not the RMM script) leaves a registered SYSTEM task whose action is `cmd.exe /c` a path under a user-writable-parent ProgramData dir. The RMM uninstall script's `\Toast2IT\*` wildcard purge *does* catch it. | Orphaned SYSTEM scheduled task after MSI uninstall; latent surface that compounds with Agent-M1 (a SYSTEM task pointing at a cmd in a dir a non-admin could come to own). | Medium |
| XT-M1 | Medium | OPEN | `src/ToastRevival.Api/Controllers/DevicesController.cs` (carve-out, anchored) | **(Carried forward from the 2026-06-02 remediate ledger — unchanged.)** Single-use-token reinstall carve-out identifies "same machine" by the self-reported `(DeviceName, Username)` tuple — not hardware-backed. | Defense-in-depth gap. **Decision (Keith, phone 2026-06-02):** fresh-token-per-reinstall REJECTED (breaks silent RMM mass deploy); fix = bind token to machine SID, scoped as build-mode project **XT-3**. **Owner: Keith.** Stays OPEN until XT-3 ships. | Medium |

## Low

None filed. (Several nitpicks were considered and dropped per the "Reserve Low for things you'd actually fix" rule.)

---

## ANCHOR-CHALLENGE

- **Agent-L1** — `src/ToastRevival.Agent/SelfUpdateService.cs:395-398`.
  Anchor text: *"ACL set in C# here rather than in WiX is intentional. The verified\ dir is created and owned exclusively by the SYSTEM updater and is re-created + re-locked + reparse-checked every run before use, so a WiX-pre-seeded ACL would not be load-bearing. Resolved no-change; not a deviation worth re-flagging."*
  **Challenge (factual basis):** the premise *"created and owned exclusively by the SYSTEM updater"* is incomplete. `EnsureProtectedVerifiedDir` deletes a pre-existing entry **only when it is a reparse point** (:385). A non-admin who pre-creates `verified\` as an ordinary directory is **not** replaced (`CreateDirectory` no-ops on an existing dir) and **remains the owner**, and `LockDownToSystemAndAdmins` never calls `SetOwner`. So for the pre-creation case the dir is *not* SYSTEM-owned, and an owner retains implicit `WRITE_DAC`.
  The anchor's *"C# vs WiX placement"* conclusion is **not** contested — only its ownership premise. Tracked as the security finding **Agent-M1** (not filed as a separate counted row, to avoid double-counting the gauge).

---

## Remediation log (2026-06-03 — team-docpro closed-loop pass)

All six actionable rows resolved, each independently verified by a separate read-only agent;
Abish ran the 7-step Code Sweep on the full diff (verdict: **SHIP**). Gates: PS parser (UTF-8
decode) OK on all 3 scripts; `dotnet build` 0 warn/0 err; `wix build` produced a 76.89 MB MSI
(only pre-existing WIX1006 property warnings) and the linked MSI's `CustomAction` +
`InstallExecuteSequence` tables confirm `UninstallApplyTask` is present.

- **RMM-H1 — FIXED-VERIFIED.** Cached `$null = $proc.Handle` immediately after `Start-Process`,
  before `WaitForExit`, so `$proc.ExitCode` is reliable on PS 5.1. A failed `msiexec /x` now
  records failure instead of silent success. *Verifier: confirmed handle materialized before
  the exit-code switch; timeout branch unaffected.*
- **RMM-H2 — FIXED-VERIFIED.** Same `$null = $proc.Handle` fix in the install path, before
  `$exitCode = $proc.ExitCode`. A successful install no longer mis-reads `$null` and no longer
  skips `Write-BootstrapFallback` / `Set-LockScreenPolicy`. *Verifier: confirmed; `-Wait
  -PassThru` template path was already safe.*
- **RMM-M1 — FIXED-VERIFIED.** Both sync'd `reg unload` sites (`uninstall-toast-agent.ps1` +
  `Reset-ToastLockScreen.ps1`) now run `[gc]::WaitForPendingFinalizers()` before the unload,
  check `$LASTEXITCODE`, retry once after 250 ms, and WARN on persistent failure — so a dormant
  hive no longer stays mounted/locked until reboot. *Verifier: both sites present and equivalent.*
- **Agent-M1 — FIXED-VERIFIED.** `EnsureProtectedVerifiedDir` now deletes ANY pre-existing entry
  (reparse → unlink only; plain dir → `Delete(recursive:true)`), and `LockDownToSystemAndAdmins`
  now calls `sec.SetOwner(system)` before applying the DACL. A non-admin pre-creator can no longer
  retain ownership / implicit WRITE_DAC, closing the local→SYSTEM path via `apply-msi.cmd`. The
  **Agent-L1 anchor-challenge is resolved** — its "SYSTEM-owned" premise is now enforced in code.
  *Verifier: reparse branch never uses recursive:true; SetOwner runs as LocalSystem (SeRestore);
  no residual ownership path.*
- **Wix-M1 — FIXED-VERIFIED.** Added `UninstallApplyTask` custom action (`schtasks /Delete /TN
  "\Toast2IT\ToastNotificationApplyUpdate" /F`, deferred / no-impersonate / ignore) and sequenced
  it `After="UninstallUpdaterTask"` on `REMOVE="ALL"`. `msiexec /x` now removes the orphaned
  SYSTEM apply task. Task name byte-matches `ApplyTaskName` in `SelfUpdateService.cs`. *Verifier +
  built-MSI table query: present exactly once in both tables; all three `\Toast2IT\*` tasks now
  have uninstall counterparts.*
- **RMM-M2 — REJECTED-VERIFIED.** "Never black" in the headless SYSTEM path is guaranteed by
  construction: the branch removes only the Toast registry triplet and defers the cache delete
  (`DeferCacheDelete` stays `$true`), and Windows' OS-baked `%WINDIR%\Web\Screen\img100.jpg`
  displays when no per-user slot exists — so black would require BOTH no OS default AND the cache
  gone, neither of which happens headless. Pinning a SYSTEM-settable default would re-introduce the
  "managed by your organization" lock the script exists to remove, so no further headless action is
  correct. Anchored at `Reset-ToastLockScreen.ps1` Step-2 headless branch (`REVIEW-2026-06-03
  RMM-M2 REJECTED-by-design: …`). *Verifier: anchor matches the regex, ID = RMM-M2, assumption is
  named and true against the surrounding code.*

**BLOCKED (Keith-owned, stays OPEN):**
- **XT-M1 — OPEN / BLOCKED.** Machine-SID token binding. Decided by Keith on the phone 2026-06-02:
  fresh-token-per-reinstall rejected (breaks silent RMM mass deploy); the fix is build-mode project
  **XT-3**, owned by Keith. Nothing for the team to escalate — the decision is already made — and
  nothing to fix here. Left OPEN with explicit owner per the no-fake-closure rule. **Note:** the
  Agent-M1 and Wix-M1 fixes ship to the fleet only via a signed **v0.4.38** MSI build; the RMM
  PowerShell fixes ship as downloadable scripts immediately.

---

## Reviewed and clean (off gauge — prose, not counted)

These surfaces in the post-ledger diff were reviewed (parallel agent + Carl re-verified) and found clean.

- **Fleet update-push, server — `DevicesController.cs:527-602`** — both endpoints `[Authorize]` + `IsAdmin()` (which OR-includes `platformAdmin`, :628, so platform-admins are additive not excluded); explicit tenant predicates (`device.TenantId != tenantId → NotFound`; `.Where(d => d.TenantId == tenantId && Status != Decommissioned)`); hub connections resolved **only** from the tenant-scoped device list (no cross-tenant push); rate-limited (`tenant-per-minute`); audit-logged. VERIFIED-CLEAN this pass.
- **Fleet update-push, dashboard — `Devices.tsx` / `devices.ts`** — catch blocks use the repo-standard `err instanceof ApiError ? err.message` (the `extractDetail()` house rule does **not** apply — that helper does not exist in this dashboard; `ApiError` already carries the server `detail`). TS response types `{pushed}` / `{pushed,total}` match the controller DTOs exactly. Only real classes/vars used (`btn-secondary`, `btn-ghost`, `spinner`, `--accent`); no `btn-danger-ghost`/`form-*`/`--accent-primary`. No onClick ReferenceError risk. No `ToastRevival` codename leak. VERIFIED-CLEAN this pass.
- **Version consistency** — `appsettings.json` `Agent:LatestVersion` `0.4.37` == csproj `0.4.37` == `Package.appxmanifest` `0.4.37`; no conflicting hardcoded default. VERIFIED-CLEAN.
- **AgentClient.cs CheckForUpdate handler + slim tray (`Program.cs`/`TrayIconService.cs`)** — `_hub.On("CheckForUpdate", …)` wired to `ForceCheckAsync`; removed menu handlers (`SendTestRequested`, `OpenDashboard`) fully removed with no dangling references; `_lastCatchupSince` is nullable + omitted-on-first-call. VERIFIED-CLEAN.
- **Diagnose-ToastLockScreen.ps1** — genuinely read-only (no Set/New/Remove/reg-add/takeown/icacls). VERIFIED-CLEAN.

---

## Top fixes (do these first)

1. **RMM-H1** — uninstall reports success on a failed removal across the fleet. Highest operational blast radius; one-line handle-cache fix.
2. **RMM-H2** — same root cause in the install script; a successful install mis-reports and skips post-install config. Fix in the same pass as RMM-H1 (blast-radius pair).
3. **Agent-M1** — close the local→SYSTEM owner-rights gap (`SetOwner(system)` in `LockDownToSystemAndAdmins`, or pre-create+ACL the dir in WiX). Security; also resolves the Agent-L1 anchor challenge.
4. **Wix-M1** — add an `UninstallApplyTask` custom action so MSI uninstall removes the orphaned `ToastNotificationApplyUpdate` SYSTEM task.
5. **RMM-M2** + **RMM-M1** — verify "never black" on a real Entra/Intune box (not on paper), and harden the `reg unload` (`WaitForPendingFinalizers` + checked retry) in the same script while there.

---

*Lifecycle: COLD REVIEW pass. All new rows OPEN; fix team drives terminal states (FIXED-VERIFIED / REMEDIATED / REJECTED-VERIFIED). Note the agent-side fixes (Agent-M1, Wix-M1) require a signed v0.4.38 MSI build to reach the fleet; the RMM PowerShell fixes (RMM-H1/H2/M1) ship as downloadable scripts. Gauge reads this file — don't alter the table format.*
