# ToastRevival - Immediate Actions

## Current Reality

Project status: M0A started.

The first local unpackaged agent spike has been created, built, and run. Packaging, signing, and clean-machine validation are still open.

## Keith

- [x] Renew code signing certificate.
- [ ] Confirm certificate type, expiration date, subject, and whether signing requires a hardware token or cloud signing flow.
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
- [ ] Package the minimal agent.
- [ ] Sign the package with the renewed certificate.
- [ ] Test install/run/toast behavior on a clean Windows 10/11 machine.
- [x] Record initial evidence in `Docs/ToastRevival/EVIDENCE`.

## Deferred

- Backend API.
- React admin dashboard.
- SignalR agent communication.
- Billing/licensing.
- Marketing site.
