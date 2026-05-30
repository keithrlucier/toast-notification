# REVIEW LEDGER — Cold Code Review (2026-05-30)

**Pass date:** 2026-05-30
**Reviewer:** Carl (cold reviewer — 4 parallel read-only review agents dispatched across Agent self-update, Backend API, Installer/RMM, and Frontend surfaces; all Critical/High claims personally verified against source by Carl before entry).
**Scope:** Net-new code since prior ledger closed clean at `6b041f5` (0.4.19, 2026-05-29). Diff `6b041f5..HEAD (ac322a4)` — M15 MSI self-update + remote uninstall (0.4.28), installer survival fixes (0.4.29–0.4.31), registry-native bootstrap (0.4.33), name-driven multi-channel RMM removal, downloadable clean-removal script. 30 files changed, +2,190 / −189.
**Prior ledger archived to:** `docs/review_history/REVIEW_LEDGER_2026-05-30_1.md`

Read REVIEW_LEDGER.md / latest review_history? Yes
Closed-pass anchors honored? Yes — Agent-M1 (HTTPS guard) confirmed PRESENT at `Program.cs:330-337/460-466/685-691` and verified to also cover the new registry-bootstrap path (not bypassed, not re-flagged). Frontend-L1/L2 (prior pass) are terminal and out of this scope. The prior 4 DevicesController IDOR rejections were re-confirmed correct and not re-opened.
Files scanned: 30 (full diff) + 8 adjacent files pulled for claim tracing (NotificationHub, TokenService, AppDbContext, HttpContextTenantProvider, UpdateService, Package.appxmanifest, DeviceDtos, index.css)
Files with anchors found and respected: 1 (Agent-M1 HTTPS guard in `Program.cs`)

---

## Summary

| Severity | Count | Open | Fixed | Rejected |
|----------|-------|------|-------|----------|
| Critical | 1     | 0    | 1     | 0        |
| High     | 1     | 0    | 1     | 0        |
| Medium   | 1     | 0    | 1     | 0        |
| Low      | 4     | 0    | 4     | 0        |
| ANCHOR-CHALLENGE | 0 | 0 | 0 | 0 |
| **Total**| **7** | **0** | **7** | **0** |

---

## Critical

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|
| Agent-C1 | Critical | FIXED-VERIFIED | `src/ToastRevival.Agent/SelfUpdateService.cs:199-264` | The SYSTEM-level `RunUpdaterMode` read `pending-action.txt` and ran `msiexec` with no Authenticode re-verification in SYSTEM context; the only signature check ran in the user-context download process. Uninstall trigger accepted arbitrary path strings. | Local privilege escalation to SYSTEM via TOCTOU swap of staged MSI or path injection in uninstall trigger. | High |

**Disposition:** FIXED. `RunUpdaterMode` switch arms now call `ExecuteVerifiedMsiUpdate` and `ExecuteVerifiedMsiUninstall`. `ExecuteVerifiedMsiUpdate` re-runs `IsSignedByToast2IT` (X509 + WinVerifyTrust) in SYSTEM context on the exact path immediately before msiexec — closes TOCTOU. `ExecuteVerifiedMsiUninstall` validates the trigger arg matches `^\{[0-9A-Fa-f]{8}-...\}$` — closes path injection. Note: directory ACL hardening (belt-and-suspenders) requires a WiX installer change to set `DirectorySecurity` at installation time; deferred to next WiX pass since the SYSTEM-side re-verification provides the security guarantee. Verified: build clean 0W/0E; Code Sweep 5-perspective PASS.

---

## High

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|
| Agent-H1 | High | FIXED-VERIFIED | `src/ToastRevival.Agent/AgentClient.cs:262-271` + `SelfUpdateService.cs:125-152` | Remote uninstall fired `WriteTrigger`/`FireUpdaterTask` after an awaited best-effort `LockScreenService.RevertAsync` — process exit could occur before the trigger was written. | Remote uninstall silently no-ops; device stays enrolled despite admin removing it. | Medium |

**Disposition:** FIXED. `RequestUninstallAsync` reordered: `ReadInstalledProductCode` → `WriteTrigger` → `FireUpdaterTask` → `DiagLog` — all synchronous/non-awaited — then `LockScreenService.RevertAsync` as best-effort. The must-succeed action now commits before any await that could be interrupted by process exit. Verified: Code Sweep PASS; logic reviewed against AgentClient.cs shutdown sequence — `OnUninstallRequested`/`_shutdown.Cancel()` are called from outside the Task.Run, unaffected.

---

## Medium

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|
| Agent-M2 | Medium | FIXED-VERIFIED | `src/ToastRevival.Agent/SelfUpdateService.cs:292-302` | `DownloadAndVerifyMsiAsync` accepted `http://` download URLs; `MsiDownloadUrl` is a server-supplied field separate from the HTTPS-guarded `config.ServerUrl`. | Contradicts Agent-M1 HTTPS posture; weakens defense-in-depth on the binary fetch. | High |

**Disposition:** FIXED. `DownloadAndVerifyMsiAsync` now rejects non-`https` URLs in `#if !DEBUG` builds, matching the Agent-M1 pattern in `Program.cs`. DEBUG builds retain `http` for localhost testing. Log message updated to note `(https required)`. Verified: build clean 0W/0E.

---

## Low

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|
| Agent-L1 | Low | FIXED-VERIFIED | `src/ToastRevival.Agent/SelfUpdateService.cs:55-70` | `RunMsiUpdateLoopAsync` constructed `new Velopack.UpdateManager(string.Empty)` before any try/catch; a ctor/`IsInstalled` throw faulted the unobserved `Task.Run` silently, killing the MSI self-update channel for the session. | MSI/RMM-deployed agents could stop self-updating with no DiagLog line. | Medium |
| Rmm-L1 | Low | FIXED-VERIFIED | `infrastructure/rmm/uninstall-toast-agent.ps1:108` | ARP removal matched `DisplayName -like 'Toast Notification*'` with no Publisher cross-check. | Third-party product with a matching display name would be removed; `Publisher -like '*Toast2IT*'` guard is cheap belt-and-suspenders. | High |
| Rmm-L2 | Low | FIXED-VERIFIED | `infrastructure/rmm/uninstall-toast-agent.ps1` (Store/MSIX path, step 5b) | On a Store/MSIX-only endpoint, `\Toast2IT\*` scheduled tasks and the `HKLM:\SOFTWARE\Toast2IT` bootstrap key were not cleaned up — those are cleaned only by MSI custom actions. | Incomplete "do no harm" reversal on the Store channel — orphaned tasks and registry bootstrap left behind. | High |
| Frontend-L1 | Low | FIXED-VERIFIED | `src/ToastRevival.Dashboard/src/components/RemoveAgentModal.tsx:92` | Inline style referenced `var(--border, #D7DEE8)` — `--border` does not exist (project token is `--border-subtle`); fallback value also one digit off. | Off-convention token reference and subtly inconsistent border color. | High |

**Dispositions:**
- **Agent-L1:** FIXED. Velopack detection moved into a dedicated try/catch; exceptions log to DiagLog and assume MSI-deployed (safe: `CheckAndTriggerAsync` exits early if version is current).
- **Rmm-L1:** FIXED. `Where-Object` filter now includes `-and $_.Publisher -like '*Toast2IT*'` — belt-and-suspenders against false ARP name matches.
- **Rmm-L2:** FIXED. Added step 5c after MSIX block: `Get-ScheduledTask -TaskPath '\Toast2IT\*' | Unregister-ScheduledTask -Confirm:$false` and `Remove-Item 'HKLM:\SOFTWARE\Toast2IT' -Recurse -Force` (guarded by `Test-Path`). Idempotent, best-effort, consistent with rest of script.
- **Frontend-L1:** FIXED. Inline style changed to `var(--border-subtle)` — matches actual project token in `index.css`.

---

## ANCHOR-CHALLENGE

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|

None found.

---

## Notes on what was reviewed and NOT flagged (verified clean)

- **Agent-M1 HTTPS guard (prior pass, FIXED) — confirmed present and complete.** `#if !DEBUG` HTTP rejection exists at `Program.cs:330-337` (SetupMode), `:460-466` (PrimaryMode stored config), `:685-691` (TryFirstRunRegistrationAsync). The registry-native bootstrap (`DeviceConfig.cs:161-184`) flows through `TryFirstRunRegistrationAsync` then the PrimaryMode guard on every launch — the registry path does NOT bypass the guard. Not re-flagged.
- **Remote uninstall authz (`DevicesController.cs:347-386`) — SAFE.** `[Authorize]` + `IsAdmin()` (role ∈ {Admin, SuperAdmin} or platformAdmin) + `if (device.TenantId != tenantId) return NotFound()` where `tenantId` is the signed JWT claim. `FindAsync(id)` bypasses the query filter but the explicit TenantId guard is the compensating control — same hardened pattern as `Decommission`. Device JWTs (`TokenService.cs:38-50`) carry no `role`/`platformAdmin`, so a stolen agent token cannot reach the uninstall command. No IDOR.
- **Clean-removal script endpoint (`DevicesController.cs:306-333`) — SAFE.** Filename is a hardcoded literal; `root` is config-sourced; no request input reaches `Path.Combine` (no path traversal). Returns metadata only (`url`, `lastModifiedUtc`, `sizeBytes`) — never streams the body, never interpolates request data into a script. `.ps1` is fully static and server-owned. `[AllowAnonymous]` exposes only a public download URL + mtime/size, no secret.
- **Prior 4 DevicesController IDOR rejections — re-confirmed.** `Get()` uses the global EF query filter; `Ping()`/`GetTenantName()`/`GetAppearanceConfig()` use `IgnoreQueryFilters()` but read ids from the signed device JWT. Honored, not re-opened.
- **RMM MSIX removal (Rmm-M3 candidate) — RESOLVED, not a bug.** `Package.appxmanifest` `Identity Name="FileUnityCloud.ToastNotification"` (line 12) contains the substring matched by `$AppxLike = '*ToastNotification*'` (script line 101). Store-channel removal matches correctly.
- **WiX installer standing rules — all hold.** KillAgent condition `REMOVE="ALL" OR Installed` present (`Setup.wxs:428`) — fires on upgrades, the production-verified fix. `MajorUpgrade Schedule="afterInstallFinalize"` correct (0.4.29). No `VersionNT64` launch condition (intentional per documented compat-shim behavior); runtime floor `IsWindowsVersionAtLeast(10,0,19041)` (`Program.cs:57`) agrees exactly with manifest `MinVersion="10.0.19041.0"` — no gate divergence.
- **Banned codename — clean.** No user-visible "ToastRevival": WiX Package/install dir/Start Menu/shortcut all "Toast Notification"; RMM script user-facing strings and the served `.ps1` say "Toast Notification"; new API responses and the remove modal say "Toast Notification". "ToastRevival" remains only in internal namespaces / `Jwt:Issuer`/`Audience` / source paths (not user-visible).
- **Frontend remove-agent modal — clean.** Backdrop dismiss uses the SAFE guarded pattern (mousedown-target ref + `e.target === e.currentTarget`), and the backdrop only calls `onClose`, never the destructive `onRemoteUninstall`. `UninstallScriptInfo` TS type matches the controller's anonymous return (camelCase default). No `btn-danger-ghost`/`form-label`/`form-input`/`form-select`/`--accent-primary`. No emojis. Download via plain `<a download>` (not a blob); `formatDate` guards null + NaN.
- **Toast activation — clean.** 5s `_activationCache` keyed on full argument string collapses legacy + WinAppSDK double-fire to one; distinct buttons carry distinct args so they don't collide; the double-encoding fix (`ToastTemplates.cs:373`) pairs correctly with a single `UnescapeDataString` on read.
- **Registry bootstrap parsing (`DeviceConfig.cs:161-184`)** — correct hive (HKLM), null-safe (`key is null`, `Guid.TryParse`, `IsNullOrWhiteSpace`), exceptions caught. camelCase bootstrap.json fallback consistent with `DeviceConfig` JsonPropertyName + case-insensitive loader. No secret values written to DiagLog.
- **Resource/async** — HttpClient/HttpResponseMessage/FileStream in `using`/`await using`; RegistryKey/Mutex disposed; WinVerifyTrust AllocHGlobal freed in `finally`. `_lastCatchupSince` is nullable + omit-on-first-call (standing rule honored). No unsafe `nullable?.ToString()[..n]` slices.

### Out-of-scope awareness (not flagged this pass — pre-existing or accepted-by-design)
- **`Devices.tsx:692` `DeviceGroupModal`** uses the naive `onClick={onClose}` backdrop pattern (loses unsaved edits on a stray drag-release). Pre-existing, outside this diff, non-destructive. The new `RemoveAgentModal` correctly does NOT copy it. Candidate for a future cleanup pass.
- **Enrollment key at rest** — `install-toast-agent.ps1` writes `bootstrap.json` (and HKLM) with the enrollment key in cleartext under world-readable `%ProgramFiles%`/HKLM SOFTWARE. Inherent to the per-machine bootstrap model; the key is a one-time registration gate forwarded once and discarded. Accepted risk unless Keith wants it elevated.
- **`uninstall-toast-agent.ps1` revert task 8s fixed sleep + msiexec timeout last-writer exit code** — minor best-effort/cosmetic robustness items; non-fatal.
- **Agent-C1 directory ACL hardening** — The SYSTEM-side re-verification closes the TOCTOU; full `%ProgramData%\Toast2IT\Toast Notification` ACL lockdown (Admins+SYSTEM only) would break the user-context write path. Deferred to a WiX installer change where the directory ACL can be set at install time before the user-context agent first runs.
