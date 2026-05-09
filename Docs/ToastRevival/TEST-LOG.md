# ToastRevival - Test Log

## 2026-05-07

### Environment Checks

- `git --version`: passed.
- `dotnet --info`: initially showed SDK `10.0.203` only.
- Installed .NET SDK `8.0.420` with `winget`.
- `dotnet --list-sdks`: passed with `8.0.420` and `10.0.203`.
- `dotnet nuget list source`: initially showed no sources.
- Added repo-local `NuGet.config` for `https://api.nuget.org/v3/index.json`.

### Build Checks

- `dotnet restore ToastRevival.sln`: passed after adding `NuGet.config`.
- `dotnet build ToastRevival.sln --no-restore`: initially failed because Windows App SDK CLI build could not load `Microsoft.Build.Packaging.Pri.Tasks.ExpandPriContent`.
- Added `<EnableMsixTooling>true</EnableMsixTooling>` to `src/ToastRevival.Agent/ToastRevival.Agent.csproj`.
- `dotnet build ToastRevival.sln --no-restore`: passed with 0 warnings and 0 errors.

### Runtime Checks

- `.\scripts\run-agent-spike.ps1 -WaitSeconds 5`: passed.
- Console output reported `ToastRevival M0A notification sent.`
- `dotnet publish src\ToastRevival.Agent\ToastRevival.Agent.csproj -c Release -r win-x64 --self-contained false -o artifacts\ToastRevival.Agent\win-x64-framework-dependent`: passed.
- `.\artifacts\ToastRevival.Agent\win-x64-framework-dependent\ToastRevival.Agent.exe --wait 5`: passed and captured `Notification activated: action=acknowledge;source=m0a`.
- `dotnet publish src\ToastRevival.Agent\ToastRevival.Agent.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o artifacts\ToastRevival.Agent\win-x64-self-contained`: passed.
- `.\artifacts\ToastRevival.Agent\win-x64-self-contained\ToastRevival.Agent.exe --wait 5`: passed and captured `Notification activated: action=acknowledge;source=m0a`.

### Artifact Checks

- Framework-dependent artifact: 32 files, about 35.83 MB.
- Self-contained artifact: 448 files, about 160.62 MB.

### Signing/Packaging Tool Checks

- `signtool.exe`: not found on PATH.
- `makeappx.exe`: not found on PATH.
- Code-signing cert check: no matching cert with private key visible in `Cert:\CurrentUser\My` or `Cert:\LocalMachine\My`.

### Boundaries

- This confirms local unpackaged notification API execution, published executable execution, and notification activation callback handling from the development machine.
- This does not yet confirm packaging, signing, reboot survival, Store submission, Intune deployment, RMM deployment, or clean-machine behavior.

## 2026-05-07 (rich notification spike)

### Asset Generation

- `.\scripts\generate-toast-assets.ps1`: passed. Produced `src/ToastRevival.Agent/Assets/toast-hero.png` (364x180, 11,549 bytes), `toast-logo.png` (48x48, 296 bytes), `toast-inline.png` (200x120, 3,872 bytes).
- Image dimensions verified via `System.Drawing.Image.FromFile`.

### Build Checks

- `dotnet build C:\SOURCE\toast\ToastRevival.sln`: passed with 0 warnings and 0 errors after the `ToastTemplates.cs` add and the `Program.cs` refactor.

### Runtime Checks (`dotnet run`)

- `--template plain`: passed - `Scenario: Default, Sound: (none), Buttons: 1`.
- `--template announcement`: passed - `Scenario: Default, Sound: Default, Buttons: 1`.
- `--template alert`: passed - `Scenario: Urgent, Sound: Alarm, Buttons: 2`.
- `--template action`: passed - `Scenario: Default, Sound: Reminder, Buttons: 2`. Captured a late activation callback `action=acknowledge;source=m0a;template=Plain` from the prior `plain` run, which is the expected COM activation bridge behaviour.
- `--template reminder`: passed - `Scenario: Reminder, Sound: Reminder, Buttons: 1`.
- `--template celebration`: passed - `Scenario: Default, Sound: Default, Buttons: 1`.
- `--template maintenance`: passed - `Scenario: Default, Sound: Default, Buttons: 2`.

### Publish Checks

- `dotnet publish` framework-dependent: passed. Output `artifacts/ToastRevival.Agent/win-x64-framework-dependent`, 35 files, 35.86 MB. `Assets/` folder with all three PNGs shipped.
- `dotnet publish` self-contained (`-p:WindowsAppSDKSelfContained=true`): passed. Output `artifacts/ToastRevival.Agent/win-x64-self-contained`, 451 files, 160.65 MB. `Assets/` folder shipped.
- Published exe smoke test: `ToastRevival.Agent.exe --template alert --no-wait` reported the expected scenario, sound, and button count.

### Boundaries

- Templates were verified at the API level (build, send, output reporting). Visual rendering in Action Center has not been pixel-checked by Diana - that is a manual review pending Keith pulling open Action Center on the dev workstation.
- No packaged install was run. No signing was attempted. No reboot/login persistence was tested.
- `signtool.exe` and `makeappx.exe` are still not on PATH locally; the renewed token-backed OV cert is still not available to Windows signing tools.

## 2026-05-07 (M0A close - signed MSI install)

### Packaging

- WiX 5.0.2 installed via `dotnet tool install --global wix --version 5.*` (WiX 7 declined - requires paid OSMF EULA).
- `scripts/build-msi.ps1` produces a per-machine self-contained MSI in `artifacts/installer/`.
- 0.1.0.0 build: `ToastRevival.Agent-0.1.0.0.msi`, 50.60 MB. Pre-rebrand naming.
- 0.2.0.0 build: `ToastNotification.Agent-0.2.0.0.msi`, 50.60 MB. Rebranded user-facing surfaces. Same UpgradeCode for clean MajorUpgrade.

### Signing

- Keith signed `ToastRevival.Agent-0.1.0.0.msi` locally with the Thales hardware token.
- `Get-AuthenticodeSignature` verification: Status `Valid`, Signer `CN="Toast2IT, LLC", S=Florida, C=US`, Issuer `CN=Sectigo Public Code Signing CA R36`, NotAfter 2027-04-15, Thumbprint 19B07B46712C2D87FF6AA99842F7EF6B036FEDA7, Timestamp `CN=DigiCert SHA256 RSA4096 Timestamp Responder 2025 1`.
- The 0.2.0.0 rebrand MSI was rebuilt after install and is awaiting re-sign before redeploy.

### MSI Property Verification (0.2.0.0)

Read directly from the MSI Property table via WindowsInstaller COM:
- `ProductName` = `Toast Notification Agent`
- `Manufacturer` = `Toast2IT, LLC`
- `ProductVersion` = `0.2.0.0`
- `UpgradeCode` = `{A6F3D8F1-7B22-4E5A-9E3C-2A4F8B1C9D70}` (matches 0.1.0.0)

### Clean-Machine Install (Win11 lab)

- App installed - no issues reported.
- Shortly after reboot, toasts were seen. No issues reported.
- This closes M0A D6 (clean-machine install), D7 (user context), D8 (login + reboot persistence).

### Boundaries

- Lab machine SmartScreen behavior on the OV-signed MSI was not specifically captured (presumed clean from "no issues" but no screenshot filed).
- Re-test of the rebranded 0.2.0.0 MSI on the lab machine was declined - rename does not change install / login / reboot mechanics, only display strings.
- Domain-joined / GPO / Intune / multi-user scenarios are M0 D4, not M0A.

## 2026-05-07 (M0 D2 MSIX build)

### MSIX Tile Asset Generation

- `.\scripts\generate-msix-tile-assets.ps1`: passed. Produced `src/ToastRevival.Agent/Images/Square44x44Logo.png` (44x44, 240 B), `Square150x150Logo.png` (150x150, 914 B), `Wide310x150Logo.png` (310x150, 9,735 B), `StoreLogo.png` (50x50, 257 B). Image dimensions verified via `[System.Drawing.Image]::FromFile`.

### MSIX Build

- `.\scripts\build-msix.ps1 -SkipAssetGeneration`: initial run produced the .msix at `artifacts/installer/msix/ToastNotification.Agent-0.2.0.0.msix` (63.53 MB) BUT failed afterwards on missing `Properties/launchSettings.json` (required by `Microsoft.WindowsAppSDK.SingleProject.targets`).
- Added `src/ToastRevival.Agent/Properties/launchSettings.json` with `MsixPackage` profile.
- `.\scripts\build-msix.ps1 -SkipAssetGeneration` (re-run): passed. 0 errors, 1 warning (`mspdbcmf.exe` missing - symbols package skipped, benign; FIX-MSIX-003).

### Manifest Verification (read directly from the .msix)

Extracted `AppxManifest.xml` from the produced `.msix` via `System.IO.Compression.ZipFile.ExtractToDirectory`:
- `Identity.Name` = `Toast2IT.ToastNotification.Agent`
- `Identity.Publisher` = `CN="Toast2IT, LLC", S=Florida, C=US` (XML-escaped `&quot;` in source) - matches Sectigo OV cert subject exactly.
- `Identity.Version` = `0.2.0.0`
- `Identity.ProcessorArchitecture` = `x64`
- `Properties.DisplayName` = `Toast Notification`
- `Properties.PublisherDisplayName` = `Toast2IT, LLC`
- `Application.Executable` = `ToastNotification.Agent.exe`
- `Application.EntryPoint` = `Windows.FullTrustApplication`
- `VisualElements.DisplayName` = `Toast Notification`
- `VisualElements.Description` = `Managed Windows toast notifications for MSP-managed endpoints.`
- `VisualElements.BackgroundColor` = `#0F1117`
- All Logo paths point at `Images\*.png`.
- `<rescap:Capability Name="runFullTrust" />` present.
- Zero occurrences of "ToastRevival" in any user-visible field. M0A standing rule held.

### Package Contents Verification

- `ToastNotification.Agent.exe` present, 282,624 bytes.
- 458 files total; WinAppSDK 1.7 self-contained runtime DLLs bundled (`Microsoft.WindowsAppRuntime.dll` 1,890,360 bytes, `Microsoft.WindowsAppRuntime.Bootstrap.dll` 396,344 bytes).
- `Assets/` folder shipped with all three toast PNGs (`toast-hero.png` 11,818 B, `toast-logo.png` 296 B, `toast-inline.png` 3,872 B).
- `Images/` folder shipped with all four tile PNGs (matching the manifest Logo paths).

### Regression Check

- `dotnet build src\ToastRevival.Agent\ToastRevival.Agent.csproj -c Release` (default unpackaged path): passed. 0 warnings, 0 errors, 2.17s elapsed. Output landed in `bin/Release/net8.0-windows10.0.19041.0/ToastNotification.Agent.dll` (separate from MSIX path at `bin/x64/Release/.../win-x64/`).
- `scripts/build-msi.ps1` not re-run this session - file content unchanged, no edits affecting MSI publish path. Existing M0A MSI artifact unaffected.

### Boundaries

- The .msix produced is UNSIGNED. M0 D2 deliverable says "signed with OV cert, installs cleanly on Win10 1809+ / Win11" - signing and install validation are Keith handoff steps:
  - Sign: Thales hardware token + Sectigo OV cert (same flow used for M0A MSI). `signtool.exe sign /tr http://timestamp.digicert.com /td sha256 /fd sha256 /a /n "Toast2IT, LLC" "<path>\ToastNotification.Agent-0.2.0.0.msix"`
  - Verify: `Get-AuthenticodeSignature` should report Status=Valid, Signer="Toast2IT, LLC", Issuer=Sectigo Public Code Signing CA R36.
  - Install: Win11 lab machine confirmed for M0A. Win10 1809 is not on hand; that gap may need to be closed during M0 D4.
- `signtool.exe` and `makeappx.exe` still not on PATH locally - MSIX packaging used `MakeAppx` from the `Microsoft.Windows.SDK.BuildTools` transitive NuGet of WinAppSDK 1.7, which is invoked by the WinAppSDK packaging targets directly. signtool is still not present locally; Keith's signing workstation has it.

## 2026-05-07 (M0 D2 Publisher fix - 0x80091005)

### First sign attempt (0.2.0.0) failed

Keith opened `ToastNotification.Agent-0.2.0.0.msix` in DigiCert Certificate Utility, selected the Toast2IT, LLC cert (Sectigo OV via Thales token, cert chain validated OK), clicked Sign. Sign failed with:

```
The file C:\SOURCE\toast\artifacts\installer\msix\ToastNotification.Agent-0.2.0.0.msix could not be signed (0x80091005).
```

### Root cause - cert subject has more RDNs than the manifest Publisher had

Cert utility Details tab Subject field (the authoritative source for cert subject):

```
CN = Toast2IT, LLC
O  = Toast2IT, LLC
S  = Florida
C  = US
```

Manifest 0.2.0.0 Publisher (incomplete): `CN="Toast2IT, LLC", S=Florida, C=US` - missing `O=Toast2IT, LLC`.

### Fix - rebuild 0.2.0.1 with corrected Publisher

`src/ToastRevival.Agent/Package.appxmanifest` Identity element updated:

```xml
Publisher="CN=&quot;Toast2IT, LLC&quot;, O=&quot;Toast2IT, LLC&quot;, S=Florida, C=US"
Version="0.2.0.1"
```

Rebuilt via `.\scripts\build-msix.ps1 -Version 0.2.0.1 -SkipAssetGeneration`: passed, 0 errors, 1 mspdbcmf warning (cosmetic). Output: `artifacts/installer/msix/ToastNotification.Agent-0.2.0.1.msix` (63.53 MB, UNSIGNED).

Verified by extracting `AppxManifest.xml` from the new .msix:

```
Name      : Toast2IT.ToastNotification.Agent
Publisher : CN="Toast2IT, LLC", O="Toast2IT, LLC", S=Florida, C=US
Version   : 0.2.0.1
```

Old artifacts deleted: `ToastNotification.Agent-0.2.0.0.msix`, `ToastRevival.Agent_0.2.0.0_x64_Test/`. Only 0.2.0.1 outputs remain on disk.

### Lesson captured (also in EVIDENCE/2026-05-07-m0-d2-msix-publisher-fix.md and project context)

- MSI signing does NOT enforce Publisher-vs-cert match; MSIX signing DOES. The M0A MSI signed fine with a manifest-less Publisher; the MSIX rejected because the four-RDN cert subject only had three of those RDNs in the manifest.
- The team's prior memory string `CN="Toast2IT, LLC", S=Florida, C=US` came from a transcription of `Get-AuthenticodeSignature` output on the M0A MSI, which truncated/abbreviated the subject. That string is NOT authoritative for MSIX work.
- Authoritative cert subject sources: cert utility Details tab; `Get-ChildItem Cert:\...\My | Where Subject -like "*<co>*" | Select Subject`; or `(Get-AuthenticodeSignature <signed-msix>).SignerCertificate.Subject` (after a successful MSIX sign).
- Code Sweep Step 4 must enumerate every RDN in the cert subject and verify each appears in the manifest Publisher in the same order with the same quoting before any sign handoff.


## 2026-05-08 (M0 D4 — FIX-MSIX-002 + MSI 0.3.1.0 build)

### FIX-MSIX-002 Code Changes

- `Package.appxmanifest`: `TargetDeviceFamily MinVersion` 10.0.17763.0 → 10.0.19041.0; `Version` 0.2.0.3 → 0.2.1.0.
- `ToastRevival.Agent.csproj`: `<TargetPlatformMinVersion>` 10.0.17763.0 → 10.0.19041.0.
- `Program.cs`: removed "spike" wording from Win-version error message (user-visible cleanup).

### MSI 0.3.1.0 Build

- `.\scripts\build-msi.ps1 -Version 0.3.1.0`: passed with 0 errors, 1 cosmetic mspdbcmf warning (FIX-MSIX-003, pre-existing).
- Artifact: `artifacts/installer/ToastNotification.Agent-0.3.1.0.msi`, 50.61 MB.

### MSI 0.3.1.0 Property Verification

Read via WindowsInstaller COM:

```
ProductName    = Toast Notification Agent
Manufacturer   = Toast2IT, LLC
ProductVersion = 0.3.1.0
UpgradeCode    = {A6F3D8F1-7B22-4E5A-9E3C-2A4F8B1C9D70}
```

UpgradeCode matches 0.3.0.0 — `<MajorUpgrade>` will fire on install of 0.3.1.0 over 0.3.0.0.

### MSIX Smoke Check (Abish QA gate — Code Sweep standing rule)

**MSIX smoke check command (canonical, updated M0 D5):**
```powershell
dotnet build src\ToastRevival.Agent\ToastRevival.Agent.csproj -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:TargetPlatformVersion=10.0.22621.0
```
The `-p:TargetPlatformVersion=10.0.22621.0` flag is required. Without it the .NET SDK TFM (`net8.0-windows10.0.19041.0`) overwrites `TargetPlatformVersion` via a late `.targets` import that runs after PropertyGroup evaluation, silently capping `MaxVersionTested` at `10.0.19041.0`. Command-line flags have higher MSBuild precedence and win. Discovered M0 D5 (2026-05-08). See INFO-D5-003.

- M0 D4 smoke check (before D5 flag discovery): passed, 0 errors, 1 mspdbcmf warning (FIX-MSIX-003, pre-existing).
- AppxManifest.xml extracted and verified from produced .msix:
  - `Identity.Version = 0.2.1.0` ✓
  - `TargetDeviceFamily.MinVersion = 10.0.19041.0` ✓ (FIX-MSIX-002 applied correctly)
  - `TargetDeviceFamily.MaxVersionTested = 10.0.19041.0` — pre-existing at D4; **RESOLVED M0 D5** via `-p:TargetPlatformVersion=10.0.22621.0` in `build-msix.ps1`.

### Regression Check

- `dotnet publish` (unpackaged, default path) compiled cleanly as part of the MSI build: 0 errors.
- `scripts/build-msi.ps1` requires no changes for the version bump; `$Version` is a parameter.

### Boundaries

- MSI 0.3.1.0 is UNSIGNED. Keith signs before the major-upgrade test.
- MSIX 0.2.1.0 not built in this session (D4 focus is MSI/scheduled-task matrix; MSIX path is D5).
- D4 test matrix execution (Tests 1-5) is a Keith handoff.
  See `EVIDENCE/2026-05-08-m0-d4-matrix-results.md` for the full test procedure.
  See `scripts/verify-d4-matrix.ps1` for the pass/fail verification script.

## 2026-05-08 (M0 D3 build — MSI with Scheduled Task)

### Code Sweep — INFO findings (non-blocking)

- **INFO-D3-1** (architectural): Major upgrade from a future 0.3.x to 0.3.y will fire `UninstallScheduledTask` during the old product's `RemoveExistingProducts` (sequence 1401, internal uninstall script with `REMOVE="ALL"`) and `InstallScheduledTask` during the new product's install (sequence 4001). The `/F` flag on `schtasks /Create` is the safety net for the brief overlap window. Validate this end-to-end during M0 D4 GPO matrix when running multi-version upgrade scenarios.
- **INFO-D3-2** (path coupling): `installer/ToastNotificationLogon.xml` hard-codes `%ProgramFiles%\Toast Notification\ToastNotification.Agent.exe` literal. The WiX `INSTALLFOLDER` resolves to the same path under the default `<StandardDirectory Id="ProgramFiles64Folder">` placement. If a future installer change relocates `INSTALLFOLDER` (e.g., to `%LocalAppData%` for a per-user MSI variant), the XML would not auto-update. Captured as a CONTEXT.md standing rule under "Scheduled Task primitive (M0 D3 standing rules)."
- **INFO-D3-3** (artifact noise): WindowsAppSDK self-contained publish ships `RestartAgent.exe` and `createdump.exe` alongside the agent. Microsoft-signed binaries used by certain WindowsAppSDK runtime paths. Not consumed by current product behavior. Acceptable.

### Pre-install structural verification

- **MSI build**: `scripts\build-msi.ps1` (default `$Version=0.3.0.0`) produced `artifacts\installer\ToastNotification.Agent-0.3.0.0.msi` (50.61 MB) with 0 errors and the pre-existing FIX-MSIX-003 mspdbcmf warning (cosmetic, unrelated to D3).
- **XML schema parse** (System.Xml on `installer\ToastNotificationLogon.xml`): root `Task version=1.4`, URI `\Toast2IT\ToastNotificationAgentLogon`, `LogonTrigger` enabled, `GroupId=S-1-5-32-545`, `RunLevel=LeastPrivilege`, action `%ProgramFiles%\Toast Notification\ToastNotification.Agent.exe --template alert --no-wait`.
- **MSI Custom Action table**: `InstallScheduledTask` Type=3106 (deferred + non-impersonate + ExeCommand + Source=Directory, return=check) with target `"[System64Folder]schtasks.exe" /Create /TN "\Toast2IT\ToastNotificationAgentLogon" /XML "[INSTALLFOLDER]ToastNotificationLogon.xml" /F`. `UninstallScheduledTask` Type=3170 (same + return=ignore for idempotency) with target `"[System64Folder]schtasks.exe" /Delete /TN "\Toast2IT\ToastNotificationAgentLogon" /F`.
- **MSI InstallExecuteSequence**: `UninstallScheduledTask` at seq 3499 with `REMOVE="ALL"`; `RemoveFiles` at 3500; `InstallFiles` at 4000; `InstallScheduledTask` at 4001 with `NOT REMOVE`.
- **MSI Shortcut table**: only `StartMenuAgentShortcut` remains. `StartupShortcut` removed cleanly.
- **MSI File table / payload**: `ToastNotificationLogon.xml` mapped to component `LogonTaskXml`. Admin-install extract (`msiexec /a`) confirmed XML lands at `<INSTALLFOLDER>\ToastNotificationLogon.xml`, byte-identical to repo source (3,816 bytes, UTF-16 LE BOM `FF FE`).

### Pre-flight: schtasks /Create from unprivileged shell

- Command: `schtasks.exe /Create /TN \Toast2IT\_PreflightTest_M0D3 /XML installer\ToastNotificationLogon.xml /F`
- Result: `ERROR: Access is denied.` (exit 1)
- Interpretation: **expected and correct**. Creating a task with a group principal (`S-1-5-32-545` BUILTIN\Users) requires admin elevation. The error reached "Access is denied" rather than "task XML is not valid" — confirming Task Scheduler accepted the XML schema and only refused at the authorization step. The MSI deferred custom action runs as SYSTEM (`Impersonate="no"`), which has the privilege.

### Boundaries

- This confirms the MSI builds, the XML parses, the WiX table layout matches the design, and the XML payload survives the cab → admin-install extract round-trip byte-identical.
- This does NOT confirm signed install on a Win11 lab, that the scheduled task is actually created on the endpoint, that the task fires at logon, that the toast renders, or that uninstall removes the task. Those checks are Keith's hand-off step (signed install + `Get-ScheduledTask` + log-out/in verification + uninstall verification + idempotency check). Hand-off detail in `EVIDENCE/2026-05-08-m0-d3-msi-build-with-scheduled-task.md` § Hand-off.

## 2026-05-09 (M2.A — Agent ↔ Backend Pipeline + HMAC)

### Scope

M2 sliced. M2.A delivers D1 SignalR client + auto-reconnect, D2 toast rendering from backend payload, D3 first-run device registration + 30-min heartbeat ping, D4 HMAC payload verification, D5 ReportDelivery + ReportInteraction over the hub, INFO-D5-001 single-instance mutex guard, INFO-MSIX-004-D activation handler, plus a new device-JWT-authenticated REST `POST /api/notifications/{id}/interactions` endpoint for the activation-handler exit path. M2.B (D6 missed catch-up + recovery for orphan Sending), M2.C (D7 system tray + D9 MSI properties — Diana session), M2.D (D8 Velopack auto-update) deferred.

### Build Checks

- `dotnet build ToastRevival.sln`: passed with **0 warnings, 0 errors** after Code Sweep FIX-M2A-001 patch.
- MSIX smoke check (`dotnet build src\ToastRevival.Agent\ToastRevival.Agent.csproj -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:TargetPlatformVersion=10.0.22621.0`): passed with 1 warning (pre-existing FIX-MSIX-003 `mspdbcmf.exe` cosmetic). Produced `bin\Debug\net8.0-windows10.0.19041.0\win-x64\AppPackages\ToastRevival.Agent_0.2.1.0_x64_Debug_Test\ToastRevival.Agent_0.2.1.0_x64_Debug.msix`. All M0 D5 manifest standing checks intact (`windows.comServer` + `windows.toastNotificationActivation` + `windows.startupTask`, four-dash sentinel on `<com:ExeServer>`, CLSID byte-identical, MaxVersionTested=10.0.22621.0).
- `dotnet ef migrations add AddTenantSigningKey --project src/ToastRevival.Api --no-build`: passed (modulo pre-existing INFO-M1-001 DeviceGroupMember warning).

### Runtime Smoke Checks

- **DiagnosticMode regression** (`dotnet run --project src/ToastRevival.Agent --no-build -- --template plain --no-wait`): exit code 0, console output `Toast Notification sent. Template: Plain`. M0A argv-driven path preserved verbatim.
- **PrimaryMode unconfigured-exit** (`dotnet run --project src/ToastRevival.Agent --no-build` with no `TOAST_TENANT_ID`/`TOAST_SERVER_URL` env vars and no `bootstrap.json`): exit code 9, stderr `Toast Notification agent is not configured. Set TOAST_TENANT_ID and TOAST_SERVER_URL, or have the installer drop bootstrap.json next to the exe.` Clean failure mode.

### Code Sweep — Pre-Commit FIX

- **FIX-M2A-001** (BLOCKING → resolved before commit): `Program.cs:14` mutex name was `Global\Toast2IT.ToastNotification.PrimaryWorker`. The `Global\` prefix uses the kernel system-wide BaseNamedObjects namespace — meaning two interactive users on the same Win11 box (Fast User Switching, RDP, Terminal Services) would have user 2's agent collide with user 1's mutex and exit with code 5. **Regression of M0 D4 multi-user verification** (the matrix run on 2026-05-08 confirmed a second local user receives toasts via the BUILTIN\Users-group Scheduled Task). Patched to `Local\` prefix (per-session BaseNamedObjects) so each Windows session gets its own primary. Build re-verified clean post-patch.

### Code Sweep — INFO findings (non-blocking, deferred)

- **INFO-M2A-002** (security, defer to M3): DeviceConfig persisted as plaintext JSON at `%LOCALAPPDATA%\Toast2IT\Toast Notification\config.json` containing the device JWT and per-tenant HMAC signing key. Per-user LocalAppData ACLs gate ordinary access; admin-credential exfiltration is not gated. M3 hardening should wrap with DPAPI CurrentUser scope (`ProtectedData.Protect`).
- **INFO-M2A-003** (M2.B): `NotificationQueueService.ProcessAsync` writes `Status=Sending` then later `Status=Sent/Failed`. A crash between the two writes leaves the row stuck in `Sending`. Not a corruption bug (deliveries remain `Pending`), but a recovery concern. M2.B catch-up should add a startup recovery for orphan `Sending` rows (`UPDATE Notifications SET Status=Failed WHERE Status=Sending AND SentAt < now() - INTERVAL '5 minutes'`).
- **INFO-M2A-004** (M2.B): No agent-side de-dup on `notificationId`. SignalR redelivery on reconnect could double-render. M2.B catch-up should track recently-rendered IDs in a 1-hour rolling window.
- **INFO-M2A-005** (deploy doc, M9): Migration backfill SQL uses `gen_random_uuid()`, which is built-in to Postgres 13+. Document Postgres minimum-version in M9 deployment.

### Boundaries

- This confirms the wire-protocol + HMAC contract is structurally correct (server signs, agent verifies via constant-time compare, both sides agree on the JSON byte sequence to sign over because the server pre-serializes and ships the string + signature as separate SignalR args).
- This does NOT confirm end-to-end runtime: a real Postgres instance + running API + agent install on a signed Win11 lab + button-click → ReportInteraction round-trip. That hand-off is Keith's lab work, gated on MSI/MSIX rebuild + signing.
- Test coverage gap (INFO-M1-004) inherited from M1 — first tests at M8.

## 2026-05-09 (M2.B — Missed catch-up + Orphan recovery + Agent dedup)

### Scope

M2.B closes D6 from the M2 plan plus the two M2.A INFO items it carried. Three structural pieces:

1. **Backend `GET /api/notifications/pending?since=<DateTime?>`** — device-JWT-authenticated, `device-per-hour` rate-limit, returns `PendingNotificationItem[]` of `(NotificationId, PayloadJson, Signature, CreatedAt)` for the device's `Pending` deliveries; cap 100 per call; ordered `CreatedAt` asc.
2. **Backend `NotificationQueueService.RecoverOrphansAsync`** — runs once at `ExecuteAsync` startup before the channel loop. Sweeps `Notifications WHERE Status=Sending AND SentAt < now()-5min` to `Failed`; pending deliveries left intact for catch-up.
3. **Agent `RunCatchupAsync` + `_renderedCache: MemoryCache<Guid, byte>`** — fires on `_hub.Reconnected` and once after cold `StartAsync`; verifies HMAC + dedups + renders + ReportDeliverys each pending item. Dedup window 1-hour sliding, shared between hub-push and catch-up paths.

Plus a structural improvement: `NotificationPayloadBuilder.BuildSigned` extracted as the single source of truth for the wire shape + signature. The hub fanout and the catch-up endpoint now sign byte-identical UTF-8 sequences via the same code path — M2.A standing rule #14 made into a structural guarantee, not a convention.

### Build Checks

- `dotnet build ToastRevival.sln`: passed with **0 warnings, 0 errors** (post-FIX-M2B-001 patch verify).
- MSIX smoke check (`dotnet build src\ToastRevival.Agent\ToastRevival.Agent.csproj -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=false -p:AppxPackageSigningEnabled=false -p:TargetPlatformVersion=10.0.22621.0`): passed with 0 warnings, 0 errors. (We didn't generate the .msix package on this run since no manifest changes; smoke check confirms the `WindowsPackageType=MSIX` build path still resolves cleanly with the new `Microsoft.Extensions.Caching.Memory` package reference.)
- No EF migration generated — pure code milestone, no schema changes.

### Runtime Smoke Checks

- **DiagnosticMode regression** (`dotnet run --project src/ToastRevival.Agent --no-build -- --template plain --no-wait`): N/A this session (no agent-mode dispatch changes; the catch-up wiring is scoped to PrimaryMode `AgentHubClient`).
- End-to-end runtime confirmation (real Postgres + API + agent + reconnect → catch-up GET → render → ReportDelivery) requires a lab run with a signed agent build talking to a running backend. Same hand-off pattern as M2.A — out of scope for the structural session.

### Code Sweep — Pre-Commit FIX

- **FIX-M2B-001** (BLOCKING → resolved before commit): `AgentClient.cs::AgentHubClient._lastCatchupSince` was initialized to `DateTime.UtcNow` at ctor. First catch-up GET would send `since=<ctor_time>`; server filter `delivery.CreatedAt >= since` would have excluded EVERY pre-existing Pending delivery — exactly the case M2.B was shipping to fix (agent rebooted, Pending from before the reboot, reconnects). **The catch-up endpoint would have returned zero results in its primary scenario.** Patched: `_lastCatchupSince` is now nullable `DateTime?` (default null); first call omits the `since` query param entirely; subsequent calls send the captured `nextSince` from the previous call. Build re-verified clean post-patch. Side benefit: no time-zone Kind=Unspecified hazard against `timestamptz` columns.

### Code Sweep — INFO findings (non-blocking, deferred)

- **INFO-M2B-002** (M3 / M5): Pending endpoint hard cap of 100 items per call. >100 backlog drains across multiple reconnect cycles. Acceptable for now; explicit pagination can land at M3/M5.
- **INFO-M2B-003** (M3 / M5): No composite DB index on `NotificationDelivery (DeviceId, Status, CreatedAt)`. Catch-up query will scan once delivery volume grows. Add via EF model + migration when needed.
- **INFO-M2B-004** (M3): Agent dedup `MemoryCache` is unbounded (no `SizeLimit`). Acceptable at MVP scale (~100 bytes/entry); set `SizeLimit=50_000` + `Size=1` on entry options at M3.
- **INFO-M2B-005** (M3): Catch-up endpoint shares the `device-per-hour` (10/hr fixed) policy with `ReportInteraction`. Flaky-network reconnect storms could throttle catch-up. Fire-and-forget semantics mean a 429 just delays delivery to the next successful reconnect — acceptable for now. Consider a separate `device-catchup-per-hour` policy at e.g. 60/hr.

### New Standing Rules (Carl, M2.B)

1. **Orphan recovery semantic**: Sweep marks the notification `Failed` but leaves Pending deliveries untouched so the catch-up endpoint can still serve them. The original FIX-LIST plan ("deliveries to Failed accordingly") would have defeated catch-up entirely. State divergence (Failed notification with Delivered devices) is acceptable — the dashboard sees fanout-Failed while delivery counts trickle up.
2. **Single signed-payload source of truth**: any new path that emits a notification payload to an agent uses `NotificationPayloadBuilder.BuildSigned`. Never reimplement the JSON shape or the HMAC step inline — byte-deterministic equivalence between hub fanout and catch-up is structural, not coincidental.
3. **Catch-up `since` initialization rule**: any future catch-up endpoint that takes a `since` parameter must initialize the agent's tracking variable in a way that does NOT exclude pre-existing pending state on first run. Nullable + omit-on-first-call is the canonical pattern (FIX-M2B-001 lesson).

### Boundaries

- This confirms structural correctness: handler shape, auth boundary, dedup wiring, recovery semantic, byte-identical signing path between hub and catch-up.
- This does NOT confirm end-to-end runtime — pending live-server lab run.
- Test coverage gap (INFO-M1-004) inherited; first tests at M8.
