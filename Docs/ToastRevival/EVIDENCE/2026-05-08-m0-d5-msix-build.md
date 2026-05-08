# Evidence: M0 D5 — MSIX 0.2.1.0 Build

**Date:** 2026-05-08
**Milestone:** M0 D5
**Build:** `ToastNotification.Agent-0.2.1.0.msix` (63.82 MB, unsigned)
**Commit:** pending sign + flight

---

## Changes Shipped

### 1. `uap5:StartupTask` extension — `src/ToastRevival.Agent/Package.appxmanifest`

Added `<uap5:Extension Category="windows.startupTask">` inside `<Application><Extensions>`, enabling the MSIX/Store channel to auto-launch the agent at user logon. Parity with the MSI channel's Scheduled Task (`\Toast2IT\ToastNotificationAgentLogon`).

- `xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"` added to `<Package>`
- `uap5` added to `IgnorableNamespaces`
- Extension body: `<uap5:StartupTask TaskId="ToastNotificationAgent" Enabled="true" DisplayName="Toast Notification" />`
- Task visible in Windows Settings > Apps > Startup; users can disable it there.

### 2. FIX-MSIX-001 — `scripts/build-msix.ps1`

**Root cause discovered:** Setting `<TargetPlatformVersion>` in a conditional csproj PropertyGroup does NOT produce the expected `MaxVersionTested` value in the produced manifest. The .NET SDK sets `TargetPlatformVersion` from the TFM (`net8.0-windows10.0.19041.0`) in a late `.targets` import that runs AFTER PropertyGroup evaluation, silently overriding the csproj value.

**Fix:** Added `-p:TargetPlatformVersion=10.0.22621.0` to the `dotnet build` command in `scripts/build-msix.ps1`. Command-line flags have higher MSBuild precedence than any imported `.targets` and win reliably.

A WHY comment was added in the script to document this behavior for future maintainers.

---

## Produced Manifest Verification

Extracted `AppxManifest.xml` from `artifacts/installer/msix/ToastNotification.Agent-0.2.1.0.msix`:

```xml
<TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.22621.0" />
```

Extensions present:
- `Category="windows.comServer"` ✓ (CLSID 7FA7762F-41EC-4D72-9F06-58964AB36FEA)
- `Category="windows.toastNotificationActivation"` ✓ (same CLSID, four-dash sentinel intact)
- `Category="windows.startupTask"` ✓ (NEW — TaskId=ToastNotificationAgent, Enabled=true)

Identity: `Name="Toast2IT.ToastNotification.Agent"`, `Version="0.2.1.0"`, `ProcessorArchitecture="x64"` ✓
Publisher: `CN="Toast2IT, LLC", O="Toast2IT, LLC", S=Florida, C=US` ✓

Build output: 0 errors, 1 cosmetic mspdbcmf warning (FIX-MSIX-003, pre-existing).

---

## Code Sweep Findings (Abish)

- **SHIP WITH NOTES**
- INFO-D5-001 (M2): No "already running" mutex guard in Program.cs. Multiple startup paths can fire multiple toasts per session.
- INFO-D5-002 (low/doc): MSI + MSIX simultaneous install fires two toasts per logon. Document in M0 D6 as mutually exclusive deployment channels.
- INFO-D5-003 (methodology): **CLOSED** — CONTEXT.md, TEST-LOG.md, and build-msix.ps1 all updated with correct smoke check command including `-p:TargetPlatformVersion=10.0.22621.0`.

---

## Keith Handoff

1. **Sign:** Plug in Thales token, unlock via SafeNet tray app.
   ```powershell
   .\scripts\sign-msix.ps1 -Path artifacts\installer\msix\ToastNotification.Agent-0.2.1.0.msix
   ```
   - SafeNet PIN dialog pops when signtool reaches for private key
   - Verify `Status = Valid` in script output

2. **Accept Developer Agreement:** Open Partner Center, check for updated App Developer Agreement prompt. Accept before submitting.

3. **Flight to Store:**
   - Go to [partner.microsoft.com/dashboard](https://partner.microsoft.com/dashboard)
   - Navigate to App ID `9P5L0MRMFRRF`
   - Start a new submission
   - Upload `ToastNotification.Agent-0.2.1.0.msix` (signed)
   - Set visibility to **Private** / hidden flight
   - Submit — certification should pass on a "Hello World" level app with no Store-registered capabilities beyond `runFullTrust`

4. **Confirm:** Screenshot or note that certification passed (or any cert failures for the team to address).

---

## Tile Assets Note

Current tile images (`Images/Square44x44Logo.png`, `Square150x150Logo.png`, `Wide310x150Logo.png`, `StoreLogo.png`) are procedural brand-teal placeholders from M0 D2. They ship with this private hidden flight for pipeline validation only. **Diana delivers curated tile assets before any expansion to public listing.** This constraint is in writing in TODO.md.
