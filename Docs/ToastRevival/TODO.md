# Toast Notification - Immediate Actions

## Current Reality

Project status: **M0A → M0 D5 ALL COMPLETE. M1 COMPLETE 2026-05-08. M2.A COMPLETE 2026-05-09. M2.B COMPLETE 2026-05-09.** Backend pending endpoint + queue startup recovery + agent reconnect catch-up + 1-hour dedup all shipped. FIX-M2B-001 caught + patched pre-commit by Abish (catch-up since-window bug). 4 INFO items deferred to M3/M5. Next: **M2.C** — Diana-engaged tray icon spec (D7) + WiX MSI property wiring (D9) so the agent picks up CLIENTID/SERVERURL from the installer.

## Keith

- [x] Renew code signing certificate (Sectigo OV, NotAfter 2027-04-15).
- [x] Confirm signing requires a hardware token (Thales).
- [x] Sign first MSI (ToastRevival.Agent-0.1.0.0.msi).
- [ ] Re-sign the rebranded MSI when needed (`artifacts/installer/ToastNotification.Agent-0.2.0.0.msi`).
- [x] Sign the M0 D2 MSIX (`artifacts/installer/msix/ToastNotification.Agent-0.2.0.1.msix`) - DONE 2026-05-07 via `signtool.exe`.
- [x] Validate install of signed M0 D2 MSIX on Win11 lab. 0.2.0.1 installed but did not fire toasts (FIX-MSIX-004); 0.2.0.3 patched + signed + installed 2026-05-08, visible toast fires, NotificationInvoked routes cleanly.
- [x] Confirm Microsoft Partner Center access (Keith signed in 2026-05-07). Verifying app ID `9P5L0MRMFRRF` is reachable from this account is M0 D5 work.
- [ ] Accept the updated App Developer Agreement in Partner Center if prompted (only when M0 D5 actually flights a build).
- [ ] Confirm domain/DNS control for `toastnotification.com` (gates M7).
- [ ] Confirm Stripe account status later, after deployment spike is proven (gates M6).

## Engineering - M0A (closed)

- [x] Install .NET SDK on the development machine.
- [x] Install/verify Windows App SDK (1.7.250310001 via NuGet), `<EnableMsixTooling>true</EnableMsixTooling>` set.
- [x] Install WiX 5.0.2 dotnet global tool.
- [x] Create repository baseline and push to GitHub.
- [x] Build the minimal Windows agent (sends one hardcoded local app notification).
- [x] Produce local Release publish artifacts (framework-dependent + self-contained).
- [x] Build a rich local notification spike with hero image, logo override, action buttons, audio. Six Diana templates covered.
- [x] Package the agent locally as a per-machine MSI (Startup-folder shortcut + Start-menu shortcut).
- [x] Sign the package with the renewed certificate (Thales token + Sectigo OV cert).
- [x] Test install / run / toast behavior on a clean Windows 11 lab machine.
- [x] Rebrand all user-facing strings to "Toast Notification" (commit 56b0adb).
- [x] Record evidence in `Docs/ToastRevival/EVIDENCE` (4 entries this milestone).

## Engineering - M0 D2 (CLOSED 2026-05-08)

- [x] **M0 D2 build:** MSIX package built via WinAppSDK 1.7 SingleProject path. First build (0.2.0.0) failed signing with 0x80091005 (manifest Publisher missing O=Toast2IT, LLC). Manifest corrected, rebuilt as 0.2.0.1.
- [x] **M0 D2 sign:** Signed 2026-05-07 via `signtool.exe`. `scripts/sign-msix.ps1` codifies the working flow.
- [x] **M0 D2 install:** 0.2.0.1 installed cleanly via Add-AppxPackage but did not fire toasts.
- [x] **M0 D2 visible-toast confirmation:** Resolved via FIX-MSIX-004 (0.2.0.3 commit `6e3495c`). Single toast fires from packaged Start menu launch, button click routes through `NotificationInvoked` handler with expected argument payload.
- [x] **Push Codex's runner-setup evidence note:** Committed 2026-05-08 (commit `3c702fc`).

## Engineering - M0 D3 (CLOSED 2026-05-08)

- [x] M0 D3 build: WiX installer registers `\Toast2IT\ToastNotificationAgentLogon` Scheduled Task via deferred schtasks.exe /Create /XML /F custom action; uninstall via /Delete /F (Return=ignore for idempotency). StartupShortcut component dropped. Built `ToastNotification.Agent-0.3.0.0.msi` 50.61 MB.
- [x] **Keith**: signed `ToastNotification.Agent-0.3.0.0.msi` via Thales+Sectigo OV.
- [x] **Keith**: installed signed MSI on Win11 lab cleanly.
- [x] **Keith**: verified task created via Get-ScheduledTask — State=Ready, MSFT_TaskLogonTrigger, MSFT_TaskExecAction.
- [x] **Keith**: confirmed alert toast fires at next user logon (visible Critical-scenario banner with hero/logo/Acknowledge/Report buttons).
- [x] Captured `EVIDENCE/2026-05-08-m0-d3-task-fires-at-logon.md` with Get-ScheduledTask output and toast description.
- [ ] (Deferred to M0 D4) Uninstall idempotency check + 0.3.x → 0.3.y major-upgrade race validation. Roll into the GPO matrix testing.

## Engineering - M0 D4 (in progress 2026-05-08)

- [x] Apply FIX-MSIX-002: `Package.appxmanifest` MinVersion 17763→19041, `TargetPlatformMinVersion` updated, manifest version 0.2.0.3→0.2.1.0.
- [x] Build `ToastNotification.Agent-0.3.1.0.msi` for major-upgrade test (UpgradeCode matches 0.3.0.0).
- [x] Write `scripts/verify-d4-matrix.ps1` — pass/fail script for each test checkpoint.
- [x] GPO standing rules documented in `CONTEXT.md` (notification policy, AppLocker, Intune, OS floor).
- [x] Evidence shells: `EVIDENCE/2026-05-08-m0-d4-fix-msix-002.md`, `EVIDENCE/2026-05-08-m0-d4-matrix-results.md`.
- [x] **Keith**: signed + installed 0.3.1.0.msi on Win11 lab. Task created, toast fires, second user toast confirmed, uninstall clean.
- [x] Mark M0 D4 COMPLETE in MILESTONES.md. **CLOSED 2026-05-08.**

## Engineering - M0 next deliverables

### M0 D5 (build complete 2026-05-08 — awaiting Keith sign + flight)
- [x] FIX-MSIX-001: `-p:TargetPlatformVersion=10.0.22621.0` added to `build-msix.ps1` command line. (csproj PropertyGroup approach does not work — TFM late-import override; see CONTEXT.md rule #4.)
- [x] Add `uap5:StartupTask` extension to manifest for Store/MSIX logon-launch parity.
- [x] Build MSIX 0.2.1.0: `.\scripts\build-msix.ps1 -Version 0.2.1.0 -SkipAssetGeneration`. Verified: MinVersion=10.0.19041.0, MaxVersionTested=10.0.22621.0, all 3 extensions present. 63.82 MB unsigned artifact at `artifacts/installer/msix/ToastNotification.Agent-0.2.1.0.msix`.
- [ ] **Keith**: Sign `ToastNotification.Agent-0.2.1.0.msix` via `.\scripts\sign-msix.ps1 -Path artifacts\installer\msix\ToastNotification.Agent-0.2.1.0.msix`
- [ ] **Keith**: Accept updated App Developer Agreement in Partner Center if prompted.
- [ ] **Keith**: Flight signed MSIX to Partner Center listing 9P5L0MRMFRRF (private/hidden). Verify package passes certification.
- [ ] **Diana**: Deliver curated tile assets (Square44x44, Square150x150, Wide310x150, StoreLogo) before any expansion beyond private/hidden flight.

### M0 D6 — DEFERRED
Deployment documentation deferred. Everything worth capturing is in CONTEXT.md already. Fold into M1 prep or skip — not on the critical path.

---

## M1 — Backend API — COMPLETE 2026-05-08

All 8 deliverables shipped. `src/ToastRevival.Api` — ASP.NET Core 8 / EF Core 8 / Npgsql / ASP.NET Identity / SignalR. Clean build, migration generated. INFO-M1-001 to INFO-M1-006 in FIX-LIST.md.

## M2 — Windows Agent Full Implementation (sliced 2026-05-09)

### M2.A — COMPLETE 2026-05-09
- [x] D1: SignalR client — connect to `/hubs/notifications`, auto-reconnect with `WithAutomaticReconnect` `[0,2,5,10,30]`s. `Microsoft.AspNetCore.SignalR.Client 8.0.15`.
- [x] D2: Toast rendering from backend payload — `ToastTemplateBuilder.BuildFromPayload` (extends, doesn't fork). Title, body, hero, logo, scenario, audio, action buttons all wired; click args carry `source=hub;notificationId=<guid>;action=<string>`.
- [x] D3: Device registration flow — `RegistrationService` posts to `/api/devices/register`, `ConfigStore` persists to `%LOCALAPPDATA%\Toast2IT\Toast Notification\config.json` via atomic temp+Move; bootstrap from env vars or `bootstrap.json` next to the exe. 30-min REST `/api/devices/ping` heartbeat.
- [x] D4: Payload HMAC verification — `Tenant.SigningKey` (32-byte random base64), server signs JSON byte sequence in `NotificationQueueService.BuildSignedPayload`, sends `(payloadJson, signature)` as separate SignalR args; agent verifies via `HmacVerifier.Verify` with `CryptographicOperations.FixedTimeEquals` constant-time compare.
- [x] D5: Interaction tracking — `ReportDelivery` after render via hub, `ReportInteraction` on `NotificationInvoked` via hub, plus REST `POST /api/notifications/{id}/interactions` for activation-handler exit path.
- [x] INFO-D5-001 (session-local mutex `Local\Toast2IT.ToastNotification.PrimaryWorker` — FIX-M2A-001 patched `Global\` → `Local\` pre-commit).
- [x] INFO-MSIX-004-D (activation-handler short-circuit before mutex/SignalR; `ActivationMode` runs Register() → wait for `NotificationInvoked` → POST REST → exit clean).

### M2.B — COMPLETE 2026-05-09
- [x] Backend: `GET /api/notifications/pending?since=<DateTime?>` device-JWT-authenticated, `device-per-hour` rate limit, returns `PendingNotificationItem[]` of (NotificationId, PayloadJson, Signature, CreatedAt) for the device's Pending deliveries; cap 100 per call.
- [x] Backend: `NotificationQueueService.RecoverOrphansAsync` runs once at `ExecuteAsync` startup. Notifications stuck in `Sending` past 5 minutes → Failed; pending deliveries STAY pending so catch-up can still deliver. Carl's overrule on the original FIX-LIST plan.
- [x] Backend: shared `NotificationPayloadBuilder.BuildSigned` helper — hub fanout + catch-up endpoint sign byte-identical UTF-8 sequences via the same code path.
- [x] Agent: on `_hub.Reconnected` and once after cold `StartAsync`, `RunCatchupAsync` GETs `/pending`, verifies + de-dups + renders + ReportDeliverys each item.
- [x] Agent: `MemoryCache<Guid,byte>` 1-hour sliding dedup. Short-circuits BOTH render and ReportDelivery; entry only set after `Show()` succeeds (render failures don't poison the cache).
- [x] FIX-M2B-001 patched pre-commit: agent `_lastCatchupSince` nullable + omit-on-first-call. Original `=DateTime.UtcNow` at ctor time would have excluded every pre-existing Pending delivery on first run.
- [x] Build clean (0 warn / 0 err); MSIX smoke check clean. No EF migration required.

### M2.C — COMPLETE 2026-05-08 (commit ecf79ce)
- [x] D7: System tray icon — WinForms NotifyIcon on STA thread; 5 states; Diana spec colors; 700ms amber pulse; context menu wired.
- [x] D9: WiX MSI properties CLIENTID + SERVERURL → bootstrap.json via SetupMode; WriteBootstrapJson/DeleteBootstrapJson CAs; version 0.4.0.0.
- [ ] D9: Silent install/uninstall verification + MSIX→MSI upgrade-path smoke check — **Keith to verify with lab machine on next MSI build**.

### M2.D — Auto-update (Velopack) — COMPLETE 2026-05-08
- [x] D8: Velopack 0.0.1298 integrated. UpdateService (registry toggle, TrySelfRedirect, 24h loop). Tray "Update Available" menu item. scripts/build-release.ps1. Feed URL placeholder (production setup at M9).

## Engineering - M2 follow-up (logged during M0 D2 — INFO-MSIX-004-A/B/C only remain)

- [ ] **INFO-MSIX-004-A/B/C** (DiagLog hygiene): Before any production launch, gate DiagLog behind a `--diag` flag or add log rotation. Currently writes append-only with no size cap and silently swallows all I/O exceptions (intentional for diagnostics phase, not for steady state). M2.D or M3 candidate.

## Deferred (later milestones)

- Backend API (M1).
- Full Windows agent with SignalR, HMAC, reconnect (M2).
- Content moderation, broadcast gates (M3).
- React admin dashboard (M4-M5).
- Curated per-template hero/logo imagery (Diana M4 deliverable, not blocking).
- Stripe billing/licensing (M6).
- Marketing site (M7).

## Deferred

- Backend API.
- React admin dashboard.
- SignalR agent communication.
- Billing/licensing.
- Marketing site.
