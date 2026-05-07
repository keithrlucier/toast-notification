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

### FIX-MSIX-003 (cosmetic) - mspdbcmf.exe warning during MSIX build

**Filed:** 2026-05-07 (M0 D2 Code Sweep)
**Surface:** `scripts/build-msix.ps1` invocation of `dotnet build`.
**Issue:** Warning "Path to mspdbcmf.exe could not be found. A symbols package will not be generated." prints during every MSIX build. Benign — only suppresses optional .appxsym output.
**Fix:** Add `-p:SymbolPackageFormat=none` to the `dotnet build` invocation in `build-msix.ps1`, OR install Visual Studio Build Tools 2022's debugging tools workload. Cosmetic only.
**Blocking:** No.

## Resolved
(None yet)
