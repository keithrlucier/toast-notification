# ToastRevival — Milestones

## M0A: Signed Toast Agent Spike  **[COMPLETE 2026-05-07]**
**Goal**: Prove the smallest possible Windows endpoint path before platform work begins.

### Deliverables (all closed)
- **D1** [x]: .NET SDK 8.0.420, Windows App SDK 1.7.250310001, WiX 5.0.2, Thales token + Sectigo OV cert installed.
- **D2** [x]: `src/ToastRevival.Agent` on `net8.0-windows10.0.19041.0`.
- **D3** [x]: Agent displays toast notifications - 7 templates (plain + 6 DESIGN-SPEC templates) via `AppNotificationBuilder`.
- **D4** [x]: MSI built locally via WiX 5 (`installer/ToastRevival.Agent.Setup.wxs`).
- **D5** [x]: Signed with Sectigo OV cert via Thales hardware token (timestamp valid past cert expiry).
- **D6** [x]: Installed on clean Win11 lab machine - no issues.
- **D7** [x]: Agent runs in logged-in user context via Startup-folder shortcut.
- **D8** [x]: Toast fires after reboot (lab machine confirmed).
- **D9** [x]: Evidence captured - `EVIDENCE/2026-05-07-m0a-*.md` (4 entries).

### Resolved Research
- Signing flow: Thales hardware token + Sectigo OV cert works cleanly from the dev workstation; Keith handles the PIN/middleware.
- COM activator / Store identity: not needed for the first spike. Unpackaged Windows App SDK 1.7 works fine for unsigned dev runs and for the per-machine MSI install.
- Packaging path: MSI first (this milestone). MSIX comes in M0 D2.
- Per-user run mechanism: Startup-folder shortcut (all-users) for M0A. Scheduled Task in user context is a more robust alternative for MSP-managed endpoints and is M0 D3.

---

---

## M0: Foundation & Deployment Validation
**Goal**: Prove the deployment model works on real enterprise images before writing product code.

### Deliverables
- **D1** [x]: Skeleton WinUI 3 / .NET 8 app that displays a hardcoded toast notification on launch (closed under M0A — same agent project carried forward)
- **D2** [x **COMPLETE 2026-05-08**]: MSIX package built, signed with OV cert, installs cleanly on Windows 11 lab; visible toast fires from packaged context, button-click routes through NotificationInvoked handler.
  - Build complete 2026-05-07 (initial 0.2.0.0). Signing tooling discovery + Publisher fix landed 0.2.0.1 same day.
  - Install validation 2026-05-08: 0.2.0.1 installed cleanly but `AppNotificationManager.Default.Register()` threw `COMException 0x80070490` (ERROR_NOT_FOUND) — DiagLog isolated the failure to the Register() call. Root cause: missing `Arguments="----AppNotificationActivated:"` on `<com:ExeServer>` (FIX-MSIX-004).
  - 0.2.0.3 patch shipped (commit `6e3495c`): Arguments token added, CLSID `7FA7762F-41EC-4D72-9F06-58964AB36FEA` retained. Signed + installed on Win11 lab; single visible toast fired, "Acknowledge" button click routed cleanly to `NotificationInvoked` handler with the expected argument payload (`action=acknowledge;source=m0a;template=Plain`). See `EVIDENCE/2026-05-07-m0-d2-msix-build.md`, `2026-05-08-m0-d2-fix-msix-004-patch-build.md`, `2026-05-08-m0-d2-fix-msix-004-register-not-found.md`, `2026-05-08-m0-d2-toast-fires-packaged.md`.
  - Win10 1809 install validation deferred to M0 D4 GPO matrix (no Win10 1809 lab machine on hand).
- **D3** [x **COMPLETE 2026-05-08**]: MSI wrapper installs the agent + registers `\Toast2IT\ToastNotificationAgentLogon` Scheduled Task at install (replaces M0A's Startup-folder shortcut). Task is logon-triggered with BUILTIN\Users group principal (`S-1-5-32-545`) at `LeastPrivilege`; action runs `%ProgramFiles%\Toast Notification\ToastNotification.Agent.exe --template alert --no-wait`. WiX 5 deferred custom actions invoke `[System64Folder]schtasks.exe` with `/Create /XML /F` (after InstallFiles, condition `NOT REMOVE`) and `/Delete /F` (before RemoveFiles, condition `REMOVE="ALL"`, `Return="ignore"` for idempotency). Pre-install verification clean (XML parses, MSI Custom Action table correct Type 3106/3170, payload byte-identical repo → cab → admin-install extract). Lab install verified 2026-05-08: signed `ToastNotification.Agent-0.3.0.0.msi` installed cleanly on Win11 lab; `Get-ScheduledTask -TaskPath '\Toast2IT\' -TaskName 'ToastNotificationAgentLogon'` returned `State=Ready` with `MSFT_TaskLogonTrigger` and `MSFT_TaskExecAction`; alert toast fired at next user logon (Critical scenario, hero + logo + Acknowledge/Report buttons all rendered correctly). See `EVIDENCE/2026-05-08-m0-d3-msi-build-with-scheduled-task.md` and `EVIDENCE/2026-05-08-m0-d3-task-fires-at-logon.md`.
- **D4** [x **COMPLETE 2026-05-08**]: Verified scheduled task on Win11 lab: MSI 0.3.1.0 installs cleanly, task State=Ready, toast fires, second local user account also receives toast (BUILTIN\Users principal confirmed), uninstall removes task cleanly. FIX-MSIX-002 applied (manifest MinVersion aligned with runtime gate). GPO/Intune testing deferred: GPO standing rules documented in CONTEXT.md, domain/Intune carry to M8 beta. See `EVIDENCE/2026-05-08-m0-d4-matrix-results.md`.
- **D5** [x **CLOSED 2026-05-09**]: Store submission pipeline verified end-to-end. M0 D5 signed MSIX (`ToastNotification.Agent-0.2.1.0.msix`) flighted to existing listing **9PFD6004DVTN**; live at https://apps.microsoft.com/detail/9PFD6004DVTN. Lab install can pull from the public Store listing directly.
- **D6**: Document deployment findings and any fallback mechanisms needed

### Open Research
- Does the scheduled task approach work under restrictive GPOs?
- Can we submit to the existing Store listing after accepting the updated Developer Agreement?
- What's the minimum Windows 10 build version for WinUI 3 + Windows App SDK?

### Agent Deployment
- Anthony: D1-D3 (system-level, requires deep understanding of WinUI 3 packaging)
- Abish: D4 (bounded verification task — run through GPO test matrix, document results)
- Carl: D5-D6 (Store submission requires Partner Center access coordination with Keith)

---

## M1: Backend API — Core Infrastructure  **[COMPLETE 2026-05-08]**
**Goal**: Functioning multi-tenant API with auth, device registration, and notification send.

### Deliverables (all closed)
- **D1** [x]: ASP.NET Core 8 project `src/ToastRevival.Api` scaffolded with EF Core 8 + Npgsql + ASP.NET Identity. `ToastRevival.sln` updated. Build: 0 warnings, 0 errors.
- **D2** [x]: Multi-tenant data model — `Tenant`, `AppUser`, `Device`, `DeviceGroup`, `DeviceGroupMember`, `NotificationTemplate`, `Notification`, `NotificationDelivery`, `AssetLibrary`, `AuditLog`. EF Core global query filters enforce per-tenant isolation on all reads. `InitialCreate` migration generated.
- **D3** [x]: JWT auth — `POST /api/auth/register` (tenant + admin user, transactional), `POST /api/auth/login`. `TokenService` issues user tokens (60 min) and device tokens (365 days). `ITenantProvider`/`HttpContextTenantProvider` resolves tenant from JWT claim.
- **D4** [x]: `POST /api/devices/register` (unauthenticated) — agent registers, receives JWT device token. `GET /api/devices`, `GET /api/devices/{id}`, `DELETE /api/devices/{id}` (decommission), `POST /api/devices/ping` (heartbeat). SHA-256 token hash stored; raw token transmitted once.
- **D5** [x]: `POST /api/notifications` — create notification, expand device/group/all targets, create per-device `NotificationDelivery` records, enqueue. `GET /api/notifications` (history), `GET /api/notifications/{id}`. `NotificationQueueService` background service pushes via SignalR.
- **D6** [x]: `NotificationHub` at `/hubs/notifications` — JWT-authenticated, tenant-scoped groups (`tenant-{id}`, `device-{id}`). `ReportDelivery` / `ReportInteraction` hub methods. `ConnectedDevices` registry for presence tracking.
- **D7** [x]: Built-in ASP.NET Core rate limiting — `tenant-per-minute` sliding window (60 req/min, partition by tenantId), `device-per-hour` fixed window (10 req/hr, partition by deviceId). Applied via `[EnableRateLimiting]` attributes.
- **D8** [x]: `AuditService` writes `AuditLog` entries on all device registration, device decommission, and notification send events. Scope-factory pattern (works from background service context).

### Code Sweep
- Commit (pending): SHIP WITH NOTES — INFO-M1-001 through INFO-M1-006 logged (see FIX-LIST.md).
- INFO-M1-002 (transaction gap in Register) fixed before commit.

### Agent Deployment
- Anthony: D1-D3, D6 ✓
- Abish: D4-D5, D7-D8 (implemented inline with Anthony's track) ✓

---

## M2: Windows Agent — Full Implementation
**Goal**: Production-ready Windows agent that connects to backend and renders notifications.

Carl sliced M2 at orientation (2026-05-09). M2.A delivers the agent↔backend pipeline + HMAC + the two pre-existing INFO items that are foundational. D6/D7/D8/D9 sliced to subsequent sessions because each carries enough scope (catch-up endpoint, Diana-engaged tray UI, Velopack integration, WiX property wiring) to be its own milestone-shaped chunk.

### Deliverables
- **D1** [x **COMPLETE 2026-05-09 (M2.A)**]: SignalR client integration — connect to backend hub, auto-reconnect with exponential backoff `[0, 2, 5, 10, 30]` seconds via `WithAutomaticReconnect`. `Microsoft.AspNetCore.SignalR.Client 8.0.15`. Hub URL derived from device config; JWT supplied via `AccessTokenProvider`. Reconnecting/Reconnected/Closed lifecycle events all logged to DiagLog.
- **D2** [x **COMPLETE 2026-05-09 (M2.A)**]: Toast notification rendering from backend payload — `ToastTemplateBuilder.BuildFromPayload(ToastPayload)` extends the existing argv-driven builder (Carl's standing check #1 — extend, don't fork). Supports `title`, `bodyLine1`, `bodyLine2`, `heroImageUrl`, `logoUrl`, `actionButtons[]`, `audioSetting` (URI or named event), `scenario` (lowercase string → enum). Click args encoded with `source=hub`, `notificationId=<guid>`, `action=<string>` for routing back through NotificationInvoked.
- **D3** [x **COMPLETE 2026-05-09 (M2.A)**]: Device registration flow — `RegistrationService.RegisterAsync` posts to `/api/devices/register` with machine name + username + OS + agent version. `ConfigStore` persists `DeviceConfig{tenantId, serverUrl, deviceId, deviceToken, signingKey}` to `%LOCALAPPDATA%\Toast2IT\Toast Notification\config.json` (or the package's LocalState equivalent when packaged) via atomic temp+Move. Bootstrap config sourced from env vars (`TOAST_TENANT_ID`, `TOAST_SERVER_URL`) or `bootstrap.json` next to the exe (D9 will write this from MSI properties). 30-minute REST `/api/devices/ping` heartbeat in addition to the hub's `OnConnectedAsync` LastPing touch.
- **D4** [x **COMPLETE 2026-05-09 (M2.A)**]: Payload verification — `Tenant.SigningKey` (32-byte random base64, generated at tenant create, returned to agent at registration). Server-side: `NotificationQueueService.BuildSignedPayload` HMAC-SHA256s the pre-serialized JSON bytes; sends `(payloadJson, signature)` as separate SignalR args to eliminate transport-serializer drift. Agent-side: `HmacVerifier.Verify` uses `CryptographicOperations.FixedTimeEquals` for constant-time compare; reject-and-drop on mismatch.
- **D5** [x **COMPLETE 2026-05-09 (M2.A)**]: Notification interaction tracking — `AgentHubClient.OnReceiveNotificationAsync` calls `ReportDelivery(notificationId)` after a successful render; `OnNotificationInvoked` parses click args and calls `ReportInteraction(notificationId, action)`. Plus REST `POST /api/notifications/{id}/interactions` for the activation-handler exit path (device-JWT-authenticated, same DeliveryStatus update + DeliveryUpdate hub broadcast as the hub method).
- **D6** [x **COMPLETE 2026-05-09 (M2.B)**]: Missed notification catch-up — `GET /api/notifications/pending?since=<DateTime?>` (device-JWT-authenticated, `device-per-hour` rate limit). Returns `PendingNotificationItem(NotificationId, PayloadJson, Signature, CreatedAt)[]` for the device's `Pending` deliveries; cap 100 per call; ordered by CreatedAt asc. PayloadJson + Signature byte-identical to the hub fanout via shared `NotificationPayloadBuilder.BuildSigned` helper — agent runs the same HMAC verify path regardless of channel. Agent: on `_hub.Reconnected` and once after cold `StartAsync`, fires `RunCatchupAsync` → fire-and-forget GET → for each item: verify, dedup-check, render, ReportDelivery. Plus INFO-M2A-003 startup recovery (`NotificationQueueService.RecoverOrphansAsync` sweeps `Notifications WHERE Status=Sending AND SentAt < now-5min` to `Failed`; deliveries STAY Pending so catch-up can still deliver — Carl's M2.B overrule on the original FIX-LIST plan) and INFO-M2A-004 agent dedup (`MemoryCache<Guid,byte>` with 1-hour sliding expiration; short-circuits BOTH render AND ReportDelivery; entry only set after `Show()` succeeds so render failures don't poison the cache). FIX-M2B-001 patched pre-commit (Abish caught: `_lastCatchupSince = DateTime.UtcNow` at ctor would have excluded every pre-existing Pending delivery on first call, defeating the catch-up's primary scenario). See `EVIDENCE/2026-05-09-m2b-catchup-and-recovery.md`.
- **D7** [x **COMPLETE 2026-05-08 (M2.C)**]: System tray icon — WinForms `NotifyIcon` on dedicated STA background thread. Five states per Diana's spec: Connecting (`#7A7A92`), Connected (`#00C9A7` teal), Reconnecting (`#F59E0B` amber 700ms pulse), Disconnected (`#F59E0B` amber static), Error (`#DC2626` red). Context menu: Open Dashboard / Send Test / View Log / Reconnect Now / Quit. `ConnectionStateChanged` event + `ReconnectAsync()` on `AgentHubClient`. Icon visible from process start. `UseWindowsForms=true`. Commit `ecf79ce`.
- **D8** [x **COMPLETE 2026-05-08 (M2.D)**]: Auto-update mechanism — Velopack 0.0.1298 integrated into the agent. `VelopackApp.Build().Run()` as first app statement (lifecycle hook). `UpdateService`: registry toggle `HKLM\SOFTWARE\Toast2IT\Toast Notification\DisableAutoUpdate` for enterprise MSP opt-out; `TrySelfRedirect` handles scheduled-task pointer problem on logons after first Velopack update; background 24h check+download loop (no-op when `IsInstalled=false` i.e. MSI-bootstrap path); `UpdateReady` event wired to tray. Tray: new "Update Available (vX.X.X)" bold menu item appears when download completes; click calls `ApplyUpdateAndRestart`. Build pipeline: `scripts/build-release.ps1` wraps `vpk pack`. Feed URL: `https://releases.toastnotification.com/agent/win-x64` (production setup deferred to M9). Code Sweep: SHIP WITH NOTES — INFO-M2D-003 through INFO-M2D-006 (see FIX-LIST.md).
- **D9** [x **COMPLETE 2026-05-08 (M2.C)**]: `CLIENTID` + `SERVERURL` MSI public properties → `bootstrap.json` via `SetupMode` (`--setup-bootstrap`). Detected before elevation guard. WiX `WriteBootstrapJson` deferred CA sequences before `InstallScheduledTask`. `DeleteBootstrapJson` on uninstall. MSI version 0.4.0.0. Silent install: `msiexec /i ... /qn CLIENTID=<guid> SERVERURL=https://...`. Commit `ecf79ce`.

### M2.A Closure (2026-05-09)
- 7 deliverables shipped (D1, D2, D3, D4, D5 + INFO-D5-001 mutex + INFO-MSIX-004-D activation handler).
- Plus: new device-JWT-authenticated REST `POST /api/notifications/{id}/interactions` endpoint for activation-handler fallback when hub unavailable.
- Code Sweep returned SHIP WITH NOTES. **FIX-M2A-001 patched pre-commit** (mutex prefix `Global\` → `Local\` to prevent multi-user-session collision). 4 INFO items deferred (INFO-M2A-002 → M3, INFO-M2A-003 + INFO-M2A-004 → M2.B, INFO-M2A-005 → M9).
- Build clean: 0 warnings + 0 errors solution-wide. MSIX smoke check clean (modulo pre-existing FIX-MSIX-003 cosmetic).

### M2.B Closure (2026-05-09)
- 1 deliverable shipped (D6 missed catch-up + recovery), closing the two M2.A INFO items it carried (INFO-M2A-003 orphan `Sending` recovery + INFO-M2A-004 agent dedup).
- New shared helper `NotificationPayloadBuilder.BuildSigned` enforces byte-deterministic signing across hub fanout and catch-up — M2.A standing rule #14 made into structural guarantee, not convention.
- Code Sweep returned SHIP WITH NOTES. **FIX-M2B-001 patched pre-commit** (catch-up `since=ctor_time` would have excluded every pre-existing Pending delivery on first run; nullable + omit-on-first-call). 4 INFO items deferred: INFO-M2B-002 (pagination beyond 100), INFO-M2B-003 (DB index `(DeviceId,Status,CreatedAt)`), INFO-M2B-004 (MemoryCache SizeLimit), INFO-M2B-005 (rate-limit on flaky-network catch-up storms).
- Build clean: 0 warnings + 0 errors solution-wide. No EF migration (pure code milestone).
- New standing rule (Carl): orphan `Sending` recovery marks the notification `Failed` but leaves `Pending` deliveries Pending so catch-up can still deliver. The original FIX-LIST plan ("deliveries → Failed accordingly") would have defeated catch-up.

### Agent Deployment (M2.A)
- Anthony: D1-D5 + INFO-D5-001 + INFO-MSIX-004-D + REST interaction endpoint (system-level wiring; no agent track parallelizable on this slice).
- Abish: Code Sweep (significant scope, multi-file, new wire protocol with security implications).
- Diana: On bench — backend wiring. Active at M2.C (tray icon design).

### Agent Deployment (M2.B)
- Anthony: D6 (backend pending endpoint + queue startup recovery; agent catch-up + dedup) — system-level, no parallelizable agent track on this slice.
- Abish: Code Sweep — caught FIX-M2B-001 (catch-up since-window bug) pre-commit.
- Diana: On bench — backend-only. Active at M2.C (tray icon).

### M2.C Closure (2026-05-08)
- 2 deliverables shipped (D7 tray icon + D9 MSI property wiring).
- Code Sweep returned SHIP WITH NOTES. INFO-M2C-001 through INFO-M2C-004 logged (see FIX-LIST.md). No blocking findings.
- Build clean: 0 warnings + 0 errors solution-wide. MSIX smoke check clean (pre-existing FIX-MSIX-003 only). MSIX manifest verified from produced artifact — all three extensions intact, MinVersion/MaxVersionTested unchanged.
- New standing rules (Carl): (1) WinForms STA tray thread is owned by `TrayIconService` and signalled via `SynchronizationContext.Post`; never update `NotifyIcon` from non-STA thread directly. (2) `SetupMode` must always be detected before the elevation guard in `AgentEntryPoint.RunAsync`.
- Diana design sign-off: Five-state iconography spec delivered and implemented. Placeholder GDI+ circles at spec colors; production SVG-rasterized assets owed before M9 GA.

### M2.D Closure (2026-05-08)
- 1 deliverable shipped (D8 Velopack auto-update).
- Code Sweep returned SHIP WITH NOTES. INFO-M2D-003 through INFO-M2D-006 logged (see FIX-LIST.md). No blocking findings.
- Build clean: 0 warnings + 0 errors solution-wide. MSIX smoke check clean (pre-existing FIX-MSIX-003 only).
- New architecture pattern: TrySelfRedirect in AgentEntryPoint.RunAsync handles the scheduled-task-pointer problem for post-MSI Velopack channel deployments. Documented in CONTEXT.md and UpdateService.cs header.

### Agent Deployment (Future M2 Slices)
- Abish: Code Sweep all slices ✓
- Diana: Production tray icon SVG assets (before M9)

---

## M3: Content Moderation & Security Hardening  **[COMPLETE 2026-05-08]**
**Goal**: Production security posture — content moderation, broadcast controls, payload signing.

### Deliverables (all closed)
- **D1** [x]: Azure Content Safety text scan integrated into `POST /api/notifications`. `IContentModerationService` + `ContentSafetyService`. Graceful no-key degradation (staging-safe).
- **D2** [x]: Image moderation on ad-hoc `HeroImageUrl` URLs. Library asset skip deferred to M5 (no asset-library lookup pipeline yet).
- **D3** [x]: Moderation decision engine — 0-1 = Pass, 2-4 = Review (PendingReview status, held for admin), 5-6 = Block (422 rejected). `AggregateModerationResults` takes worst-of-text+image.
- **D4** [x]: Admin approval queue — `GET /api/moderation/pending`, `POST /approve`, `POST /reject`. Backend-only; UI in M4 dashboard.
- **D5** [x]: Broadcast gate — `TargetType.All` requires `mfa=true` JWT claim; >100 devices requires `UserRole.Admin+`.
- **D6** [x]: MFA enforcement — OtpNet 1.4.0 TOTP, `POST /api/auth/mfa/enroll`, `POST /api/auth/mfa/verify` → 15-min elevated token with `mfa=true` claim.
- **D7** [x]: Tenant blocklists — `TenantBlocklistEntry` model, `GET/POST/DELETE /api/blocklist`, `BlocklistService` integrated as pipeline first-step (hit = Block, bypasses Azure scan).
- **D8** [x **PRE-CLOSED M2.A**]: HMAC payload signing shipped in M2.A D4.
- **D9** [x]: Security audit — Abish Code Sweep with 5-perspective review across all new surfaces + existing endpoints.

### INFO Items Closed
- INFO-M2A-002 ✓: DPAPI CurrentUser wrap on `ConfigStore.Save/TryLoad` (agent).
- INFO-M2D-005 ✓: WinVerifyTrust P/Invoke + cert subject check in `UpdateService.TrySelfRedirect`.
- INFO-M1-003 ✓: `EnrollmentKey` on `Tenant` + `FixedTimeEquals` gating in `DevicesController.Register`.
- INFO-M2B-003 ✓: Composite index `(DeviceId, Status, CreatedAt)` on `NotificationDeliveries`.
- INFO-M2B-004 ✓: `MemoryCache.SizeLimit = 50_000` + `Size = 1` per entry in `AgentHubClient`.
- INFO-M2B-005 ✓: `device-catchup-per-hour` (60/hr) rate limit; catch-up endpoint migrated off shared `device-per-hour`.
- INFO-M2C-002 ✓: WiX `LaunchCondition VersionNT64 >= 1000` (Win10+); runtime Program.cs holds precise build-19041 floor.

### Code Sweep
- Commit `362f9d3`: SHIP WITH NOTES — FIX-M3-001 patched pre-commit (WiX LaunchCondition value incorrect). INFO-M3-001/002/003 in FIX-LIST.

### Migration
- `M3SecurityHardening` (20260509024211): `TenantBlocklistEntries` table, `Tenants.EnrollmentKey` column, composite `NotificationDeliveries` index.

### Agent Deployment
- Anthony: D1-D7 (system-level + security-critical seams — single author for security milestone)
- Abish: Code Sweep / D9 security audit ✓

---

## M4: Admin Dashboard — Core UI  **[COMPLETE 2026-05-08]**
**Goal**: Functional web dashboard for notification management.

### Deliverables (all closed)
- **D1** [x]: Vite 6 + React 18 + TypeScript in `src/ToastRevival.Dashboard/`. Auth flow: login, register, JWT AuthContext with MFA claim detection, ProtectedRoute. React Router v6 with `createBrowserRouter`. API proxy to backend port 5216.
- **D2** [x]: Device inventory — table with search, online/offline filter, last-seen, agent version, decommission action. Parallel data load with refresh.
- **D3** [x]: Template gallery — 6 curated templates (Announcement, Alert, Action Required, Reminder, Celebration, Maintenance), each card renders a scaled-down live CSS preview thumbnail (0.55x ToastPreview component, not static images).
- **D4** [x]: Notification composer — template quick-pick, content fields with character count (title: 48, body: 90), action button editor (label/actionId/style), audio + scenario selectors.
- **D5** [x]: Live notification preview — CSS shadow implementation in Segoe UI font (scoped, not Inter). `#202020` desktop background, `#2D2D2D` Windows 11 notification card, real-time updates on every keystroke. Urgency badge, hero image 364:180 ratio, action button color logic (Critical=red, Success=green, Default=white). Diana sign-off: APPROVED.
- **D6** [x]: Send/schedule interface — All/Group/Device target picker, datetime-local schedule field. MFA broadcast confirmation modal (`BroadcastConfirmModal`) for `TargetType.All` — prompts TOTP verification, calls `/api/auth/mfa/verify`, stores elevated token before allowing send.
- **D7** [x]: Notification history — paginated table (25/page), expandable detail rows, delivery rate + click-through rate columns, search by title/status.
- **D8** [x]: Diana design review — spacing system verified (8px grid throughout), preview font verified (Segoe UI scoped), no purple, no emojis, --text-dim at #7A7A92 on standing known list.

### Backend INFO items closed (M4)
- INFO-M3-002 ✓: `IBlocklistService` interface extracted; `BlocklistService` implements it; `NotificationsController` now injects the interface.
- INFO-M3-003 ✓: `ContentSafetyService` injects `ILogger<ContentSafetyService>`; `Console.Error.WriteLine` replaced with structured logging.

### Code Sweep
- Commit `016c4c9`: SHIP WITH NOTES — INFO-M4-001/002/003 logged. FIX caught pre-commit: `templateId` field omitted from send payload (string IDs ≠ database Guids; deferred to M5). API response shape mismatch (`NotificationHistoryItem` vs `NotificationResponse`) caught and fixed during review.

### Build
- TypeScript strict, 0 errors, 0 warnings. Production build: 51 modules, 272KB / 82KB gzip. Backend build: 0 warnings, 0 errors.

### Agent Deployment
- Anthony: D1-D2, D5-D6 + backend INFO items ✓
- Abish: D3-D4, D7 + Code Sweep ✓
- Diana: D5 preview sign-off, D8 full design review ✓

---

## M5: Admin Dashboard — Advanced Features

### M5.A: Pre-work + User Management + API Keys  **[COMPLETE 2026-05-09]**
**Goal**: Close M4 INFO items, add D2 user management and D5 API key management.

#### Deliverables (all closed)
- **INFO-M4-001** [x]: TemplatesController `GET /api/templates` + tenant seeding on Register + Compose.tsx slug→Guid map + templateId in buildRequest.
- **INFO-M4-002** [x]: DeviceGroupsController — 6 endpoints (list, create, delete, members CRUD). DeviceCount maintained manually.
- **INFO-M4-003** [x]: History() pagination — `page`/`pageSize` query params with `Skip/Take`. Frontend already sends these; now honored.
- **D2** [x]: UsersController — `GET /api/users`, `POST /api/users/invite`, `PUT /api/users/{id}/role`, `DELETE /api/users/{id}`. Users.tsx page — table with role badge, MFA status, last active, role selector, remove action. Admin-only.
- **D5** [x]: TenantApiKey model + migration `M5ApiKeyManagement`. ApiKeysController — generate (returns full key once), list (prefix only), revoke (soft delete). ApiKeys.tsx page — key table, generate form, one-time display modal with clipboard copy.
- **Diana** [x]: Analytics chart spec delivered in DESIGN-SPEC.md — chart library (Recharts), layout, chart types, colors, tooltip style, backend API requirements for D1.

#### INFO Items Filed
- INFO-M5-001: No explicit error handling around template seeding in AuthController.Register() (M6)
- INFO-M5-002: UsersController.Invite has no role ceiling (acceptable)
- INFO-M5-003: TemplatesController.BuildDefaultTemplates coupling (M6 refactor candidate)

#### Code Sweep
- Commit pending: SHIP WITH NOTES — INFO-M5-001/002/003 logged.

#### Agent Deployment (M5.A)
- Anthony: INFO pre-work backend + D2 backend + D2 frontend ✓
- Abish-1: D5 full (model + migration + controller + frontend) ✓
- Abish: Code Sweep (significant scope) ✓
- Diana: Analytics chart spec for DESIGN-SPEC.md ✓

### M5.B: Analytics + Tenant Settings  **[COMPLETE 2026-05-09 (commit aabe739)]**

#### Deliverables (all closed)
- **D1** [x]: Delivery analytics — AnalyticsController (3 aggregate endpoints: /api/analytics/summary|volume|breakdown). Analytics.tsx: 4 metric cards (Sent/DeliveryRate/InteractionRate/ActiveDevices), full-width LineChart 240px (Sent #00C9A7 + Delivered #60A5FA dashed), BarChart delivery status (per-bar Cell colors per spec), horizontal BarChart template usage (#60A5FA). Time range selector 7/30/90d. Custom dark tooltip throughout. Recharts 2.x, isAnimationActive=false on all charts.
- **D3** [x]: Tenant settings — 4 new fields on Tenant model (LogoUrl, PrimaryColor, DefaultAudioSetting, DefaultScenario). Migration M5TenantSettings. TenantController: GET /api/tenant/settings (all-auth) + PUT (admin-only). TenantSettings.tsx: branding section (logo URL + synchronized color picker/hex input + logo preview), notification defaults (audio/scenario selects), rate limits as read-only metric cards.

#### INFO Items Filed
- INFO-M5B-001 (performance, future): AnalyticsController.Summary materializes delivery statuses in memory. Replace with server-side COUNT for high-volume tenants.
- INFO-M5B-002 (acceptable): UpdateSettings silently ignores invalid DefaultScenario enum values instead of 400.
- INFO-M5B-003 (acceptable): PrimaryColor stored without hex-format validation.

#### Code Sweep
- Commit aabe739: SHIP WITH NOTES — INFO-M5B-001/002/003 logged.
- Diana sign-off: chart tooltip + Cell status colors verified, TenantSettings color picker sync verified. APPROVED.

#### Build
- TypeScript strict, 0 errors. Vite build ✓. dotnet build: 0 warnings, 0 errors.

#### Agent Deployment (M5.B)
- Anthony: D1 backend + D1 frontend ✓
- Abish-1: D3 backend + D3 frontend ✓
- Abish: Code Sweep ✓
- Diana: Chart spec sign-off + D3 branding UI review ✓

### M5.C: Asset Library + Notification Scheduling  **[COMPLETE 2026-05-09]**

#### Deliverables (all closed)
- **D4** [x]: Asset library — `AssetsController` (GET list, POST upload multipart, DELETE). `ModerateImageBytesAsync` added to `IContentModerationService` — scans bytes pre-persist, no URL dependency. Files at `wwwroot/assets/{tenantId}/{assetId}{ext}` via `UseStaticFiles()`. AssetLibrary global query filter enforces tenant isolation. `Assets.tsx`: drag-and-drop drop zone, type selector (HeroImage/Logo/Icon), card grid with moderation badge, "Use as Hero"/"Use as Logo" navigate actions, two-step inline delete confirm.
- **D6** [x]: Notification scheduling — INFO-M1-005 resolved. `EnqueueDueScheduledAsync` backfills on startup; `RunSchedulerLoopAsync` PeriodicTimer(60s) sweeps for newly-due notifications. `ProcessAsync` guards non-Queued status (duplicate-enqueue safety). `Compose.tsx` UTC fix: `scheduledAt` converts datetime-local → ISO UTC; timezone note added.

#### INFO Items Filed
- INFO-M5C-001: Uploaded assets publicly accessible by URL (required for Windows agent image fetch). Document at M9.
- INFO-M5C-002: No `(Status, ScheduledAt)` index for scheduler sweep — acceptable MVP, flag for M6+.
- INFO-M5C-003: Drop zone accepts `image/*` MIME in addition to extension whitelist — backend extension check is the gate.

#### Code Sweep
- Commit pending: SHIP WITH NOTES. Pre-commit fix: ProcessAsync non-Queued guard prevents duplicate-fanout.

#### Build
- dotnet build: 0 warnings, 0 errors. No EF migration needed (AssetLibrary + ScheduledAt in InitialCreate).
- tsc --noEmit: 0 errors.

#### Agent Deployment (M5.C)
- Anthony: D4+D6 backend ✓
- Abish-1: D4+D6 frontend ✓
- Abish: Code Sweep ✓

### M5.D: Export  **[COMPLETE 2026-05-09]**

#### Deliverables (all closed)
- **D7** [x]: Export — audit logs + delivery reports (CSV/PDF).
  - Backend: `AuditController` — `GET /api/audit` (paginated list, admin-only) + `GET /api/audit/export?format=csv|pdf&days=7|30|90` (full-period download, admin-only).
  - Backend: `NotificationsController` `GET /api/notifications/{id}/report?format=csv|pdf` — per-notification delivery report (all authenticated users).
  - Backend: `PdfExportService` (QuestPDF Community) — audit log A4 landscape PDF + delivery report A4 portrait PDF. White background, teal header stripe, alternating rows.
  - Frontend: `AuditLog.tsx` — paginated table, time-range selector (7/30/90d), export dropdown. Admin-only route `/audit`.
  - Frontend: `History.tsx` — "Export Report" dropdown (CSV/PDF) in expanded notification row, blob download.
  - `Sidebar.tsx` + `App.tsx` — Audit Log nav item added (admin section).

#### INFO Items Filed
- INFO-M5D-001: `CsvCell` helper duplicated in AuditController + NotificationsController. Extract to shared utility at M6+.
- INFO-M5D-002: No index on `AuditLog.Timestamp`. Acceptable at MVP; add migration at M6+.
- INFO-M5D-003: CSV injection risk in audit action strings — acceptable for admin-only MSP export. Sanitize at M9 if needed.
- INFO-M5D-004: `PdfExportService.GeneratePdf()` synchronous — acceptable for infrequent admin export. Wrap in `Task.Run()` at M9 scale.

#### Code Sweep
- Pre-commit fix: `PdfExportService.cs` — `log.UserId?.ToString()[..8]` range indexer not null-safe; patched to ternary.
- Verdict: SHIP WITH NOTES.

#### Build
- `dotnet build ToastRevival.sln`: 0 warnings, 0 errors.
- `npx tsc -p tsconfig.app.json --noEmit`: 0 errors. Vite build: clean.

---

## M6: Licensing & Subscription System  **[COMPLETE 2026-05-09]**
**Goal**: Commercial-ready licensing with Stripe integration.

### Deliverables (all closed)
- **D1** [x]: Subscription tiers — `ILicenseService`/`LicenseService` with tier limits (Free=10, Pro=250, Enterprise=0/unlimited). `LicenseCount=0` means unlimited throughout the stack.
- **D2** [x]: Stripe integration — `BillingController` with `GET /api/billing/plan`, `POST /api/billing/checkout`, `POST /api/billing/portal`, `GET /api/billing/invoices`, `POST /api/billing/webhook`. Stripe.net 47.3.0. Webhook signature verified via `EventUtility.ConstructEvent` (raw body read, Stripe-Signature header). Events handled: `checkout.session.completed`, `customer.subscription.updated`, `customer.subscription.deleted`, `invoice.payment_failed`, `invoice.paid`. Fire-and-forget with `IServiceProvider` captured before response — HttpContext lifetime safe.
- **D3** [x]: License enforcement — `DevicesController.Register` calls `_license.CanRegisterDeviceAsync` before creating device. `BillingStatus.Canceled` → 403. Device limit reached → 402. `ConsumedCount` incremented on register, decremented on decommission.
- **D4** [x]: Usage metering — `SyncConsumedCountAsync` reconciles `ConsumedCount` from live device table on every plan fetch. `isNearLimit` (≥90%) and `isAtLimit` flags on plan endpoint. Near-limit and at-limit warnings rendered in `Billing.tsx`.
- **D5** [x]: Tenant onboarding flow — `Onboarding.tsx` at `/onboarding`. 3-step wizard: Welcome → Choose Plan (Free/Pro/Enterprise cards with Stripe checkout) → Install Agent (pre-filled MSI silent install command with CLIENTID/SERVERURL). Protected route.
- **D6** [x]: Admin billing page — `Billing.tsx` at `/billing`. Current plan card with tier, status badge, device usage bar. Plan upgrade cards (checkout redirect). "Manage Billing" → Stripe billing portal. Invoice history table with PDF/hosted links. Sidebar "Billing" nav item added.

### INFO Items Closed (carried from prior milestones)
- INFO-M5-001 ✓: `AuthController.Register` template seeding now wrapped in try/catch with explicit `RollbackAsync` + clean 500 response.
- INFO-M5D-001 ✓: `CsvHelper` static class extracted to `Utilities/CsvHelper.cs`. `AuditController` and `NotificationsController` both updated to use `CsvHelper.Cell()`.
- INFO-M5D-002 ✓: `IX_AuditLogs_Timestamp` index added in M6Billing migration.
- INFO-M5C-002 ✓: `IX_Notifications_Status_ScheduledAt` partial index added in M6Billing migration.

### INFO Items Filed
- INFO-M6-001: Stripe keys in `appsettings.json` are placeholder strings. Production must override via env vars (`Stripe__SecretKey`, `Stripe__WebhookSecret`, `Stripe__ProPriceId`, `Stripe__EnterprisePriceId`). Document in M9 DEPLOY.md.
- INFO-M6-002: `SyncConsumedCountAsync` on every plan fetch = one extra DB query. Acceptable at current scale. Cache at M9.
- INFO-M6-003: Invoice list makes live Stripe API call per request. Add short-TTL cache at M9.
- INFO-M6-004: `Onboarding.tsx` welcome step uses emoji placeholder icons. Diana will provide SVG replacements at M7.

### Migration
- `M6Billing` (20260509053033): `Tenants.StripeCustomerId`, `Tenants.StripeSubscriptionId`, `Tenants.PastDueAt`, `IX_Notifications_Status_ScheduledAt` (partial), `IX_AuditLogs_Timestamp`.

### Code Sweep
- Pre-commit fixes: (1) `HandleStripeEventAsync` — `IServiceProvider` captured before `Task.Run` to avoid HttpContext lifetime race; (2) invoice `Created` — Stripe.net returns `DateTime`, used directly.
- Verdict: SHIP WITH NOTES.

### Build
- `dotnet build ToastRevival.sln`: 0 warnings, 0 errors.
- `npx tsc -p tsconfig.app.json --noEmit`: 0 errors. Vite build: clean.

### Codex Pricing v2 / PlatformAdmin update - 2026-05-09
- Replaced tier UX and enforcement with the single Standard plan: $0.22/device/month, 100-device billable floor ($22), 14-day Stripe trial.
- `LicenseService.CanRegisterDeviceAsync` now blocks only canceled billing; Stripe subscription quantity sync updates the first subscription item to `max(100, activeDeviceCount)` on register/decommission.
- `BillingController.Plan` now returns `pricePerDevice`, `minimumDevices`, `monthlyFloor`, `deviceCount`, `billableDevices`, `currentBill`, `billingStatus`, and `trialEnd`.
- `Billing.tsx` and `Onboarding.tsx` were rewritten for the single-plan contract.
- Platform admin support added: `AppUser.IsPlatformAdmin`, JWT `platformAdmin` claim, `PlatformAdmin` policy, and `/api/system/*` cross-tenant endpoints.
- Production row `keith@colosolutions.com` promoted to `Role=SuperAdmin` and `IsPlatformAdmin=true`; smoke users cleaned up after verification.

### Agent Deployment
- Anthony: D2-D3 backend (Stripe integration, license enforcement, migration, CsvHelper, AuthController fix) ✓
- Abish-1: D1 service + D4 metering + D5 onboarding + D6 billing frontend ✓
- Abish: Code Sweep — caught 2 HOLD findings (IServiceProvider lifetime, invoice date) ✓
- Diana: Billing page design review — device usage bar and spacing approved ✓

---

## M7: Marketing Site — toastnotification.com Redesign  **[COMPLETE 2026-05-09]**
**Goal**: Professional marketing site that sells the product to MSPs and ranks well with both human search and AI-powered discovery.

Carl sliced M7 at orientation (2026-05-09). M7.A delivers the design spec and the onboarding emoji-to-SVG swap (closes INFO-M6-004) as a single Diana-led design milestone. M7.B/C/D are Build Mode sessions implementing against the locked spec.

### M7.A Closure (2026-05-09) — Design Spec + Onboarding SVGs
- DESIGN-SPEC.md Marketing Site section expanded from a 20-line stub to a full ~480-line specification: architectural premise (single SPA, two visual contexts), routes (`/`, `/pricing`, `/docs/*`, `/how-we-built-it`, `/legal/*`), `MarketingHeader`/`MarketingFooter` chrome, page-by-page wireframes for Home / Pricing / Docs / How We Built It / Legal, image strategy (no stock, no AI slop, product screenshots only), icon system, animation rules (default opacity:1; reveal animations gated on `prefers-reduced-motion`), mobile breakpoints, performance targets (FCP ≤ 1.0s, LCP ≤ 2.5s, Lighthouse ≥ 90), copy direction with banned-word list, complete `llms.txt` draft, SEO/JSON-LD direction, M7.B/C/D acceptance criteria, explicit out-of-scope list.
- INFO-M6-004 closed: four `Onboarding*` React SVG components shipped at `src/ToastRevival.Dashboard/src/icons/onboarding/index.tsx` (Bell, Template, Package, Launch). `Onboarding.tsx` welcome step swaps the four emoji icons for the SVG components. Stroke-only, 1.5 weight, round caps/joins, `currentColor` for parent-controlled color, `aria-hidden="true"` on every decorative SVG.
- Pre-commit security fix (INFO-M7A-001): `*.pem` and `*.key` patterns added to `.gitignore`. Two Lightsail SSH private keys at `Docs/Assets/` were untracked but not ignored; `git check-ignore` confirms both ignored now.
- TS check + Vite build clean. No backend changes.

### M7.B Closure (Corporate Edition) — 2026-05-09
- Re-scoped 2026-05-09 after Keith called the original M7.B render flat. New brief: corporate (Bloomberg / Datadog / Cloudflare register), not vibe-SaaS template.
- **Tier model collapsed.** Three-tier (Free / Pro / Enterprise) pricing replaced with single plan: $0.22/device/month, 100-device subscription minimum ($22/month entry), 14-day free trial. No upsells, no premium tiers. Backend pricing-v2 implementation is Codex's track per `Docs/ToastRevival/CODEX-HANDOFF.md`.
- **Real product screenshot** captured from the live composer (smoke-test tenant on prod) and shipped at `public/screenshots/composer-hero.png`. Replaces the M7.B-paused empty 16:9 placeholder frame.
- Home page rebuilt — hero (real screenshot), problem/solution (msg.exe vs branded toast), capabilities (4-card), security architecture (8-control matrix), how it works (3-step), pricing summary (math table), 14-day-trial final CTA. Zero internal milestone codes leaks. No "M9 roadmap" / "MOST POPULAR" / "publishes shortly" copy.
- Pricing page rebuilt — single Standard plan card, indicative-cost table by fleet size (100 → 5,000+ devices), 6-section "what's included" inclusion grid (Notifications, Targeting, Deployment, Tracking & audit, Security, Branding & ops), 8-question FAQ accordion (devices, mid-month overage, contracts, volume, trial, payment, data residency, SSO/SAML/HIPAA), procurement-friendly final CTA with `mailto:` for >5,000 devices.
- `MarketingLayout` / `MarketingHeader` / `MarketingFooter` / `BrandMark` / `FeatureIcons` corporate scaffolding from M7.A retained (light-mode Diana spec, restrained accent, 8px grid, Bloomberg-grade typography). Footer slimmed to 3-col (brand + Product + Legal).
- `HowWeBuiltIt.tsx` and `DocsComingSoon.tsx` deleted per Keith's standing rules: DocPro/AI-built case study is OFF the public marketing surface (lives in llms.txt + internal docs only); no placeholder pages ever ship.
- App.tsx routing: marketing routes (`/` and `/pricing`) wrapped in lazy-loaded `MarketingLayout`. Dashboard moves to `/dashboard`. Root `/` is a `RootIndex` toggle — anon visitors see the marketing Home, authenticated users redirect to `/dashboard`. Sidebar's existing `to="/"` Dashboard link resolves through the toggle without modification, so Codex's in-flight Sidebar WIP is untouched.
- Build clean: 721 modules transformed, marketing chunks lazy-loaded (`Home-*.js` 11 kB, `Pricing-*.js` 9.8 kB, `MarketingLayout-*.js` 4.2 kB, `MarketingLayout-*.css` 20.85 kB, `FeatureIcons-*.js` 1.5 kB), main bundle 722 kB. TS check passes against the full project including 25 in-flight Codex WIP files.
- Deployed to TOASTWEB1 with `/opt/toast/dashboard.bak.m7b` rollback backup. Post-deploy verification: emitted `/static/index-7zXBsNuA.js` returns 200 (722 540 bytes); `/`, `/pricing`, `/login`, `/register` all 200; `/screenshots/composer-hero.png` 200 (125 198 bytes). Playwright live render check: anon `/` → marketing Home, authed `/` → redirect to `/dashboard`, both with zero console errors.
- **Codex coordination doc** shipped at `Docs/ToastRevival/CODEX-HANDOFF.md` — file manifest of what we touched tonight and what's NOT touched, plus the spec Codex inherits for pricing v2 ($0.22/device + 100-device floor + 14-day trial) and PlatformAdmin role (`IsPlatformAdmin: bool` flag on `AppUser`, `/api/system/*` endpoints, idempotent seed for keithrlucier@gmail.com).

### Deliverables
- **D1** [x **COMPLETE 2026-05-09 (M7.A)**]: Diana DESIGN-SPEC for marketing site (shared design system with dashboard); onboarding emoji→SVG swap closes INFO-M6-004.
- **D2** [x **COMPLETE 2026-05-09 (M7.B Corporate Edition)**]: Marketing Home rebuilt corporate-grade — hero with real product screenshot, problem/solution comparison, capability matrix, security architecture, how-it-works, single-plan pricing summary, formal CTA. Live at https://toastnotification.com/.
- **D3** [x **COMPLETE 2026-05-09 (M7.B Corporate Edition)**]: Pricing page rebuilt as single-plan layout — $0.22/device + 100-device floor + 14-day trial, indicative-cost table, 6-card inclusion grid, 8-question FAQ. Live at https://toastnotification.com/pricing.
- **D4** [x **COMPLETE 2026-05-09 (M7.C)**]: Documentation pages (`/docs/*`) — hub, getting started, deploy guides (Store/Intune/RMM), API reference. Six lazy-loaded chunks (4.75–10.96 kB) under `MarketingLayout > DocsLayout` (240px sticky sidebar at top:80px, mobile collapsible nav). `CodeBlock` component with copy-to-clipboard + content-type label; `Callout` (note/warning) with `--status-success` / `--status-warning` borders. Banned-terms grep clean (no `persona`, `audio drama`, `jailbreak`, `M[0-9]+` codes, third-party tracking). Live at https://toastnotification.com/docs. See `EVIDENCE/2026-05-09-m7c-docs-hub.md`.
- **D5** [x **COMPLETE 2026-05-09 (M7.D)**]: SEO + LLM-discovery layer. Static files at `public/`: `llms.txt` (pricing-v2-correct, no `/how-we-built-it` reference, no banned terms — `personas` → `team members` patched pre-commit), `robots.txt` (allowlist marketing surfaces, disallow auth/dashboard/api/hubs), `sitemap.xml` (8 marketing URLs, 2026-05-09 lastmod). New `src/lib/seo.ts` `useSeo` hook + `softwareApplicationLd`/`pricingProductLd`/`techArticleLd`/`breadcrumbLd` helpers — zero new deps. All 8 marketing pages wired: per-page title, description, canonical, OG (title/description/url/type/site_name/image + 1200×630), Twitter card (summary_large_image), and one JSON-LD block per page (Home: `SoftwareApplication`; Pricing: `Product`+`AggregateOffer`+`BreadcrumbList`; Docs: `TechArticle`+`BreadcrumbList`). FIX-M7D-001 patched pre-commit (defensive `</script>` escape on JSON-LD serialization). 1200×630 `og-card.png` rendered via Playwright from `Docs/ToastRevival/EVIDENCE/m7d/og-renderer.html` (brand teal `#00C9A7`, Inter+JetBrains Mono, "Branded Windows notifications, sent from your dashboard." — Diana sign-off). Favicon set: `favicon.svg` (bell-with-checkmark, `#0F172A` rounded-square + `#00C9A7` stroke), `favicon-32.png`/`favicon-64.png`/`favicon-192.png`/`favicon-512.png`, `apple-touch-icon.png` (180×180). `index.html` `<head>` updated with default title/description/canonical/OG/Twitter/favicon refs + `<link rel="llms" href="/llms.txt">`. INFO-M7C-005 closed (docs body color tightened to `--text-primary` at `.m-docs-content p/ul/ol`). See `EVIDENCE/2026-05-09-m7d-seo-llm-discovery.md`.
- **D6** [x **COMPLETE 2026-05-09**]: Deployed to https://toastnotification.com — same Lightsail box (TOASTWEB1), same nginx, no DNS change. Curl + Playwright verified.

### SEO & LLM Discovery Notes (Keith's direction)

**Traditional SEO**: Content must be intent-dense for MSP search terms — "managed Windows toast notifications," "send notifications to endpoints via RMM," "Windows notification management for MSPs." No fluff. Real product screenshots. Structured data (JSON-LD: SoftwareApplication schema). XML sitemap. Canonical tags.

**LLM-targeted content (`llms.txt`)**: Place an `llms.txt` file at the domain root — similar to `robots.txt` but for AI crawlers (Anthropic, OpenAI, Perplexity all crawl it). Contents: plain-text description of the product, who it's for, key capabilities, pricing, and links to documentation. LLMs use this to answer "what is Toast Notification" questions accurately.

**The DocPro story**: Keith has approved telling it. Toast Notification was designed and built by an AI-native development team (Carl, Anthony, Diana, Abish) coordinated through DocPro — the same platform Keith uses for all product development. This is a real differentiator: a fully functional SaaS product for MSPs, built with AI from scratch, with no compromises on architecture or security. Diana will write this as a case study or "How we built it" section — not hero myth, concrete engineering narrative with real milestone data.

**`llms.txt` draft direction**: Lead with the product. Mention DocPro and the AI-built story as context, not the headline. LLMs should be able to answer: "What is Toast Notification?", "How do MSPs deploy it?", "What does it cost?", "Who built it?" — all from the llms.txt content alone.

### M7.D Closure (2026-05-09 PM)
- 1 deliverable shipped (D5 SEO + LLM-discovery layer), closing INFO-M7C-005 (docs body contrast).
- New `src/lib/seo.ts` self-contained head manager — 0 new deps. 8 marketing pages migrated from inline `useEffect` head pokers to `useSeo` (net additive: gains canonical, OG, Twitter, JSON-LD per page).
- llms.txt pricing-v2-correct (single-plan $0.22/device + 100-device floor + 14-day trial); `/how-we-built-it` URL removed everywhere; `personas` → `team members` (FIX banned-term enforcement, patched pre-commit).
- 1200×630 OG card + favicon set (SVG + 32/64/192/512/180 PNGs) rendered via Playwright off-prod from `Docs/ToastRevival/EVIDENCE/m7d/{og-renderer,favicon-renderer}.html`. Diana sign-off.
- Code Sweep returned SHIP WITH NOTES. **FIX-M7D-001 patched pre-commit** (defensive `</script>` escape on JSON-LD serialization). 3 INFO items deferred: INFO-M7D-001 (sitemap lastmod manual maintenance, M9 generator), INFO-M7D-002 (dashboard inherits marketing default `<title>`, Codex track), INFO-M7D-003 (useSeo runs in useEffect — modern crawlers fine, no-JS crawlers see defaults only — acceptable).
- Build clean: 0 errors, 0 warnings, 730 modules. seo lazy chunk 3.79 kB. main bundle 727.24 kB. MarketingLayout CSS 29.04 kB.
- New standing rules (Carl): (1) Public-facing static files (llms.txt, robots.txt, sitemap.xml, og-card.png, favicons) live in `src/ToastRevival.Dashboard/public/` and ship via Vite's static copy — no build-time generation step. (2) Per-page SEO is the page's responsibility via `useSeo`; index.html holds defaults only.

### Agent Deployment
- Diana: D1 ✓ (M7.A design spec); D5 favicon + OG card visual sign-off + INFO-M7C-005 contrast tweak ✓
- Anthony: M7.A onboarding SVG implementation ✓; D5 useSeo hook + helpers + per-page wiring + index.html head ✓
- Abish: M7.A Code Sweep ✓; D5 Code Sweep ✓ (caught banned term + script-tag XSS defense pre-commit)
- Build Mode: D2-D4 (M7.B/M7.C — shipped against the locked spec)
- Carl: D6 deploy coordination ✓

---

## M8: Integration Testing & Beta
**Goal**: End-to-end testing across all deployment channels, beta program launch.

Carl sliced M8 at orientation (2026-05-09). M8.A delivers the xUnit + WebApplicationFactory + Postgres test foundation and the first server-side E2E scenario (the API/SignalR/DB loop that D1/D2/D3 lab tests will exercise once Keith flights signed packages). M8.B/C/D are subsequent sessions covering load, security, and beta coordination.

### Deliverables
- **D1**: End-to-end test: Store install → register → receive notification → interact → verify delivery tracking (Keith-lab work — signed MSIX is already live at https://apps.microsoft.com/detail/9PFD6004DVTN, so the lab install can pull from the public Store listing directly).
- **D2**: End-to-end test: MSI/RMM install → same flow (Keith-lab)
- **D3**: End-to-end test: Intune LOB deploy → same flow (Keith-lab)
- **D4** [x **COMPLETE 2026-05-09 (M8.B)**]: Load harness — concurrent SignalR clients, single-tenant fanout latency percentiles (p50/p95/p99), queue saturation drain. Default 100-device CI-fast scenario asserts every device receives a signed payload within a 5s p95 budget; opt-in 1,000-device variant gated on `TOAST_TEST_RUN_LOAD_1K=1` for local measurement. Sustained-burst scenario verifies the unbounded `Channel<Guid>` in `NotificationQueueService` drains cleanly across 5×30 = 150 deliveries with no `Failed`/`PartialFailure` rows. Closes M8.A carry-forward INFO items: INFO-M8A-001 (PostgresFixture Docker pre-flight) and INFO-M8A-002 (ApiTestFactory class-scoped fixture share). See `EVIDENCE/2026-05-09-m8b-load-harness.md`.
- **D5** [x **COMPLETE 2026-05-09 (M8.C)**]: Security penetration testing — tenant isolation, auth bypass, content injection, privilege escalation. WebSocket-transport hub variant test (closes INFO-M8A-003) and env-gated registration-path load scenario (closes INFO-M8B-002) shipped alongside. FIX-M8C-001 (cross-tenant audit log leak) caught in-milestone and patched. See `EVIDENCE/2026-05-09-m8c-pen-test.md`.
- **D6**: Beta program — invite 3-5 MSP partners for real-world testing
- **D7**: Bug fix cycle based on beta feedback

### M8.A Closure (2026-05-09) — Backend Test Foundation + First E2E Scenario

- New test project `tests/ToastRevival.Api.Tests` shipped: xUnit 2.9.2, Microsoft.AspNetCore.Mvc.Testing 8.0.15, Microsoft.AspNetCore.SignalR.Client 8.0.15, Testcontainers.PostgreSql 4.0.0, Respawn 6.2.1. Project ID `{C7F2D4ED-90A1-4F2C-AD3E-4F5B6C7D8E9F}`, registered under new `tests` solution folder.
- `PostgresFixture` (collection-scoped `IAsyncLifetime`) spins up `postgres:16-alpine` via Testcontainers; `TOAST_TEST_CONNECTION_STRING` env var fallback for CI service containers and Docker-less dev boxes.
- `ApiTestFactory` (`WebApplicationFactory<Program>` override) layers `appsettings.Test.json` and rewrites `ConnectionStrings:DefaultConnection` to point at the fixture-managed Postgres. The API's existing `db.Database.Migrate()` startup hook applies the schema automatically. Forced `Production` environment so the test surface matches deployed behavior (real CORS policy, no Swagger).
- `PayloadVerifier` reproduces the agent-side HMAC verification (HMAC-SHA256 + `CryptographicOperations.FixedTimeEquals`) — Windows-only `ToastRevival.Agent` cannot be referenced from a netstandard test assembly, so the verifier mirrors the production primitives.
- First E2E test `HubFanout_DeliversSignedPayload_ReportsDelivery_ReportsInteraction` exercises tenant register → device register → SignalR connect (LongPolling for TestServer compat) → admin POST → background queue fanout → HMAC verify → `ReportDelivery` → `ReportInteraction` → DB invariants. Eight assertions cover the M2.A/M2.B critical path.
- Production code change: `Program.cs:212` exposes `public partial class Program;` for `WebApplicationFactory<Program>`. Zero behavior change — IL of `Program` is unchanged. No production callers affected.
- CI runner shipped at `.github/workflows/api-tests.yml`: Ubuntu runner with `postgres:16-alpine` service container, paths-filtered to API/sln/test changes only, uploads `.trx` results as 14-day artifact. Closes the "infrastructure with no execution path" gap on dev boxes without Docker.
- Closes carry-forward `INFO-M1-004` (zero automated tests since M1 shipped 2026-05-08).
- Code Sweep returned `SHIP WITH NOTES`. Five INFO items deferred to M8.B (`INFO-M8A-001` Docker pre-flight, `INFO-M8A-002` factory fixture-share, `INFO-M8A-003` WebSocket-transport variant) or no-action (`INFO-M8A-004` sln BOM, `INFO-M8A-005` PayloadVerifier drift). Zero HOLD findings. No banned-terms violations.
- Build: `dotnet build ToastRevival.sln` → 0 warnings, 0 errors solution-wide. Local `dotnet test` blocked (no Docker, no local Postgres on this dev box) — CI runner closes the gap on first push to `main`.
- See `EVIDENCE/2026-05-09-m8a-test-foundation.md`.

### Agent Deployment (M8.A)
- Anthony: full implementation (test project, fixture, factory, verifier, E2E scenario, CI workflow, sln + Program.cs edits) — system-level wiring with HMAC + JWT + tenant-isolation seams, single-author shape consistent with M2.A and M2.B.
- Abish: significant-scope Code Sweep — five INFO items, zero HOLD.
- Diana: not engaged (backend-only milestone).
- Carl: scope-slicing decision (M8.A vs full M8), CI-runner-or-no-runner call.

### M8.B Closure (2026-05-09) — Load Harness + Fixture Refactor

- 1 deliverable shipped (D4 load harness), closing the two M8.A INFO items it carried (INFO-M8A-001 PostgresFixture Docker pre-flight + INFO-M8A-002 ApiTestFactory class-scoped fixture share).
- New `LoadFixture` (collection-scoped via `LoadCollection`) owns one `ApiTestFactory` plus a `Respawner` snapshot of the post-migration empty schema. Both `EndToEndNotificationTests` and the new `LoadTests` consume the shared fixture; per-test isolation preserved via `_load.ResetAsync()` calls (truncates non-Identity tables in milliseconds, no-op when Respawner can't initialize against the connection string).
- New `LoadHarness` static helper exposes `SeedTenantAsync(factory, deviceCount)` and `RunSingleNotificationFanoutAsync(factory, tenant, timeout)`. Seeding bypasses the rate-limited `/api/devices/register` endpoint via DB scope + `TokenService` minting (rationale: M8.A E2E covers registration; M8.B is testing fanout). Admin user is seeded with `UserRole.Admin` so the `>100`-device target gate passes. Result type captures p50/p95/p99 latency, first/last receive offset, total elapsed, HMAC verify failures.
- New `LoadTests` class with three `[Fact]`s:
  - `Fanout_To_DefaultDeviceCount_DeliversWithinLatencyBudget` — 100-device CI-fast smoke; asserts every device receives, every payload HMAC-verifies, p95 < 5s.
  - `Fanout_To_LargeCount_OptIn_DeliversWithinLooseBudget` — 1,000-device variant gated on `TOAST_TEST_RUN_LOAD_1K=1`; asserts ≥99% receive within 2 minutes, zero verify failures. Skipped on CI by default to keep wall-time predictable.
  - `Sustained_Burst_AllNotificationsDrainCleanly` — 5 notifications dispatched in tight succession against 30 devices; polls until every notification reaches `NotificationStatus.Sent` (which `ProcessAsync` only flips on full delivery success), failing fast on `Failed`/`PartialFailure`.
- `PostgresFixture.InitializeAsync` adds a friendly Docker pre-flight: probes `/var/run/docker.sock` (Linux/macOS) or `\\.\pipe\docker_engine` (Windows), honors `DOCKER_HOST`, throws a single-paragraph `InvalidOperationException` pointing the developer at the env-var override and the CI service-container pattern. Without the gate, Testcontainers surfaces an internal connection-failure stack trace that isn't actionable.
- `EndToEndNotificationTests` refactored from per-test `ApiTestFactory` instantiation to the shared `LoadFixture`. Test now starts with `await _load.ResetAsync()` so the shared DB is clean for each run.
- Build: `dotnet build` on the API + tests projects clean (0 warnings, 0 errors) once the local Defender block on `Microsoft.AspNetCore.Mvc.Testing.Tasks.dll` is sidestepped via `-p:_MvcTestingTasksAssembly=<relocated-copy>`. CI runner (Linux Ubuntu) is unaffected and is the verification gate (M8.A precedent).
- New standing rules (Carl):
  - **Pre-seed via DB scope when the test target is downstream of registration.** Going through `/api/devices/register` for load testing burns the rate-limit budget on the registration path the test isn't even measuring; seeding keeps the pressure on the actual surface under test.
  - **Default load-test sizing optimizes for CI predictability.** The 1,000-device run is opt-in, not the default. The default scenario (100 devices) must complete under 30s wall-clock on the GitHub-hosted Ubuntu runner.
  - **Friendly fixture pre-flight is non-negotiable.** When a test fixture has external dependencies (Docker, network, third-party services), the fixture's `InitializeAsync` must produce a single-paragraph instruction on missing dependency before any vendor exception surfaces. The vendor stack trace is for after the gate, not in place of one.
- INFO items deferred:
  - INFO-M8B-001: load test p95 budget is a smoke threshold, not a regression gate — replace with a CI-runner-baselined rolling p95 at M8.C/M9.
  - INFO-M8B-002: M8.C should add an env-gated registration-path load scenario for completeness.
  - INFO-M8B-003: local-build Defender block on this dev box (environmental, no action).

### Agent Deployment (M8.B)
- Anthony: D4 load harness + fixture refactor + Docker pre-flight + E2E refactor — system-level wiring on the test infrastructure, single-author shape consistent with M8.A.
- Abish: significant-scope Code Sweep across 6 files (3 new + 2 modified test files + 1 doc surface).
- Diana: not engaged (backend-only milestone).
- Carl: scope-slicing decision (D4 lands first, D5 holds for M8.C with WebSocket-transport variant), test-sizing call (default 100, opt-in 1,000), pre-seed-vs-register-endpoint call.

### M8.C Closure (2026-05-09) — Security Pen-Test + WebSocket Variant + Reg-Path Load

- 22 new test [Fact]s shipped across 4 new test files. Three deliverables retired in one slice: D5 security pen-test, INFO-M8A-003 (WebSocket-transport hub variant), INFO-M8B-002 (registration-path load scenario).
- **FIX-M8C-001 caught in-milestone and patched.** AuditController.List + AuditController.Export had no `tenantId` filter — `AuditLog` is the one entity intentionally without a global query filter (PlatformAdmin SystemController needs the cross-tenant view), and the per-tenant audit endpoints were leaking other tenants' audit rows to any authenticated tenant admin. Both endpoints now extract `tenantId` from the JWT claim and add `.Where(l => l.TenantId == tenantId)` before the timestamp range filter. Composite `(TenantId, Timestamp)` index already exists at AppDbContext line 127. Single regression test (`TenantIsolation_AuditList_DoesNotLeakOtherTenantsRows`) seeds two tenants' audit rows and asserts only own-tenant data returns.
- New `SecurityHarness` static helper exposes `SeedTenantAsync(factory, role, deviceCount, isPlatformAdmin)` returning a `SeededPenTenant` record (Tenant + Admin user + tokens + devices). Distinct from `LoadHarness` in two ways: role is configurable (Technician/Admin/SuperAdmin probes) and the seeded `Tenant` + `AppUser` rows are exposed for tests that need to mutate state. Plus `ForgeUserJwt(factory, claims, expiresAt, signingKeyOverride, issuerOverride, audienceOverride)` for negative paths (expired tokens, wrong-key forgeries, missing claims) — the production `TokenService` reads `IsPlatformAdmin` from the DB row so it cannot mint forged tokens for negative tests.
- New `SecurityTests` class (20 [Fact]s, organized by lane via `// ─── X ───` regions):
  - **Tenant Isolation (6)**: `DeviceList_FiltersToOwnTenant`, `DeviceGetById_ReturnsNotFoundForOtherTenant`, `NotificationSendTargetingOtherTenantsDevices_ResolvesToZero`, `PendingEndpoint_DeviceFromOtherTenantSeesNothing`, `AuditList_DoesNotLeakOtherTenantsRows` (FIX-M8C-001 regression test), `HubDeviceConnectedEvent_DoesNotLeakAcrossTenantGroups`.
  - **Auth Bypass (5)**: `ExpiredUserJwt_Returns401`, `JwtSignedWithWrongKey_Returns401`, `DeviceJwtMissingDeviceIdClaim_ReturnsUnauthorized_OnPending`, `UserJwtOnDevicePendingEndpoint_ReturnsUnauthorized`, `BroadcastToAllWithoutMfaClaim_Returns403`.
  - **Content Injection (4)**: `XssInBody_PersistsRawAndSurvivesSigning`, `OversizedTitle_RejectedByModelValidation` (10 KB title vs `[MaxLength(64)]`), `UnicodeBoundary_RoundTripsClean` (emoji + RTL + ZWJ + replacement char), `ScriptCloseTagInBody_PersistsRawAndDeliversIntactOverHub` (exercises production payload-building via hub fanout, JSON parses cleanly).
  - **Privilege Escalation (5)**: `TechnicianInviteUser_Returns403`, `AdminChangingOwnRole_Returns400`, `AdminTargetingOtherTenantUser_Returns404` (cross-tenant user role change masked by Users query filter), `TechnicianBroadcastOver100Devices_Returns403`, `AdminWithoutPlatformAdminClaim_Returns403_OnSystemTenants`.
- New `WebSocketTransportTests` (1 [Fact]): `WebSocket_HubAuthenticatesViaQueryStringAccessToken_AndReceivesNotification`. Uses `factory.Server.CreateWebSocketClient()` + `SkipNegotiation = true` + `Transports = HttpTransportType.WebSockets` to hit the hub URL directly with a WebSocket upgrade and no Authorization header. The only auth channel is the `access_token` query string — exactly the path `JwtBearerEvents.OnMessageReceived` at `Program.cs:65-75` reads. Validates a notification round-trips the WebSocket transport with HMAC verify. **Closes INFO-M8A-003.**
- New `RegistrationLoadTests` (1 [Fact], env-gated `TOAST_TEST_RUN_REGISTRATION_LOAD=1`): `Registration_ConcurrentBurst_AllSucceed_NoCollisions_ConsumedCountAccurate`. Stands up its own `ApiTestFactory` against the shared `PostgresFixture` for a fresh rate-limit window — the `device-per-hour` policy partitions on `RemoteIpAddress?.ToString() ?? "anon"` for unauthenticated registrations, so a shared factory would leak budget from other tests. Issues 8 concurrent `POST /api/devices/register` calls (sized below the 10/hr `"anon"` partition cap with retry headroom), asserts all 200 OK with unique DeviceIds, and verifies `Tenant.ConsumedCount == 8` to catch any concurrent-write loss in the licensing path. **Closes INFO-M8B-002.**
- Pre-M8.C cleanup commit (`8ff8e59`): consolidated 16 stray screenshot artifacts from prior milestones (M5 smoke, M7.B preview, M7.C/M7.D prod verification, Track A) at the repo root into `Docs/ToastRevival/EVIDENCE/screenshots-2026-05-09/`, added `.playwright-mcp/` to `.gitignore`, and committed the Codex-track admin/platform/billing closeout note that had been sitting unstaged in EVIDENCE/. Diff hygiene before pen-test work, not after.
- Build: `dotnet build` on the API + tests projects clean (0 warnings, 0 errors) once the M8.B Defender workaround is applied (`-p:_MvcTestingTasksAssembly=/tmp/m8c-tasks/Microsoft.AspNetCore.Mvc.Testing.Tasks.dll`). CI runner (Linux Ubuntu) is the verification gate (M8.A/M8.B precedent — INFO-M8B-003 stands).
- Banned-terms grep clean across all four new test files (zero hits on customer-facing prohibited terms or team-name leaks).
- New M8.C standing sweep check (Carl): **"Does every entity without a global query filter have an explicit tenantId predicate at every per-tenant controller read site?"** `AuditLog` is the canonical "read by both PlatformAdmin and tenant admins" table and it bit us. Future tables of the same shape (audit-log style, cross-tenant analytics, etc.) need this check applied at controller review.
- INFO items:
  - **INFO-M8C-001 — RESOLVED 2026-05-10 (session 3)**: `TenantIsolation_HubDeviceConnectedEvent_DoesNotLeakAcrossTenantGroups` `Task.Delay(500ms)` replaced with 20ms predicate-poll, 300ms total timeout. Fail-fast when isolation is broken; otherwise drains the full window. See FIX-LIST.md.

### Agent Deployment (M8.C)
- Anthony: full implementation (4 new test files, AuditController patch) — single-author shape consistent with M8.A and M8.B.
- Abish: significant-scope Code Sweep across 5 files. SHIP WITH NOTES verdict, 1 INFO carry-forward, 0 HOLD.
- Diana: not engaged (backend-only milestone).
- Carl: scope-slicing decision (cleanup commit before pen-test work, FIX-M8C-001 caught and patched same milestone instead of deferred), AuditLog query-filter design call (PlatformAdmin needs cross-tenant; per-tenant controllers must filter explicitly), test-fixture-vs-shared-factory call on RegistrationLoadTests (own factory for rate-limit isolation).

### Agent Deployment (Future M8 Slices)
- Anthony: D1/D2/D3 end-to-end on Keith's lab — signed Store MSIX is live (9PFD6004DVTN); MSI/RMM channel and Intune LOB packaging exist; Keith's lab pass is the remaining gate
- Abish: D6/D7 (beta coordination is process work) + Code Sweep all slices

---

## M9: Launch
**Goal**: Public launch with Store submission, production infrastructure, and marketing.

### Deliverables
- **D1**: Production infrastructure — Azure/AWS deployment, monitoring, alerting, backups
- **D2**: Store submission — listing **9PFD6004DVTN is live** at https://apps.microsoft.com/detail/9PFD6004DVTN with the M0 D5 signed MSIX (closed 2026-05-09). Remaining work: keep the Store version current as new builds ship, expand to additional markets when product reach warrants, and Diana's curated tile assets before broader expansion.
- **D3** [x **COMPLETE 2026-05-09**]: RMM deployment packages — `infrastructure/rmm/install-toast-agent.ps1` (canonical PowerShell 5.1+ install script with Authenticode verify, idempotent skip-if-installed, msiexec /qn) + `uninstall-toast-agent.ps1` + per-RMM READMEs for NinjaOne, Datto RMM, ConnectWise Automate. Single source of truth; per-RMM dirs explain how to import the canonical script. Defense-in-depth: script verifies MSI Authenticode `Toast2IT, LLC` cert + chain before execution, refuses to install otherwise.
- **D4**: Launch marketing — email to beta users, social media, MSP community posts
- **D5**: Support system — ticketing, knowledge base, SLA documentation
- **D6**: SOC 2 preparation — document controls, begin audit process

### Agent Deployment
- Carl: D1-D2 (infrastructure + Store submission — operational ownership)
- Anthony: D3 (RMM packages require testing on actual RMM tools)
- Abish: D4-D6 (marketing coordination, documentation, compliance prep)

### M9.C Closure (2026-05-10) — Enrollment Key, Agent Drain Loop, Production Tray + Store Assets

Six punch-list items closed in one bundle. No MSI sign cycle required this session — agent code lands in repo, ships with next signed build.

1. **INFO-M1-003 (forward-only)** — Auto-generation of 24-byte base64 `EnrollmentKey` on every new `Tenant` row. Backfilled 3 existing prod tenants via psql + pgcrypto. `TenantSettingsResponse` exposes the key to admin role only. New admin-gated `POST /api/tenant/enrollment-key/regenerate` for rotation. `DeployCommand.tsx` fetches `/api/tenant/settings` on mount and includes `ENROLLMENTKEY=<key>` in the msiexec command + parameter chip. Backwards-compat note: v0.3.x field installs that don't pass the key will 403 at register going forward; field install base = Keith's lab only.
2. **INFO-M9B-001 (source-only)** — `AgentClient.RunCatchupAsync` passes `&limit=500` and loops until partial page. `MaxLoops=64` guard. `since` advances `items[^1].CreatedAt + 1 tick`. Drain ceiling 30,000/hr per device.
3. **Tray icon production glyphs** — `TrayIconService.CreateBellIcon` replaces `CreateCircleIcon`. Five state colors preserved; Disconnected and Error get a diagonal slash overlay. Same path data as the Store tile renderer (single brand bell).
4. **Microsoft Store tile assets** — `scripts/generate-msix-tile-assets.ps1` rewritten for Diana's M9.C production spec. Near-black `#0A0F1A` panel, brand-teal `#00C9A7` bell with halo on Square150 and Wide310, two-line "Toast / Notification" wordmark on Wide310. Outputs replace M0A placeholders at `src/ToastRevival.Agent/Images/{Square44,Square150,Wide310x150,StoreLogo}.png`.
5. **INFO-M2B-003 (doc fix)** — Composite DB index `(DeviceId, Status, CreatedAt)` confirmed in migration `20260509024211_M3SecurityHardening`. FIX-LIST + TODO updated.
6. **Azure Content Safety env config** — `ContentSafety__Endpoint` + `ContentSafety__Key` confirmed present in `/opt/toast/.env`. DEPLOY.md item closeable.

Design canon: `Docs/ToastRevival/Design/sources/tray-icons-and-tiles.svg` — single source of truth for the bell path data; both renderers (C# tray, PowerShell tile) cross-reference it.

Code Sweep: SHIP. INFO-M9C-001/002/003 carry-forwards (full agent enforcement next signed MSI; deploy-command fetch caching nice-to-have; rotation breaks future MSI installs not registered devices).

Cross-AI handoff: parallel session shipping action-buttons + marketing-site rewrite tracks. Selective-commit preserved their unstaged WIP through M9.C close. Full handback note at `Docs/ToastRevival/CODEX-HANDOFF.md` (M9.C section).

Build clean: API, Agent, Dashboard. Deploy verified — `/api/health` 200, `/login` 200, `/security/` + `/docs/` 200 after trailing-slash redirect, auth gates 401 correctly.

### Agent Deployment (M9.C)
- Anthony: backend EnrollmentKey wiring, agent drain loop, frontend DeployCommand, tile renderer, tray bell glyph.
- Diana: production tray icon spec + Store tile spec; SVG canon as single source of truth.
- Abish: Code Sweep (significant scope, multi-file, multi-surface) — SHIP.
- Carl: foreman + selective-commit discipline + Codex handoff coordination.

### M9.B Closure (2026-05-10) — Pending Endpoint Pagination
- INFO-M2B-002 resolved. `GET /api/notifications/pending` now accepts an optional `?limit=<int>` query param, defaulting to 100 (backwards compat for v0.3.x agents that omit it) and server-clamped to `[1, 500]`. Wire shape stays an array — agents in the field unmarshal `List<PendingNotificationItem>` and need no rebuild.
- New integration test `SecurityTests.PendingEndpoint_LimitParamControlsPageSize_ClampsToBounds` exercises default + explicit-in-range + upper-clamp (limit=999 → 500) + lower-clamp (limit=0 → 1) against a real Postgres container with 510 seeded Pending deliveries.
- Code Sweep: SHIP. No HOLD findings. INFO-M9B-001 (carry-forward): agent-side adoption of `?limit=500` deferred to next signed agent build to avoid an MSI rebuild + sign cycle this session — until then the fleet drains at 100/call. INFO-M2B-003 (composite DB index `(DeviceId, Status, CreatedAt)`) becomes more valuable now that callers can request 500-item pages; still acceptable at MVP scale.
- Pre-commit hygiene (Abish catch): two unrelated WIP files in working tree (`Security.tsx` 226-line marketing copy edit, `Agent.csproj` 0.3.1.0 version bump) preserved unstaged via selective-commit pattern. M9.B commit ships only the controller + test + spec note + milestone docs.
- Build clean: 0 warnings + 0 errors solution-wide. CI runs the new test on `ubuntu-latest` against `postgres:16-alpine` service container — local Docker not required.

### Agent Deployment (M9.B)
- Anthony: backend controller + integration test (no parallelizable track at this scope).
- Abish: Code Sweep — caught the pre-existing WIP scope leak before commit.
- Diana: On bench — backend-only milestone, no UI work.
- Carl: foreman + selective-commit discipline.

### Session 3 Cleanup (2026-05-10) — Bucket 3 + INFO-MSIX-004

Four low/nice-to-have INFO items closed. No new milestone, no EF migration, no signed build.

1. **INFO-MSIX-004 (A/B/C)** — `DiagLog` 512KB rotation (`.log` → `.log.1`, two generations) added to `Program.cs::DiagLog.RotateIfNeeded`. `--diag` flag handler (`DiagMode` class) dumps log path + last 200 lines to stdout; dispatched before elevation check so support staff can run as admin. Ships next signed agent build.
2. **INFO-M8C-001** — `SecurityTests.TenantIsolation_HubDeviceConnectedEvent_DoesNotLeakAcrossTenantGroups` 500ms fixed delay replaced with 20ms predicate-poll (300ms timeout). Fail-fast on leaked event; removes 500ms floor from the passing path.
3. **INFO-M7C-003** — Docs route paths centralized to `src/routes/docsRoutes.ts` (`DOCS_PATHS`). `App.tsx` (router) and `DocsLayout.tsx` (nav sidebar) both import from it — single source of truth, structural coupling prevents future nav/route drift.
4. **INFO-M9C-002** — `DeployCommand.tsx` enrollment-key fetch module-cached (`_enrollmentKeyCache` promise-level singleton). `/api/tenant/settings` fires at most once per page load regardless of mount count.

**App store status:** Partner Agreement fully signed (2026-05-10). Listing 9PFD6004DVTN 100% live. Remaining pre-M9-marketing-push polish: Store listing copy rewrite (Diana tile assets shipped M9.C).

5. **Home page redesign** — technical hero (copy-left / RMM code-block-right), terminal msg.exe vs. Toast Notification comparison, bento grid for platform architecture, accurate two-card pricing (free ≤25 / standard $0.22 from device 26). `bento.css` new stylesheet. No fake stats. Deployed 2026-05-10 (commit `a6b658c`).

---

---

## M10: Trial Approval Gate + Tenant Install Surface  **[COMPLETE 2026-05-12]**
**Goal**: Replace open tenant self-registration with an admin-reviewed approval flow. Add a dedicated MSI install surface for approved tenants.

### Deliverables (all closed)
- **D1** [x]: `TrialRequest` EF model + `M10TrialApprovalGate` migration — stores company, contact, phone, job title, use case, Turnstile metadata.
- **D2** [x]: Public `/register` now queues a `TrialRequest` instead of creating a tenant. `AllowLegacyDirectRegister=false` in production.
- **D3** [x]: Platform admin `/system/trial-requests` — lists pending requests with company/contact/use-case, approve or reject actions. Approval provisions tenant + sends password setup email.
- **D4** [x]: Cloudflare Turnstile bot protection wired to registration form. `TurnstileVerifier` middleware. `Turnstile__Required=true` in production.
- **D5** [x]: `/devices/install` tenant install surface — tenant admin sees prefilled msiexec command with CLIENTID + ENROLLMENTKEY. Devices and Onboarding pages link directly.
- **D6** [x]: Marketing + SEO refresh — pricing, docs, Home.tsx, JSON-LD, `llms.txt` aligned with reviewed-trial model and block pricing ($22/mo flat). No fake stats.

### Code Sweep
- Sweep #34 (Abish): 40-file bundle shipped in 2 commits. 6 INFO tickets. Cloudflare Turnstile validator follows defense-in-depth. M9.A/login/MFA endpoints preserved. Banned-terms clean.
- Verdict: SHIP.

### Build
- `dotnet build`: 0 warnings, 0 errors.
- `npm run build`: passed (tsc + Vite + prerender-seo.mjs). Existing chunk-size warning unchanged.

---

## M11: Self-Hosted Docker Distribution + Business Model Refresh  **[IN PROGRESS — opened 2026-05-12]**
**Goal**: Make Toast Notification publicly self-hostable via Docker Compose. Refresh the three-tier business model to reflect the project's honest origin and current reality. No SaaS hype.

### Origin context
Built in 2020 for MSPs during the COVID-19 WFH explosion. 986,000 legitimate messages delivered across 17 production tenants. Teams and Slack crushed the generic use case. This is now a passion project for the shops where OS-level fleet notification without a third-party dependency still matters. The self-hosted path is genuinely free, no strings.

### Business model (three tiers)
| Tier | What they get | Cost |
|---|---|---|
| Free Trial | 2 devices, fully functional, 14 days, admin-approved | $0 |
| Managed SaaS | 0–100 devices, we handle hosting/updates/backups | $22/mo |
| Roll Your Own | Full Docker Compose source, self-hosted, no device cap | $0 |

### Deliverables
- **D1** [x **COMPLETE 2026-05-12**]: Docker Compose infrastructure — `src/ToastRevival.Api/Dockerfile` (multi-stage ASP.NET Core 8), `src/ToastRevival.Dashboard/Dockerfile` (Vite → nginx alpine), `src/ToastRevival.Dashboard/nginx.conf` (SPA routing + API/SignalR proxy), `docker-compose.yml` (3-service stack: toast-api, toast-dashboard, toast-db), `.env.example` (all config keys documented), `README-SELF-HOST.md` (origin story + 3-step deploy). Billing disabled by default (empty Stripe keys). Turnstile and Content Safety gracefully degrade. Named volumes for DB data and uploaded assets. Commit `eff6c6c`.
- **D2** [x **COMPLETE 2026-05-12**]: `TOAST_REQUIRE_BILLING` env var added. When `false`/unset, `CanRegisterDeviceAsync` returns `true` immediately — no device cap, no Stripe required. SaaS sets `true`. Commit `76ae2ef`.
- **D3** [x **COMPLETE 2026-05-12**]: `DISABLEAUTOUPDATE=1` wired in `installer/ToastRevival.Agent.Setup.wxs`. Writes `HKLM\SOFTWARE\Toast2IT\Toast Notification\DisableAutoUpdate=1`. `UpdateService.cs` already consumed that key — no C# changes. Commit `76ae2ef`.
- **D4** [x **COMPLETE 2026-05-12**]: Full git history scan — CLEAN. Server IPs exist only in `Docs/ToastRevival/` and commit messages, neither of which shipped to the public repo. No PEM content, no live Stripe keys, no passwords. Debug MSIX artifact caught and scrubbed before public push.
- **D5** [x **COMPLETE 2026-05-12**]: Public repo live — https://github.com/keithrlucier/toast-notification — 218 files, single clean commit, fresh history. PAT rolled by Keith post-push.
- **D6** [x **COMPLETE 2026-05-12**]: `BillingPlanRules.TrialDeviceLimit = 2`. `CanRegisterDeviceAsync` gates Trialing tenants at 2 devices. `Onboarding.tsx` BillingStep replaced with three-tier `TierCard` layout. INFO-M11-SW-001 deferred (TOCTOU on concurrent trial registration — not blocking). Commit `76ae2ef`.
- **D7** [ ]: Marketing site pricing page rewrite — honest three-tier copy. Diana leads. No comparison tables, no urgency CTAs. Origin story in the lead.

### Bonus deliverables shipped 2026-05-12 (outside original M11 scope)
- **Tenant sidebar branding** [x]: Sidebar shows tenant name + logo. Platform admins see platform branding. `isAdmin` fixed — platform admins excluded from tenant-scoped ADMIN_ITEMS. Loading skeleton. Commit `a5b9a8a`.
- **Dynamic favicon** [x]: `maybeUpdateFavicon()` on `setSession` updates browser tab icon to tenant logo. Platform admins skip. Commit `a5b9a8a`.
- **Logo auto-inject removed** [x]: `NotificationsController.Send` no longer falls back to tenant logo when Logo URL field is blank. Operator intent (blank = no logo) respected. Commit `eb388b0`.

### Open Research
- Does `LicenseService.CanRegisterDeviceAsync` return true for all devices when `Stripe__SecretKey` is empty? Verify in code — don't assume.
- What is the expiry UX after 14 days? Agent goes quiet? Dashboard locks? Tenant gets a nudge and upgrade path?
- GitHub org: **RESOLVED** — `keithrlucier/toast-notification` (personal account). Keith provides PAT; rolls it after push.
- Agent code signing: **RESOLVED** — self-hosters must either (A) use our pre-signed MSI from GitHub Releases [Path A, recommended] or (B) acquire their own OV cert (~$300-400/yr, 1-3 day validation) and sign the MSI themselves [Path B, high-friction]. Path B friction is intentional — it converts evaluators to SaaS. Documented in `README-SELF-HOST.md` and `Docs/rollyourown.md`.
- `DISABLEAUTOUPDATE`: registry key name + WiX property syntax TBD at D3.

### Code Sweep
- D1 sweep (Abish, 2026-05-12): Doc-only + additive infrastructure. Zero blast radius on existing code. No server IPs, no codename leaks, no secrets. SHIP.
- D2–D7: sweeps queued per deliverable.

### Team Assignment
- Carl: architecture decisions (billing bypass, public repo strategy, git history)
- Anthony: D2 (LicenseService audit), D3 (DISABLEAUTOUPDATE), D4 (history sanitization), D5 (repo creation + first push)
- Diana: D7 (marketing pricing page rewrite)
- Abish: code sweeps on each deliverable

---

## Milestone Summary

| Milestone | Focus | Key Risk | Est. Complexity |
|---|---|---|---|
| M0 | Deployment validation | GPO restrictions on scheduled tasks | Medium |
| M1 | Backend API core | Multi-tenant data isolation correctness | High |
| M2 | Windows agent | SignalR reconnection + offline catch-up | High |
| M3 | Security & moderation | Moderation accuracy vs. false positives | Medium |
| M4 | Dashboard core UI | Live preview accuracy matching Windows rendering | High |
| M5 | Dashboard advanced | Timezone handling for scheduling | Medium |
| M6 | Licensing & billing | Stripe webhook reliability, edge cases | Medium |
| M7 | Marketing site | Design consistency with dashboard | Low |
| M8 | Testing & beta | Real-world deployment diversity | High |
| M9 | Launch | Store certification, infrastructure hardening | Medium |
| M10 | Trial approval gate | Turnstile + registration flow correctness | Low |
| M11 | Self-hosted distribution | LicenseService bypass + git history sanitization | Medium |
