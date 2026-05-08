# Toast Notification - Current Status

> Repo / project codename: **ToastRevival** (internal). Product / user-facing brand: **Toast Notification** (toastnotification.com).

Last updated: 2026-05-08

## Project State

**M0A: COMPLETE (2026-05-07).** Signed MSI installs cleanly on a Win11 lab machine, agent runs in user context, toast survives login and reboot. Brand on every user-facing surface flipped from project codename to product name.

**M0 D2: COMPLETE (2026-05-08).** Signed MSIX (`ToastNotification.Agent-0.2.0.3.msix`, commit `6e3495c`) installs cleanly via Add-AppxPackage on Win11 lab; single visible toast fires from packaged Start menu launch; "Acknowledge" button click routes through `NotificationInvoked` handler with expected argument payload (`action=acknowledge;source=m0a;template=Plain`). FIX-MSIX-004 resolved: missing `Arguments="----AppNotificationActivated:"` on `<com:ExeServer>` was causing `AppNotificationManager.Default.Register()` to throw `COMException 0x80070490` (ERROR_NOT_FOUND). DiagLog file-based diagnostic logging added in 0.2.0.2 (commit `eca31dc`) was what made the failure point isolatable in a single install cycle. Toast Activator CLSID locked at `7FA7762F-41EC-4D72-9F06-58964AB36FEA` for the lifetime of this product.

See `EVIDENCE/2026-05-07-m0-d2-msix-build.md`, `-publisher-fix.md`, `-signed.md`, `2026-05-08-m0-d2-fix-msix-004-patch-build.md`, `2026-05-08-m0-d2-fix-msix-004-register-not-found.md`, `2026-05-08-m0-d2-toast-fires-packaged.md`.

**M0 D3: COMPLETE (2026-05-08).** Signed `ToastNotification.Agent-0.3.0.0.msi` installed cleanly on Win11 lab. `Get-ScheduledTask -TaskPath '\Toast2IT\' -TaskName 'ToastNotificationAgentLogon'` returned `State=Ready` with `MSFT_TaskLogonTrigger` and `MSFT_TaskExecAction`. Alert toast fired at next user logon — Critical-scenario banner with hero/logo/Acknowledge/Report buttons all rendered correctly. Per-machine MSI now uses a logon-triggered Scheduled Task in the BUILTIN\Users group context (`S-1-5-32-545` at `LeastPrivilege`) instead of M0A's all-users Startup-folder shortcut. Better GPO/Intune story for the MSI/RMM channel. Standing rules in CONTEXT.md "Scheduled Task primitive (M0 D3 standing rules)" production-validated. See `EVIDENCE/2026-05-08-m0-d3-msi-build-with-scheduled-task.md` and `EVIDENCE/2026-05-08-m0-d3-task-fires-at-logon.md`.

**Codex provisioned the build server pipeline (2026-05-07).** GitHub Actions self-hosted runner on 52.21.249.120 as Windows service `actions.runner.keithrlucier-toast.EC2AMAZ-A5EU435-toast-build`. Workflow `.github/workflows/agent-build.yml` (commit `9363764`) verified end-to-end on unsigned MSI builds. Codex's evidence note committed 2026-05-08 (commit `3c702fc`).

**M0 D4: COMPLETE (2026-05-08).** MSI 0.3.1.0 signed and installed on Win11 lab; task State=Ready; toast fires; second local user account also received toast (BUILTIN\Users confirmed); uninstall removed task cleanly. FIX-MSIX-002 applied (manifest MinVersion 17763→19041). GPO/Intune testing deferred: behavior documented in CONTEXT.md, carry to M8 beta. See `EVIDENCE/2026-05-08-m0-d4-matrix-results.md`.

**M0 D5: BUILD COMPLETE 2026-05-08 (pending Keith sign + Store submission).** MSIX `ToastNotification.Agent-0.2.1.0.msix` (63.82 MB, unsigned) built and verified. Three code changes shipped: (1) `uap5:StartupTask` extension added to manifest (MSIX/Store logon-launch parity with MSI Scheduled Task channel); (2) `TargetPlatformVersion=10.0.22621.0` baked into `build-msix.ps1` via command-line flag (FIX-MSIX-001 resolved — csproj PropertyGroup approach silently failed due to TFM override in late .targets import); (3) CONTEXT.md standing rules updated with discovery. Produced manifest verified: `MinVersion=10.0.19041.0`, `MaxVersionTested=10.0.22621.0`, all three extensions present. **Keith handoff:** sign the MSIX with Thales token, then flight to Partner Center listing 9P5L0MRMFRRF. Accept Developer Agreement if prompted.

**M1: COMPLETE 2026-05-08.** `src/ToastRevival.Api` — ASP.NET Core 8 / EF Core 8 / Npgsql / ASP.NET Identity / SignalR. All 8 deliverables shipped: scaffolding, multi-tenant data model with global query filters, JWT auth with user+device tokens, device registration API, notification send API with queue background service, SignalR hub with tenant/device groups, rate limiting, audit logging. `InitialCreate` migration generated. Build: 0 warnings, 0 errors. INFO-M1-001 through INFO-M1-006 logged (see FIX-LIST.md).

**Next: M2 — Windows Agent Full Implementation.** SignalR client integration, device registration flow, HMAC payload verification, interaction tracking, system tray icon, auto-update.

Codex is also working on this project. Coordinate before running commands on the server during active installer windows.

## M0A Deliverables - All Closed

- D1: Dev machine has .NET SDK 8.0.420 + Windows App SDK 1.7.250310001 + WiX 5.0.2 + Thales hardware token signing.
- D2: `src/ToastRevival.Agent` agent project on `net8.0-windows10.0.19041.0`.
- D3: Toast displayed - 7 templates (plain + 6 rich) wired through `AppNotificationBuilder`, hero/logo/scenario/audio/multi-button.
- D4: MSI built locally via WiX 5 (`installer/ToastRevival.Agent.Setup.wxs`, `scripts/build-msi.ps1`).
- D5: Signed with Sectigo OV cert via Thales hardware token. Signature valid, DigiCert-timestamped. Cert NotAfter 2027-04-15.
- D6: Installed on clean Win11 lab machine - no issues.
- D7: Agent runs in logged-in user context via Startup-folder shortcut (per-machine MSI, all-users startup).
- D8: Toast still fires after reboot (lab machine confirmed).
- D9: EVIDENCE entries: `2026-05-07-m0a-local-agent-spike.md`, `2026-05-07-m0a-rich-notification-spike.md`, `2026-05-07-m0a-close-signed-msi-install.md`, `2026-05-07-build-server-bootstrap.md`.

## Current Build Outputs

- `artifacts/installer/ToastRevival.Agent-0.1.0.0.msi` - signed, installed on lab machine. Pre-rebrand.
- `artifacts/installer/ToastNotification.Agent-0.2.0.0.msi` - rebranded user-facing surfaces. Same UpgradeCode -> MajorUpgrade replaces 0.1.0.0 cleanly. Awaiting re-sign before redeploy if needed.
- `artifacts/installer/msix/ToastNotification.Agent-0.2.0.1.msix` - signed; install validation revealed FIX-MSIX-004 (Register() ERROR_NOT_FOUND). Superseded.
- `artifacts/installer/msix/ToastNotification.Agent-0.2.0.2.msix` - DiagLog scaffolding + initial COM activator declarations. Superseded by 0.2.0.3 (Register still threw without Arguments token).
- **`artifacts/installer/msix/ToastNotification.Agent-0.2.0.3.msix` - 63.53 MB. Signed by Keith 2026-05-08, installed cleanly on Win11 lab; visible toast fires; NotificationInvoked routes cleanly. Current canonical M0 D2 build.**
- **`artifacts/installer/ToastNotification.Agent-0.3.0.0.msi` - 50.61 MB. Signed by Keith 2026-05-08, installed cleanly on Win11 lab; scheduled task created (State=Ready); toast fires at logon. Canonical M0 D3 build.**
- **`artifacts/installer/msix/ToastNotification.Agent-0.2.1.0.msix` - 63.82 MB. UNSIGNED. M0 D5 build. Three extensions: `windows.comServer`, `windows.toastNotificationActivation`, `windows.startupTask`. Manifest: MinVersion=10.0.19041.0, MaxVersionTested=10.0.22621.0. Awaiting Keith sign + Store flight.**

## Server (52.21.249.120)

See `CONTEXT.md` -> Server Infrastructure for full details.

- Windows Server 2022 Datacenter, hostname `EC2AMAZ-A5EU435`.
- SSH port 22 and RDP port 3389 reachable.
- Key-based SSH configured from this workstation.
- .NET SDK `8.0.420` and Git `2.53.0.windows.2` installed.
- Repo cloned to `C:\toast`, remote: `https://github.com/keithrlucier/toast`.
- Visual Studio Build Tools / Windows SDK installation still needs verification (Codex owns).
- `signtool.exe` and `makeappx.exe` not available on the server yet (Codex owns).
- WinRM blocked; use SSH.
- PATH is not reliable in SSH sessions; use full binary paths.

## Local Environment Notes

- Git, .NET SDK `8.0.420` (pinned via `global.json`), Windows App SDK 1.7.250310001 via NuGet, WiX 5.0.2 (`dotnet tool install --global wix --version 5.*`).
- Repo-local `NuGet.config` for nuget.org.
- Windows App SDK CLI builds require `<EnableMsixTooling>true</EnableMsixTooling>`.
- MSIX build wrapped in `scripts/build-msix.ps1`. Smoke check command (must include TargetPlatformVersion flag): `dotnet build src\ToastRevival.Agent\ToastRevival.Agent.csproj -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:TargetPlatformVersion=10.0.22621.0`. Without the flag, the .NET SDK TFM late-import silently caps MaxVersionTested at 10.0.19041.0. See CONTEXT.md standing rule #4.
- Code-signing flow: Thales hardware token + Sectigo OV cert. Keith handles signing for both MSI and MSIX. **The OV cert subject is `CN="Toast2IT, LLC", O="Toast2IT, LLC", S=Florida, C=US`** (CN, O, S, C - four RDNs, both CN and O contain a comma so both need quotes). `Package.Identity.Publisher` MUST match this string exactly across all four RDNs or signing fails (0x80091005 in DigiCert Utility / 0x800B0109 in signtool) and any installed signed MSIX rejects with 0x800B0109. The authoritative reference is the cert utility's Details -> Subject field (NOT `Get-AuthenticodeSignature` on a previously-signed MSI - that display can truncate the subject).

## Open Items (carried into M0 or later)

- M0 D2 (Win10 1809 install validation): no Win10 1809 lab machine on hand; deferred to M0 D4 GPO matrix.
- M0 D4: GPO / domain / Intune / multi-user matrix. Apply `FIX-MSIX-002` first. Roll uninstall idempotency + 0.3.x → 0.3.y major-upgrade race validation into this matrix.
- M0 D5: Build complete; **Keith signs + flights to 9P5L0MRMFRRF**. Diana curated tile assets needed before public expansion.
- M0 D6: Document deployment findings + fallback mechanisms. Diana tile assets delivery.
- M2 follow-up: detect `----AppNotificationActivated:` arg in `AgentOptions.Parse` and route to one-shot activation handler instead of falling through to a default Plain template re-send (INFO-MSIX-004-D).
- M2 follow-up: HMAC payload verification, SignalR reconnect, missed notification catch-up.
- M1/M2 hygiene: gate DiagLog behind `--diag` flag or add rotation before launch (INFO-MSIX-004-A/B/C).
- M4 design: curated per-template hero / logo imagery + curated MSIX tile imagery. Open Diana question - `AppNotificationButtonStyle.Critical` vs `Success` for security-framed actions.
- Future templates: inline image, text input, selection input controls not yet exercised.
- No automated tests exist yet (first tests expected at M1 backend / M2 agent integration).
