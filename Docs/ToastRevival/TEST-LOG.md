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
