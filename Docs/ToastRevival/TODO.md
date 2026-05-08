# Toast Notification - Immediate Actions

## Current Reality

Project status: **M0A COMPLETE (2026-05-07). M0 D2 COMPLETE (2026-05-08). M0 D3 COMPLETE (2026-05-08).** Signed `ToastNotification.Agent-0.3.0.0.msi` installed cleanly on Win11 lab; `\Toast2IT\ToastNotificationAgentLogon` Scheduled Task created with State=Ready, MSFT_TaskLogonTrigger, MSFT_TaskExecAction; alert toast fired at next user logon. Per-machine MSI now uses logon-triggered Scheduled Task in BUILTIN\Users group context (`S-1-5-32-545` at LeastPrivilege) instead of M0A's all-users Startup-folder shortcut — better GPO/Intune story for the MSI/RMM channel. Next deliverable: **M0 D4** — GPO/domain/Intune/multi-user matrix with `FIX-MSIX-002` applied first.

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

## Engineering - M0 next deliverables

- [ ] M0 D4: Verify scheduled task survives standard enterprise GPOs, domain-joined machines, Intune-managed devices, multi-user scenarios. Apply `FIX-MSIX-002` (manifest MinVersion vs runtime gate divergence) first.
- [ ] M0 D5: Push skeleton app to existing Store listing 9P5L0MRMFRRF (private/hidden flight). Apply `FIX-MSIX-001` (bump `TargetPlatformVersion` to 10.0.22621.0) first. Add `<uap5:Extension Category="windows.startupTask">` to the MSIX manifest for parity with the MSI's logon-trigger behavior on the Store/MSIX channel.
- [ ] M0 D6: Document deployment findings + any fallback mechanisms needed.

## Engineering - M2 follow-up (logged during M0 D2)

- [ ] **INFO-MSIX-004-D**: Agent's `AgentOptions.Parse` silently ignores unknown args. When the user clicks a toast button on a deployed notification while no agent instance is running, the framework launches the exe with `----AppNotificationActivated:...` prepended; the agent currently falls through to default Plain template re-send instead of routing to a one-shot activation handler that exits cleanly. Detect the activation arg early in `Program.cs` and short-circuit before `Show()`.
- [ ] **INFO-MSIX-004-A/B/C** (DiagLog hygiene): Before any production launch, gate DiagLog behind a `--diag` flag or add log rotation. Currently writes append-only with no size cap and silently swallows all I/O exceptions (intentional for diagnostics phase, not for steady state).

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
