# Toast Notification - Current Status

> Repo / project codename: **ToastRevival** (internal). Product / user-facing brand: **Toast Notification** (toastnotification.com).

Last updated: 2026-05-07

## Project State

**M0A: COMPLETE.** Signed MSI installs cleanly on a Win11 lab machine, agent runs in user context, toast survives login and reboot. Brand on every user-facing surface flipped from project codename to product name.

**M0 D2: BUILT + SIGNED + VERIFIED (2026-05-07). Install validation on Win11 lab is the only remaining step.** Single-Project MSIX produced from `src/ToastRevival.Agent` via WinAppSDK 1.7's vendor-native packaging. Signed with the Sectigo OV cert on the Thales SafeNet token via `signtool.exe` (DigiCert Cert Utility v2.x does not support MSIX — that was the M0 D2 surprise). `Get-AuthenticodeSignature` reports Status=Valid, Signer=`CN="Toast2IT, LLC", O="Toast2IT, LLC", S=Florida, C=US`, Issuer=Sectigo Public Code Signing CA R36, NotAfter 2027-04-15, DigiCert-timestamped. Output: `artifacts/installer/msix/ToastNotification.Agent-0.2.0.1.msix` (63.56 MB, SIGNED). See `EVIDENCE/2026-05-07-m0-d2-msix-build.md`, `EVIDENCE/2026-05-07-m0-d2-msix-publisher-fix.md`, `EVIDENCE/2026-05-07-m0-d2-msix-signed.md`. Tooling lessons codified in `CONTEXT.md` -> Code Signing section and `scripts/sign-msix.ps1`.

**Next:** install validation on Win11 lab machine (`Add-AppxPackage` or double-click). Win10 1809 verification deferred to M0 D4 (no 1809 lab on hand). After install verifies, M0 D3 (MSI scheduled-task variant) starts.

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
- `artifacts/installer/ToastNotification.Agent-0.2.0.0.msi` - rebranded user-facing surfaces. Same UpgradeCode -> MajorUpgrade replaces 0.1.0.0 cleanly. Awaiting Keith's re-sign before redeploy. Re-test on lab machine declined (rename does not change install / login / reboot mechanics).
- `artifacts/installer/msix/ToastNotification.Agent-0.2.0.1.msix` - 63.56 MB, **SIGNED** (Sectigo OV via Thales token, signtool.exe, DigiCert-timestamped). Status=Valid via `Get-AuthenticodeSignature`. Awaiting install validation on Win11 lab.

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

- M0 D2: Keith sign + install validation on Win11 lab and Win10 1809 (signing handoff documented in `EVIDENCE/2026-05-07-m0-d2-msix-build.md`).
- M0 D3: MSI wrapper with scheduled task in user context (replaces Startup-folder shortcut).
- M0 D4: GPO / domain / Intune / multi-user matrix.
- M0 D5: Store submission flight to 9P5L0MRMFRRF (apply `FIX-MSIX-001` first - bump `TargetPlatformVersion` to 10.0.22621.0).
- M0 D6: Document deployment findings + fallback mechanisms.
- M0+: GitHub Actions self-hosted runner on the build server (Codex owns).
- M4 design: curated per-template hero / logo imagery + curated MSIX tile imagery to replace generated brand placeholders. Open Diana question - `AppNotificationButtonStyle.Critical` vs `Success` for security-framed actions.
- Future templates: inline image, text input, selection input controls not yet exercised.
- M2: HMAC payload verification, SignalR reconnect, missed notification catch-up.
- No automated tests exist yet (first tests expected at M1 backend / M2 agent integration).
