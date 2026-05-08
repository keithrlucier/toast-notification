# ToastRevival - Fix List

## Open Issues

### FIX-MSIX-001 (low) - TargetPlatformVersion caps MaxVersionTested at Win10 2004

**Filed:** 2026-05-07 (M0 D2 Code Sweep)
**Surface:** `src/ToastRevival.Agent/ToastRevival.Agent.csproj` conditional MSIX PropertyGroup
**Issue:** `<TargetPlatformVersion>10.0.19041.0</TargetPlatformVersion>` propagates into the generated manifest as `TargetDeviceFamily MaxVersionTested="10.0.19041.0"`. Sideload install is unaffected, but Microsoft Store flighting (M0 D5) will want a current Win11 build claim (10.0.22621.0 or higher).
**Fix when M0 D5 starts:** Bump `<TargetPlatformVersion>` to `10.0.22621.0` in the conditional MSIX PropertyGroup. Re-test sideload install on Win11 lab machine. Re-sign.
**Blocking:** No — only blocks Store flight, not sideload.

### FIX-MSIX-002 (low) - Manifest MinVersion vs. runtime gate divergence

**Filed:** 2026-05-07 (M0 D2 Code Sweep)
**Surface:** `src/ToastRevival.Agent/Package.appxmanifest` `TargetDeviceFamily MinVersion="10.0.17763.0"` (Win10 1809) vs. `src/ToastRevival.Agent/Program.cs` `OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)` (Win10 2004 / build 19041).
**Issue:** A Win10 1809 install will succeed via MSIX MinVersion check but the agent will exit 2 at runtime with the message "Toast Notification agent requires Windows 10 2004 / build 19041 or later for this spike." Confusing UX — install looks healthy, runtime fails silently.
**Fix when M0 D4 starts:** Either (a) relax the runtime check to 17763 if AppNotificationManager actually works there, or (b) bump `TargetDeviceFamily MinVersion` to `10.0.19041.0` so the install fails up front on incompatible Windows builds. Option (b) is the safer default; the M0A spike already runs on 19041.
**Blocking:** No — milestone target is 1809+ but lab machine is Win11; Win10 1809 verification is the M0 D4 GPO matrix work.

### FIX-MSIX-004 (medium) - Packaged MSIX install does not fire toasts - 0.2.0.3 PATCH BUILT 2026-05-08, AWAITING KEITH SIGN+INSTALL

**Update 2026-05-08 (post-0.2.0.2 install attempt):** DiagLog from 0.2.0.2 install captured `AppNotificationManager.Default.Register()` throwing `COMException 0x80070490` (`HRESULT_FROM_WIN32(ERROR_NOT_FOUND)`) before `Show()` was reached. Original FIX-MSIX-004 hypothesis (Show silently no-ops) was wrong; Register() itself was the failure point. **Root cause: missing `Arguments="----AppNotificationActivated:"` on `<com:ExeServer>`.** Microsoft's packaged-WinAppSDK quickstart sample includes the four-dash sentinel; the framework uses it as the activator surface marker, and Register()'s COM class registration lookup fails ERROR_NOT_FOUND without it. Patched in 0.2.0.3. CONTEXT.md "Toast Activator Class ID" updated with the standing rule. See `EVIDENCE/2026-05-08-m0-d2-fix-msix-004-register-not-found.md`.



**Filed:** 2026-05-07 (M0 D2 install validation)
**Patch built:** 2026-05-08 (`ToastNotification.Agent-0.2.0.2.msix`, unsigned)
**Surface:** Win11 lab machine, signed `ToastNotification.Agent-0.2.0.1.msix` installed via Add-AppxPackage.

**Symptom (0.2.0.1):** Console window flashes when the package launches via Start menu tile or `shell:appsfolder\<AUMID>`, but no toast banner appears, no entry lands in Action Center (Win+N), and Settings -> System -> Notifications -> Toast Notification shows no Notification history. The same agent code shipped via the M0A MSI fires toasts reliably (Startup-folder shortcut, unpackaged path).

**Hypothesis:** `Package.appxmanifest` was missing the COM activator declarations that the WinAppSDK packaged toast path requires. For UNPACKAGED apps, `AppNotificationManager.Default.Register()` auto-injects a CLSID into `HKCU\SOFTWARE\Classes\CLSID\...` so the toast pipe is wired implicitly (that's why M0A MSI works). For PACKAGED apps, the framework looks up the activator CLSID from the manifest; without those declarations, `Register()` returns success at the API surface but the activation channel never wires.

**Patch shipped in 0.2.0.2 (commit pending):**

1. **CLSID locked**: `7FA7762F-41EC-4D72-9F06-58964AB36FEA` (generated 2026-05-08 via `[guid]::NewGuid()`; documented in `CONTEXT.md` -> Toast Activator Class ID).
2. **Manifest patch in `src/ToastRevival.Agent/Package.appxmanifest`**:
   - Added `xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10"` and `xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"` to `<Package>`.
   - Added `com desktop` to `IgnorableNamespaces`.
   - Added `<Extensions>` **inside `<Application>`** (NOT at Package level — first build attempt with Extensions at Package level failed schema validation `C00CE014` "Element ... unexpected according to content model of parent element"). Both `<com:Extension Category="windows.comServer">` and `<desktop:Extension Category="windows.toastNotificationActivation">` go inside `<Application>` per Microsoft's quickstart.
   - Both CLSIDs (`com:Class Id` and `ToastActivatorCLSID`) byte-for-byte identical.
3. **Diagnostic logging in `src/ToastRevival.Agent/Program.cs`**: `DiagLog` static class writes to `Windows.Storage.ApplicationData.Current.LocalFolder.Path\agent.log` when packaged, falls back to `%LOCALAPPDATA%\Toast2IT\Toast Notification\agent.log` when unpackaged. Logs at app start (with pid/args/baseDir/IsPackaged), pre/post `Register()`, pre/post `Show()`, exception path, every exit code.
4. **Version bumped to 0.2.0.2** in manifest + `scripts/build-msix.ps1` default.

**Hand-off (Keith):**
  1. Sign: `.\scripts\sign-msix.ps1 -Path artifacts\installer\msix\ToastNotification.Agent-0.2.0.2.msix`.
  2. Install on Win11 lab: `Add-AppxPackage -Path <signed-msix>` (or `-ForceUpdateFromAnyVersion` if 0.2.0.1 is still installed).
  3. Launch from Start menu tile (NON-elevated; the IsElevated guard at Program.cs:13 exits 3 in elevated context).
  4. Look for: visible toast banner (bottom-right), Action Center entry (Win+N), Settings -> System -> Notifications -> Toast Notification -> Notification history.
  5. Pull `agent.log` from `%LOCALAPPDATA%\Packages\Toast2IT.ToastNotification.Agent_8gxm9tzcy3sby\LocalState\agent.log` and ship it back.

**If toast fires:** mark M0 D2 complete in MILESTONES.md, move FIX-MSIX-004 to Resolved, capture EVIDENCE/2026-05-08-m0-d2-toast-fires-packaged.md.

**If toast still doesn't fire (fallback diagnostic tree):**
  - Read agent.log: did Register throw? Did Show return? What AUMID was used at runtime?
  - Check `TargetDeviceFamily MaxVersionTested="10.0.19041.0"` vs lab Win11 build (`[Environment]::OSVersion.Version.Build`). If lab build > 22000 there could be a notifications-suppressed-when-tested-version-too-low side effect (FIX-MSIX-001 already tracks bumping MaxVersionTested for Store flight; consider pulling forward).
  - Verify `BackgroundColor="#0F1117"` is a valid 6-char hex (it is; ruled out).
  - Check packaged AUMID via `Get-StartApps | Where-Object { $_.Name -like '*Toast*' }` and compare to the Identity-derived AUMID (`Toast2IT.ToastNotification.Agent_8gxm9tzcy3sby!App`).

**Reference:** Microsoft docs on packaged WinAppSDK toast activation: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/notifications/app-notifications/app-notifications-quickstart (Packaged section).

**Blocking:** YES for M0 D2 close. Cannot mark D2 complete until visible toast verified on signed 0.2.0.2.

### FIX-MSIX-003 (cosmetic) - mspdbcmf.exe warning during MSIX build

**Filed:** 2026-05-07 (M0 D2 Code Sweep)
**Surface:** `scripts/build-msix.ps1` invocation of `dotnet build`.
**Issue:** Warning "Path to mspdbcmf.exe could not be found. A symbols package will not be generated." prints during every MSIX build. Benign — only suppresses optional .appxsym output.
**Fix:** Add `-p:SymbolPackageFormat=none` to the `dotnet build` invocation in `build-msix.ps1`, OR install Visual Studio Build Tools 2022's debugging tools workload. Cosmetic only.
**Blocking:** No.

## Resolved
(None yet)
