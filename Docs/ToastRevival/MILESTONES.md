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
- **D5**: Verify Store submission pipeline — push skeleton app to the existing 9P5L0MRMFRRF listing (private/hidden flight)
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

### M5.C (Future)
- **D4**: Asset library — upload/manage hero images + logos, moderation status
- **D6**: Notification scheduling — calendar view, timezone handling

### M5.D (Future)
- **D7**: Export — audit logs, delivery reports (CSV/PDF)

### Agent Deployment (M5.B planned)
- Anthony: D1 backend (3 aggregate endpoints) + D1 frontend (Recharts charts)
- Abish: D3 backend + D3 frontend
- Diana: D1 chart sign-off, D3 branding UI review

---

## M6: Licensing & Subscription System
**Goal**: Commercial-ready licensing with Stripe integration.

### Deliverables
- **D1**: Subscription tiers — Free (10 devices), Pro (250 devices), Enterprise (unlimited)
- **D2**: Stripe integration — subscription creation, billing portal, webhook handling
- **D3**: License enforcement — device registration blocked when limit reached, grace period handling
- **D4**: Usage metering — device count tracking, overage alerts
- **D5**: Tenant onboarding flow — signup, plan selection, first device registration walkthrough
- **D6**: Admin billing page — current plan, usage, invoices, upgrade/downgrade

### Agent Deployment
- Anthony: D2-D3 (Stripe integration + license enforcement — payment code must be precise)
- Abish: D1, D4-D6 (tier definitions, metering, onboarding flow — bounded tasks)

---

## M7: Marketing Site — toastnotification.com Redesign
**Goal**: Professional marketing site that sells the product.

### Deliverables
- **D1**: Diana DESIGN-SPEC for marketing site (shared design system with dashboard)
- **D2**: Build Mode execution — hero, features, pricing, testimonials, documentation
- **D3**: Pricing page — transparent per-device pricing, tier comparison
- **D4**: Documentation — getting started guide, deployment guides (Store/Intune/RMM), API docs
- **D5**: SEO optimization — meta tags, structured data, sitemap
- **D6**: Deploy to toastnotification.com (existing domain)

### Agent Deployment
- Diana: D1 (design spec — must be done before build)
- Build Mode: D2-D5 (marketing site is a Build Mode project)
- Carl: D6 (deployment coordination)

---

## M8: Integration Testing & Beta
**Goal**: End-to-end testing across all deployment channels, beta program launch.

### Deliverables
- **D1**: End-to-end test: Store install → register → receive notification → interact → verify delivery tracking
- **D2**: End-to-end test: MSI/RMM install → same flow
- **D3**: End-to-end test: Intune LOB deploy → same flow
- **D4**: Load testing — 1,000 concurrent agents, notification blast, measure delivery latency
- **D5**: Security penetration testing — tenant isolation, auth bypass, content injection, privilege escalation
- **D6**: Beta program — invite 3-5 MSP partners for real-world testing
- **D7**: Bug fix cycle based on beta feedback

### Agent Deployment
- Anthony: D1-D3, D5 (end-to-end requires system understanding, security testing requires judgment)
- Abish: D4, D6-D7 (load testing is scripted, beta coordination is process work)

---

## M9: Launch
**Goal**: Public launch with Store submission, production infrastructure, and marketing.

### Deliverables
- **D1**: Production infrastructure — Azure/AWS deployment, monitoring, alerting, backups
- **D2**: Store submission — update 9P5L0MRMFRRF with production build, expand to all markets
- **D3**: RMM deployment packages — documented scripts for NinjaOne, Datto, ConnectWise
- **D4**: Launch marketing — email to beta users, social media, MSP community posts
- **D5**: Support system — ticketing, knowledge base, SLA documentation
- **D6**: SOC 2 preparation — document controls, begin audit process

### Agent Deployment
- Carl: D1-D2 (infrastructure + Store submission — operational ownership)
- Anthony: D3 (RMM packages require testing on actual RMM tools)
- Abish: D4-D6 (marketing coordination, documentation, compliance prep)

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
