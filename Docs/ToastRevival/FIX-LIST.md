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

### FIX-MSIX-004 (medium) - Packaged MSIX install does not fire toasts (likely missing COM activator manifest extensions)

**Filed:** 2026-05-07 (M0 D2 install validation)
**Surface:** Win11 lab machine, signed `ToastNotification.Agent-0.2.0.1.msix` installed via Add-AppxPackage.

**Symptom:** Console window flashes when the package launches via Start menu tile or `shell:appsfolder\<AUMID>`, but no toast banner appears, no entry lands in Action Center (Win+N), and Settings -> System -> Notifications -> Toast Notification shows no Notification history. The same agent code shipped via the M0A MSI fires toasts reliably (Startup-folder shortcut, unpackaged path).

**Leading hypothesis:** Our `Package.appxmanifest` is missing the COM activator declarations that the WinAppSDK packaged toast path requires.

For UNPACKAGED apps, `AppNotificationManager.Default.Register()` auto-injects a CLSID into `HKCU\SOFTWARE\Classes\CLSID\...` and `HKCU\SOFTWARE\Classes\AppUserModelId\...` so the toast pipe is wired implicitly. That is why the M0A MSI works.

For PACKAGED apps, the framework expects the COM CLSID to be declared in the manifest. Without those declarations, `AppNotificationManager.Default.Register()` likely succeeds at the API surface but fails to wire the activation channel; subsequent `Show()` calls get accepted by the runtime but produce no visible toast and no Action Center entry. (We catch and stderr exceptions, but the console closes before they can be read; a log capture in next session will confirm.)

**Manifest patch shape (for next session, not yet applied):**

```xml
<!-- Add these namespaces to the <Package> element -->
xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10"
xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"

<!-- IgnorableNamespaces: add com desktop -->

<!-- After </Applications>, before <Capabilities> -->
<Extensions>
  <com:Extension Category="windows.comServer">
    <com:ComServer>
      <com:ExeServer Executable="ToastNotification.Agent.exe" DisplayName="Toast Notification Activator">
        <com:Class Id="GENERATE-A-NEW-GUID" DisplayName="Toast Notification Activator" />
      </com:ExeServer>
    </com:ComServer>
  </com:Extension>
  <desktop:Extension Category="windows.toastNotificationActivation">
    <desktop:ToastNotificationActivation ToastActivatorCLSID="SAME-GUID-AS-ABOVE" />
  </desktop:Extension>
</Extensions>
```

The CLSID GUID must be identical in both extension blocks. Generate once via `[guid]::NewGuid()` and bake in. Once baked, do NOT change it - it identifies the activation surface and any change would orphan registrations on already-installed clients.

**Diagnostic plan for next session (in order):**
  1. Add file-based logging to `Program.cs` immediately after `AppNotificationManager.Default.Register()` and `Show()` calls. Log to `(Windows.Storage.ApplicationData.Current.LocalFolder.Path)\agent.log` for packaged context, fallback to `%LOCALAPPDATA%\Toast2IT\Toast Notification\agent.log` for unpackaged. This proves whether Register succeeded, whether Show was reached, and what the AUMID looks like at runtime.
  2. Rebuild + re-sign + reinstall the unmodified package (no manifest patch yet). Launch from Start menu tile. Read the log: did Register throw? Did Show return cleanly? What AUMID was used?
  3. Apply the manifest patch above (com + desktop extensions, generated CLSID). Bump version to 0.2.0.2. Rebuild + re-sign + reinstall. Re-test. If toast now appears, the hypothesis is confirmed and patch lands as the fix.
  4. If the manifest patch alone doesn't resolve it, look at: TargetDeviceFamily MaxVersionTested vs the lab Win11 build (mismatch can suppress notifications); manifest visualElements `BackgroundColor` (#0F1117 should be valid); the packaged AUMID hash vs what `Get-StartApps` reports.

**Reference points:**
  - M0A MSI's Startup-folder shortcut launches the same agent code unelevated at user login and toasts fire reliably - proves the agent code itself is correct in user context.
  - Microsoft docs on packaged WinAppSDK toast activation: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/notifications/app-notifications/app-notifications-quickstart (Packaged section explicitly enumerates the com:Extension declarations).

**Blocking:** YES for M0 D2 close. Cannot mark D2 complete until visible toast verified.

### FIX-MSIX-003 (cosmetic) - mspdbcmf.exe warning during MSIX build

**Filed:** 2026-05-07 (M0 D2 Code Sweep)
**Surface:** `scripts/build-msix.ps1` invocation of `dotnet build`.
**Issue:** Warning "Path to mspdbcmf.exe could not be found. A symbols package will not be generated." prints during every MSIX build. Benign — only suppresses optional .appxsym output.
**Fix:** Add `-p:SymbolPackageFormat=none` to the `dotnet build` invocation in `build-msix.ps1`, OR install Visual Studio Build Tools 2022's debugging tools workload. Cosmetic only.
**Blocking:** No.

## Resolved
(None yet)
