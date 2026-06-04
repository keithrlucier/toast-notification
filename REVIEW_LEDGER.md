# REVIEW LEDGER — Cold Code Review (2026-06-03)

```
Read REVIEW_LEDGER.md / latest review_history?   Yes (prior ledger REVIEW_LEDGER_2026-06-03_1.md + REVIEW_LEDGER_2026-06-02_1.md + REVIEW_LEDGER_2026-06-02_2.md read; all prior items verified)
Closed-pass anchors honored?                       Yes (REVIEW Agent-L1 updated in code; RMM-M2 REJECTED-by-design anchor in Reset-ToastLockScreen.ps1; XT-M1 carve-out anchor in DevicesController.cs; BLK-1 NormalizeForMatch anchor in BlocklistService.cs)
Files scanned:                                     ~95 (full workspace sweep: 4 RMM PS1 scripts; WiX installer; Agent C# src (SelfUpdateService, AgentClient, UpdateService, DesktopOverlayService, TenantLogoStore); API Controllers (Devices, Blocklist, Notifications, Moderation, Templates, Audit, Health); API Services (BlocklistService, NotificationQueueService, NotificationPayloadBuilder, BillingPlanRules, HttpContextTenantProvider); AppDbContext; Dashboard TSX (Devices, EnrollmentTokens, Compose, Auth, ProtectedRoute))
Files with anchors found and respected:            6 (SelfUpdateService.cs, DevicesController.cs, BlocklistService.cs, Reset-ToastLockScreen.ps1, AuthContext.tsx, install-toast-agent.template.ps1)
```

> **Cold pass — 2026-06-03 (Carl + 4 parallel Explore agents).** Scope: full workspace sweep
> (not bounded to recent commits). Prior ledger archived to
> `Docs/review_history/REVIEW_LEDGER_2026-06-03_1.md`. Prior closed rows (FIXED-VERIFIED /
> REJECTED-VERIFIED) not re-derived. XT-M1 carried forward (OPEN by design, Keith's product
> decision, unchanged since 2026-06-02).
>
> **False-positive triage note:** 6 "Critical IDOR" candidates surfaced by one agent across
> `BlocklistController.List()`, `NotificationsController.History()`, `ModerationController.GetPending()`,
> `ResolveTargetDeviceIds()` (All + Device paths), `NotificationDeliveries` fetch, and
> `BlocklistService.CheckAsync()`. Personally verified against `AppDbContext` — **all six are
> false positives**. Every cited entity has an EF global query filter keyed to
> `_tenantProvider.TenantId` (lines 53, 79, 88, 101, 117, 152, 165, 182 in `AppDbContext.cs`);
> `HttpContextTenantProvider` reads the JWT `tenantId` claim in-request, returning null outside a
> request context (which makes filters produce `WHERE tenant_id IS NULL` — zero rows). Background
> processing in `NotificationQueueService` correctly uses `IgnoreQueryFilters()` throughout. No
> IDOR exists.

## Gauge rules (v5.4.25 parser — DO NOT BREAK)

Every pipe-table row whose first cell is an ID (letter + digit) counts as OPEN **unless**
the row also contains one of these uppercase terminal tokens:
`FIXED-VERIFIED` · `REMEDIATED` · `REJECTED-VERIFIED` · `VERIFIED-CLEAN`

`DEFERRED` / `BLOCKED` / `OPEN` all **count as OPEN**. An in-code anchor is NOT a terminal
state. Only code changed + verified earns a terminal token. Prose sections (ANCHOR-CHALLENGE,
"Reviewed and clean") use no pipe rows and do not affect the count.

---

## Critical

None found.

## High

None found.

## Medium

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|---|---|---|---|---|---|---|
| RMM-M3 | Medium | FIXED-VERIFIED | `infrastructure/rmm/install-toast-agent.template.ps1:81-90` | The Authenticode signature check is explicitly labelled "Optional, informational" in a comment and is WARN-only: an invalid or missing signature (`$sig.Status -ne 'Valid'`) logs a warning and **does not halt installation**. The main `install-toast-agent.ps1` exits with code 3 on a bad signature; the template makes no such guarantee. | **FIXED:** Comment updated, WARN→ERROR log, `Remove-Item` + `exit 3` added in both the failure branch and the catch block — now matches main script enforcement. PS1 parse gate: OK. Abish sweep: SHIP. | High |
| XT-M1 | Medium | OPEN | `src/ToastRevival.Api/Controllers/DevicesController.cs` (carve-out, anchored) | **(Carried forward — unchanged since 2026-06-02.)** Single-use-token reinstall carve-out identifies "same machine" by the self-reported `(DeviceName, Username)` tuple — not hardware-backed. A compromised endpoint with a spent HKLM token value and knowledge of its own name/username could re-enroll a second device. | Defense-in-depth gap (HKLM read already implies machine compromise). **Decision (Keith, phone 2026-06-02):** fresh-token-per-reinstall REJECTED (breaks silent RMM mass deploy); fix = bind token to machine SID, scoped as build-mode project **XT-3**. **Owner: Keith.** Stays OPEN until XT-3 ships. | Medium |

## Low

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|---|---|---|---|---|---|---|
| Agent-L2 | Low | FIXED-VERIFIED | `src/ToastRevival.Agent/AgentClient.cs:42,735` | `HttpResponseMessage` objects returned by `PostAsJsonAsync` at lines 42 and 735 are not wrapped in `using var`. Every other HTTP call in the file uses `using var resp = ...` (lines 91, 137, 484, 666, 692). After `ReadAsStringAsync` / `ReadFromJsonAsync` drains the body, the socket is effectively returned, but deterministic disposal of the message object (headers, content buffers) is skipped and deferred to GC. | **FIXED:** `using var` added at both sites (line 42 `response`, line 735 `resp`). C# build: succeeded, 0 warnings. Abish sweep: SHIP. | High |

---

## ANCHOR-CHALLENGE

None this pass.

---

## Reviewed and clean (off gauge — prose, not counted)

Surfaces personally verified this pass (in addition to the false-positive triage above):

- **EF global query filter coverage** — `AppDbContext.cs` confirmed: `AppUser` (line 43), `Device` (53), `DeviceGroup` (62), `DeviceGroupMember` (79), `Notification` (88/101), `NotificationDelivery` (117), `AuditLog` (126), `TenantBlocklistEntry` (152), `TenantApiKey` (165), `EnrollmentToken` (182) all have `HasQueryFilter` keyed to `_tenantProvider.TenantId`. VERIFIED-CLEAN.
- **ModerationController** — `GetPending()` relies on the Notifications global filter (confirmed); `Approve()` and `Reject()` each independently fetch with an explicit `n.TenantId == tenantId` predicate before mutation. Audit-logged. VERIFIED-CLEAN.
- **BlocklistService.NormalizeForMatch** — `BLK-1` anchor in code documents the NFKC + format-char-strip approach and its known limitation (cross-script homoglyphs deferred to Azure Content Safety). Sound and intentional. VERIFIED-CLEAN.
- **NotificationQueueService background path** — orphan recovery (`RecoverOrphansAsync`) and scheduled backfill (`EnqueueDueScheduledAsync`) correctly use `IgnoreQueryFilters()` with a fresh DI scope. `ProcessAsync` also uses `IgnoreQueryFilters()`. No tenant-filter bypass risk. VERIFIED-CLEAN.
- **SelfUpdateService.cs post-Agent-M1 fix** — `EnsureProtectedVerifiedDir` deletes any pre-existing entry (reparse-point: unlink only; plain dir: `Delete(recursive:true)`); `LockDownToSystemAndAdmins` calls `sec.SetOwner(system)` before setting ACEs. The Agent-L1 anchor's "SYSTEM-owned" premise is now enforced in code. VERIFIED-CLEAN.
- **WiX installer post-Wix-M1 fix** — `UninstallApplyTask` custom action present and sequenced `After="UninstallUpdaterTask"` on `REMOVE="ALL"`. All three `\Toast2IT\*` tasks have uninstall counterparts. VERIFIED-CLEAN.
- **Dashboard XSS surface** — no `dangerouslySetInnerHTML`, `innerHTML`, or `eval` anywhere. `AuthContext.tsx` localStorage anchor (`REVIEW-2026-05-25 INFO-01 REJECTED-by-design`) confirmed as intentional pending a dedicated migration milestone. CSS custom properties verified against `index.css` — no undefined vars. VERIFIED-CLEAN.
- **Version consistency (v0.4.38)** — `appsettings.json` `Agent:LatestVersion` `0.4.38` == `ToastRevival.Agent.csproj` `0.4.38` == `Package.appxmanifest` `0.4.38`. VERIFIED-CLEAN.
- **install-toast-agent.ps1 (main script)** — Authenticode enforcement exits code 3 on bad signature (lines 272-287); `$null = $proc.Handle` materialized before `WaitForExit` (RMM-H2 fix confirmed present). VERIFIED-CLEAN.
- **RMM scripts (all four)** — `[gc]::WaitForPendingFinalizers()` + checked `reg unload` retry present in both sites (RMM-M1 fix); `RMM-M2 REJECTED-by-design` anchor present in `Reset-ToastLockScreen.ps1` headless branch. VERIFIED-CLEAN (except template Authenticode advisory-only, filed as RMM-M3).

---

## Top fixes (do these first)

1. **RMM-M3** — harden `install-toast-agent.template.ps1` Authenticode check from WARN to EXIT-on-failure, matching the main script's `exit 3` behavior. One condition + `exit 3` line. Low effort, meaningful supply-chain protection for any customer using the template as-is.
2. **XT-M1** — machine-SID token binding (build-mode project XT-3, owner: Keith). Cannot be fixed without Keith's input.
3. **Agent-L2** — `using var response = ...` at lines 42 and 735 in `AgentClient.cs`. One-liner; brings both sites in line with the rest of the file.

---

*Lifecycle: COLD REVIEW pass. All new rows start OPEN; fix team drives terminal states (FIXED-VERIFIED / REMEDIATED / REJECTED-VERIFIED). Gauge reads the pipe-table rows — do not alter the format.*
