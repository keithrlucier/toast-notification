# Toast Notification - Immediate Actions

## Current Reality

Project status: **M0A COMPLETE (2026-05-07).** Signed MSI installed and verified on Win11 lab machine. Login + reboot toast persistence confirmed. User-facing brand corrected from project codename to product name. Next milestone: **M0 - Foundation & Deployment Validation**, starting with M0 D2 (MSIX).

## Keith

- [x] Renew code signing certificate (Sectigo OV, NotAfter 2027-04-15).
- [x] Confirm signing requires a hardware token (Thales).
- [x] Sign first MSI (ToastRevival.Agent-0.1.0.0.msi).
- [ ] Re-sign the rebranded MSI when needed (`artifacts/installer/ToastNotification.Agent-0.2.0.0.msi`).
- [ ] Sign the M0 D2 MSIX (`artifacts/installer/msix/ToastNotification.Agent-0.2.0.0.msix`) with Thales+Sectigo, then validate clean install on the Win11 lab machine.
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

## Engineering - M0 next session (Foundation & Deployment Validation)

- [x] **M0 D2 build:** MSIX package built via WinAppSDK 1.7 SingleProject path, identity surface audited, `Publisher` matches cert subject exactly. Output: `artifacts/installer/msix/ToastNotification.Agent-0.2.0.0.msix` (UNSIGNED).
- [ ] **M0 D2 sign + install:** Keith signs with Thales+Sectigo, installs on Win11 lab and (ideally) Win10 1809. Same signtool flow as M0A MSI.
- [ ] M0 D3: MSI wrapper that creates a Scheduled Task in user context (replacing the current Startup-folder shortcut for better GPO/Intune compatibility).
- [ ] M0 D4: Verify scheduled task survives standard enterprise GPOs, domain-joined machines, Intune-managed devices, multi-user scenarios. Apply `FIX-MSIX-002` (manifest MinVersion vs runtime gate divergence) first.
- [ ] M0 D5: Push skeleton app to existing Store listing 9P5L0MRMFRRF (private/hidden flight). Apply `FIX-MSIX-001` (bump `TargetPlatformVersion` to 10.0.22621.0) first.
- [ ] M0 D6: Document deployment findings + any fallback mechanisms needed.

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
