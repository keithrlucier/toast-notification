# Toast Notification - Current Status

> Repo / project codename: **ToastRevival** (internal). Product / user-facing brand: **Toast Notification** (toastnotification.com).

Last updated: 2026-05-08

## Project State

**M0A: COMPLETE (2026-05-07).** Signed MSI installs cleanly on a Win11 lab machine, agent runs in user context, toast survives login and reboot. Brand on every user-facing surface flipped from project codename to product name.

**M0 D2: COMPLETE (2026-05-08).** Signed MSIX (`ToastNotification.Agent-0.2.0.3.msix`, commit `6e3495c`) installs cleanly via Add-AppxPackage on Win11 lab; single visible toast fires from packaged Start menu launch; "Acknowledge" button click routes through `NotificationInvoked` handler with expected argument payload (`action=acknowledge;source=m0a;template=Plain`). FIX-MSIX-004 resolved: missing `Arguments="----AppNotificationActivated:"` on `<com:ExeServer>` was causing `AppNotificationManager.Default.Register()` to throw `COMException 0x80070490` (ERROR_NOT_FOUND). DiagLog file-based diagnostic logging added in 0.2.0.2 (commit `eca31dc`) was what made the failure point isolatable in a single install cycle. Toast Activator CLSID locked at `7FA7762F-41EC-4D72-9F06-58964AB36FEA` for the lifetime of this product.

See `EVIDENCE/2026-05-07-m0-d2-msix-build.md`, `-publisher-fix.md`, `-signed.md`, `2026-05-08-m0-d2-fix-msix-004-patch-build.md`, `2026-05-08-m0-d2-fix-msix-004-register-not-found.md`, `2026-05-08-m0-d2-toast-fires-packaged.md`.

**M0 D3: BUILT, AWAITING SIGN + LAB VERIFY (2026-05-08).** `ToastNotification.Agent-0.3.0.0.msi` built locally — per-machine MSI now registers `\Toast2IT\ToastNotificationAgentLogon` Scheduled Task at install (replaces M0A's all-users Startup-folder shortcut). Task is logon-triggered with BUILTIN\Users group principal (`S-1-5-32-545`) at `LeastPrivilege`, action runs `%ProgramFiles%\Toast Notification\ToastNotification.Agent.exe --template alert --no-wait`. Better GPO and Intune compatibility for the MSI/RMM deployment channel. Pre-install verification clean: XML parses, MSI Custom Action table correct (deferred + non-impersonate, sequenced after InstallFiles / before RemoveFiles), payload byte-identical from repo through cab. Pre-flight `schtasks /Create` from unprivileged shell rejected with "Access is denied" — confirming the schema parsed and the privilege model is enforced; MSI deferred CA running as SYSTEM during install will succeed. See `EVIDENCE/2026-05-08-m0-d3-msi-build-with-scheduled-task.md`. Hand-off to Keith for sign + lab install + Get-ScheduledTask verification.

**Codex provisioned the build server pipeline (2026-05-07).** GitHub Actions self-hosted runner on 52.21.249.120 as Windows service `actions.runner.keithrlucier-toast.EC2AMAZ-A5EU435-toast-build`. Workflow `.github/workflows/agent-build.yml` (commit `9363764`) verified end-to-end on unsigned MSI builds. Codex's evidence note committed 2026-05-08 (commit `3c702fc`).

**Next:** Keith signs `ToastNotification.Agent-0.3.0.0.msi`, installs on Win11 lab, verifies the scheduled task creates and fires at logon, captures evidence; then **M0 D4** — GPO/domain/Intune/multi-user matrix with `FIX-MSIX-002` applied first.

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
- **`artifacts/installer/ToastNotification.Agent-0.3.0.0.msi` - 50.61 MB. Built 2026-05-08 with Scheduled Task primitive replacing M0A's Startup-folder shortcut. Awaiting Keith sign + lab install verification. Current candidate M0 D3 build.**

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
- MSIX build is single-csproj conditional: `dotnet build -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false` -> wrapped in `scripts/build-msix.ps1`. The Single-Project MSIX targets need `Properties/launchSettings.json` with a `MsixPackage` profile present (added 2026-05-07).
- Code-signing flow: Thales hardware token + Sectigo OV cert. Keith handles signing for both MSI and MSIX. **The OV cert subject is `CN="Toast2IT, LLC", O="Toast2IT, LLC", S=Florida, C=US`** (CN, O, S, C - four RDNs, both CN and O contain a comma so both need quotes). `Package.Identity.Publisher` MUST match this string exactly across all four RDNs or signing fails (0x80091005 in DigiCert Utility / 0x800B0109 in signtool) and any installed signed MSIX rejects with 0x800B0109. The authoritative reference is the cert utility's Details -> Subject field (NOT `Get-AuthenticodeSignature` on a previously-signed MSI - that display can truncate the subject).

## Open Items (carried into M0 or later)

- M0 D2 (Win10 1809 install validation): no Win10 1809 lab machine on hand; deferred to M0 D4 GPO matrix.
- M0 D3 close (signed install + Get-ScheduledTask verification + toast-fires-at-logon evidence): Keith-side; pending hand-off.
- M0 D4: GPO / domain / Intune / multi-user matrix. Apply `FIX-MSIX-002` first.
- M0 D5: Store submission flight to 9P5L0MRMFRRF (apply `FIX-MSIX-001` first).
- M0 D6: Document deployment findings + fallback mechanisms.
- M2 follow-up: detect `----AppNotificationActivated:` arg in `AgentOptions.Parse` and route to one-shot activation handler instead of falling through to a default Plain template re-send (INFO-MSIX-004-D).
- M2 follow-up: HMAC payload verification, SignalR reconnect, missed notification catch-up.
- M1/M2 hygiene: gate DiagLog behind `--diag` flag or add rotation before launch (INFO-MSIX-004-A/B/C).
- M4 design: curated per-template hero / logo imagery + curated MSIX tile imagery. Open Diana question - `AppNotificationButtonStyle.Critical` vs `Success` for security-framed actions.
- Future templates: inline image, text input, selection input controls not yet exercised.
- No automated tests exist yet (first tests expected at M1 backend / M2 agent integration).
