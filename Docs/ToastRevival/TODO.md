# ToastRevival - Immediate Actions

## Current Reality

Project status: Pre-M0.

Only the code signing certificate renewal is known to be complete. Previous checked items represented intended planning status, not verified implementation status.

## Keith

- [x] Renew code signing certificate.
- [ ] Confirm certificate type, expiration date, subject, and whether signing requires a hardware token or cloud signing flow.
- [ ] Confirm Microsoft Partner Center access to app ID `9P5L0MRMFRRF`.
- [ ] Confirm whether a new private/hidden Store submission can be created.
- [ ] Confirm domain/DNS control for `toastnotification.com`.
- [ ] Confirm Stripe account status later, after the deployment spike is proven.

## Engineering

- [ ] Install .NET SDK on the development machine.
- [ ] Install/verify Windows App SDK and Visual Studio workloads required for WinUI 3/MSIX.
- [ ] Create repository baseline and push to GitHub.
- [ ] Create `M0A - Signed Toast Agent Spike`.
- [ ] Build a minimal Windows agent that displays one hardcoded toast.
- [ ] Package the minimal agent.
- [ ] Sign the package with the renewed certificate.
- [ ] Test install/run/toast behavior on a clean Windows 10/11 machine.
- [ ] Record evidence in `Docs/ToastRevival/EVIDENCE`.

## Deferred

- Backend API.
- React admin dashboard.
- SignalR agent communication.
- Billing/licensing.
- Marketing site.
