# ToastRevival - Fix List

## Open Issues

### FIX-MSIX-001 — **RESOLVED 2026-05-08 (M0 D5)**

**Filed:** 2026-05-07 (M0 D2 Code Sweep)
**Surface:** `scripts/build-msix.ps1`
**Root cause discovered (M0 D5):** Setting `<TargetPlatformVersion>` in a csproj conditional PropertyGroup does NOT work. The .NET SDK TFM (`net8.0-windows10.0.19041.0`) sets `TargetPlatformVersion=10.0.19041.0` in a late `.targets` import that runs AFTER PropertyGroup evaluation, silently overriding any csproj value.
**Fix applied:** Added `-p:TargetPlatformVersion=10.0.22621.0` to the `dotnet build` invocation in `scripts/build-msix.ps1`. Command-line flags have higher MSBuild precedence than imported `.targets`. Produced manifest verified: `MaxVersionTested="10.0.22621.0"` ✓. See CONTEXT.md standing rule #4.

### INFO-D5-001 (M2) - No "already running" guard in Program.cs

**Filed:** 2026-05-08 (M0 D5 Code Sweep)
**Surface:** `src/ToastRevival.Agent/Program.cs`
**Issue:** The agent unconditionally fires a toast on every launch. If multiple startup triggers fire (e.g., startupTask + manual launch + second session on same machine), multiple agent instances fire multiple toasts per logon. Not introduced by D5 — pre-existing — but surfaced by the addition of `windows.startupTask` which creates a second automatic launch path.
**Fix:** Add a named mutex guard at process startup — exit cleanly if another instance is already running. M2 work.
**Blocking:** No.

### INFO-D5-002 (low) - MSI + MSIX simultaneous install fires two toasts per logon

**Filed:** 2026-05-08 (M0 D5 Code Sweep)
**Surface:** Deployment documentation (M0 D6, M7)
**Issue:** If both the MSI (Scheduled Task channel) and MSIX (startupTask channel) are installed simultaneously on the same machine, both launch mechanisms fire independently at logon, producing two toasts per session.
**Fix:** Document in M0 D6 deployment findings: "Do not install MSI and MSIX on the same endpoint. Choose one channel — MSI for RMM-managed deployment, MSIX/Store for user-managed." INFO-D5-001 mutex guard would also limit blast radius.
**Blocking:** No.

### FIX-MSIX-002 (low) - Manifest MinVersion vs. runtime gate divergence — **RESOLVED 2026-05-08**

**Filed:** 2026-05-07 (M0 D2 Code Sweep)
**Resolved:** 2026-05-08 (M0 D4 pre-work, commit pending)
**Surface:** `src/ToastRevival.Agent/Package.appxmanifest`, `src/ToastRevival.Agent/ToastRevival.Agent.csproj`

**Fix applied (Option b):** bumped `TargetDeviceFamily MinVersion` and `<TargetPlatformMinVersion>` from
`10.0.17763.0` (Win10 1809) to `10.0.19041.0` (Win10 2004 / build 19041), matching the `Program.cs`
runtime check. Manifest version bumped to `0.2.1.0`. Win10 1809 installs now fail at `Add-AppxPackage`
with a clear "requires Windows 10.0.19041.0" error rather than installing successfully and failing
silently at runtime. See `EVIDENCE/2026-05-08-m0-d4-fix-msix-002.md`.

**Win10 1809 lab verification:** not performed (no 1809 lab machine). Acceptable — the fix is
preventative for a platform below the product's stated floor, and the lab machine is Win11.

### FIX-MSIX-004 (medium) - Packaged MSIX install does not fire toasts - **RESOLVED 2026-05-08 (commit `6e3495c`)**

**Update 2026-05-08 (post-0.2.0.2 install attempt):** DiagLog from 0.2.0.2 install captured `AppNotificationManager.Default.Register()` throwing `COMException 0x80070490` (`HRESULT_FROM_WIN32(ERROR_NOT_FOUND)`) before `Show()` was reached. Original FIX-MSIX-004 hypothesis (Show silently no-ops) was wrong; Register() itself was the failure point. **Root cause: missing `Arguments="----AppNotificationActivated:"` on `<com:ExeServer>`.** Microsoft's packaged-WinAppSDK quickstart sample includes the four-dash sentinel; the framework uses it as the activator surface marker, and Register()'s COM class registration lookup fails ERROR_NOT_FOUND without it.

**Resolution:** 0.2.0.3 patch (commit `6e3495c`) added the Arguments token. Keith signed + installed via Add-AppxPackage on Win11 lab. DiagLog confirmed `Register() returned without throwing`, `Show() returned without throwing`, and `NotificationInvoked` fired with the expected argument payload after Keith clicked the toast's Acknowledge button. Single visible toast appeared, no duplicates, button-click routed cleanly. CONTEXT.md "Toast Activator Class ID" section captures the standing rule for the Arguments token going forward. See `EVIDENCE/2026-05-08-m0-d2-fix-msix-004-register-not-found.md` and `EVIDENCE/2026-05-08-m0-d2-toast-fires-packaged.md`.



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

### INFO-M1-001 — DeviceGroupMember missing global query filter (low)
**Filed:** 2026-05-08 (M1 Code Sweep)
**Surface:** `Data/AppDbContext.cs`, EF model validation warning
**Issue:** EF Core warning: "Entity 'Device' has a global query filter defined and is the required end of a relationship with 'DeviceGroupMember'." `DeviceGroupMember` has no TenantId column so cannot have its own filter. In practice it is only ever loaded through Device or DeviceGroup (both filtered), so cross-tenant leakage is not possible via normal query paths.
**Fix:** Acceptable as-is for M1. Could add a TenantId column to DeviceGroupMember in a future migration if the warning becomes a compliance concern.
**Blocking:** No.

### INFO-M1-003 — Device registration trusts TenantId from request body (medium)
**Filed:** 2026-05-08 (M1 Code Sweep)
**Surface:** `Controllers/DevicesController.cs` `POST /api/devices/register`
**Issue:** Any client that knows a valid TenantId can register a device for that tenant. No pre-shared enrollment key gates the endpoint.
**Fix:** M3 hardening — add `EnrollmentKey` column to Tenant, require it in `RegisterDeviceRequest`, validate server-side before allowing registration.
**Blocking:** No. Endpoint behavior is noted in code comment.

### INFO-M1-004 — No test coverage (low)
**Filed:** 2026-05-08 (M1 Code Sweep)
**Surface:** `src/ToastRevival.Api/` — entire project
**Issue:** Zero unit tests, zero integration tests.
**Fix:** M8 integration testing milestone. For earlier milestones, individual controller/service tests can be added incrementally.
**Blocking:** No.

### INFO-M1-005 — Scheduled notifications lost on restart (low)
**Filed:** 2026-05-08 (M1 Code Sweep)
**Surface:** `Services/NotificationQueueService.cs`
**Issue:** The `Channel<Guid>` is in-memory and unbounded. Notifications queued for future delivery (`ScheduledAt > now`) are not re-queued on service restart because they are never written to the channel.
**Fix:** On startup, load all `Notification` rows with `Status = Queued` and `ScheduledAt <= now` and enqueue them. Long-term: replace with durable queue (e.g., PostgreSQL-backed queue or dedicated message broker).
**Blocking:** No. Not a concern until real users schedule future notifications.

### INFO-M1-006 — JWT key requires environment-specific override (low)
**Filed:** 2026-05-08 (M1 Code Sweep)
**Surface:** `appsettings.json`
**Issue:** `Jwt:Key` placeholder is in committed `appsettings.json`. No runtime assertion enforces minimum key length or environment-specific override.
**Fix:** Add a startup check: `if (jwtKey.Length < 32 && !app.Environment.IsDevelopment()) throw`. Use environment variable `Jwt__Key` for production overrides.
**Blocking:** No. Covered by deployment documentation (M7/M9).

## Resolved

- **FIX-MSIX-004** (medium) - 2026-05-08, commit `6e3495c`. Packaged MSIX install did not fire toasts because `<com:ExeServer>` was missing `Arguments="----AppNotificationActivated:"`. Patched, signed, installed; visible toast verified on Win11 lab with button-click routing through `NotificationInvoked`. See entry above for full root-cause detail.
