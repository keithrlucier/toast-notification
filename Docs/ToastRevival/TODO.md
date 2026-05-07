# ToastRevival - Immediate Actions

## Current Reality

Project status: M0A started.

The first local unpackaged agent spike has been created, built, and run. Packaging, signing, and clean-machine validation are still open.

## Keith

- [x] Renew code signing certificate.
- [x] Confirm signing requires a hardware token.
- [ ] Confirm certificate type, expiration date, subject, token vendor/provider, and whether unattended signing is possible.
- [ ] Confirm Microsoft Partner Center access to app ID `9P5L0MRMFRRF`.
- [ ] Confirm whether a new private/hidden Store submission can be created.
- [ ] Confirm domain/DNS control for `toastnotification.com`.
- [ ] Confirm Stripe account status later, after the deployment spike is proven.

## Engineering

- [x] Install .NET SDK on the development machine.
- [ ] Install/verify Windows App SDK and Visual Studio workloads required for WinUI 3/MSIX.
- [x] Create repository baseline and push to GitHub.
- [x] Create `M0A - Signed Toast Agent Spike`.
- [x] Build a minimal Windows agent that sends one hardcoded local app notification.
- [x] Produce local Release publish artifacts for the minimal agent.
- [x] Build a rich local notification spike with hero image, logo override, action buttons, and audio. Six Diana templates covered. See `EVIDENCE/2026-05-07-m0a-rich-notification-spike.md`.
- [ ] Package the minimal agent.
- [ ] Sign the package with the renewed certificate.
- [ ] Test install/run/toast behavior on a clean Windows 10/11 machine.
- [ ] Replace generated brand placeholder PNGs with curated per-template images (Diana M4 deliverable, not blocking M0A signing).
- [x] Record initial evidence in `Docs/ToastRevival/EVIDENCE`.

## Deferred

- Backend API.
- React admin dashboard.
- SignalR agent communication.
- Billing/licensing.
- Marketing site.
