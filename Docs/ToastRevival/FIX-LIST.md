# ToastRevival - Fix List

## Open Issues

### FIX-MSIX-001 — **RESOLVED 2026-05-08 (M0 D5)**

**Filed:** 2026-05-07 (M0 D2 Code Sweep)
**Surface:** `scripts/build-msix.ps1`
**Root cause discovered (M0 D5):** Setting `<TargetPlatformVersion>` in a csproj conditional PropertyGroup does NOT work. The .NET SDK TFM (`net8.0-windows10.0.19041.0`) sets `TargetPlatformVersion=10.0.19041.0` in a late `.targets` import that runs AFTER PropertyGroup evaluation, silently overriding any csproj value.
**Fix applied:** Added `-p:TargetPlatformVersion=10.0.22621.0` to the `dotnet build` invocation in `scripts/build-msix.ps1`. Command-line flags have higher MSBuild precedence than imported `.targets`. Produced manifest verified: `MaxVersionTested="10.0.22621.0"` ✓. See CONTEXT.md standing rule #4.

### INFO-D5-001 — **RESOLVED 2026-05-09 (M2.A)**

**Filed:** 2026-05-08 (M0 D5 Code Sweep)
**Resolved:** 2026-05-09 (M2.A, FIX-M2A-001 patch + named mutex implementation)
**Surface:** `src/ToastRevival.Agent/Program.cs`
**Resolution:** `AgentEntryPoint.RunAsync` now takes a session-local named mutex (`Local\Toast2IT.ToastNotification.PrimaryWorker`) before entering primary worker mode. Activation mode + diagnostic mode both short-circuit BEFORE the mutex acquisition (their flows are short-lived and must not block the long-running primary). `WaitOne(TimeSpan.Zero)` non-blocking try; if held, exit code 5 with a clear stderr message. `AbandonedMutexException` catch path takes ownership when the previous holder crashed without releasing. `Local\` prefix (NOT `Global\`) — verified during Code Sweep that `Global\` would regress M0 D4 multi-user verification by colliding across Windows sessions; FIX-M2A-001 patched the prefix pre-commit.

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

### INFO-M1-005 — **RESOLVED 2026-05-09 (M5.C)**

**Filed:** 2026-05-08 (M1 Code Sweep)
**Resolved:** 2026-05-09 (M5.C)
**Surface:** `Services/NotificationQueueService.cs`
**Resolution:** `EnqueueDueScheduledAsync` runs at startup (backfill) and every 60 seconds (via `RunSchedulerLoopAsync` PeriodicTimer). Backfill loads `Notifications WHERE Status=Queued AND ScheduledAt<=now` and enqueues them. Timer tick does the same sweep continuously. `ProcessAsync` now guards on `Status != Queued` to prevent double-fanout if a startup + timer tick overlap. Both tasks run concurrently via `Task.WhenAll` alongside the existing queue consumer (`ProcessQueueAsync`).

### INFO-M1-006 — JWT key requires environment-specific override (low)
**Filed:** 2026-05-08 (M1 Code Sweep)
**Surface:** `appsettings.json`
**Issue:** `Jwt:Key` placeholder is in committed `appsettings.json`. No runtime assertion enforces minimum key length or environment-specific override.
**Fix:** Add a startup check: `if (jwtKey.Length < 32 && !app.Environment.IsDevelopment()) throw`. Use environment variable `Jwt__Key` for production overrides.
**Blocking:** No. Covered by deployment documentation (M7/M9).

### INFO-MSIX-004-D — **RESOLVED 2026-05-09 (M2.A)**

**Filed:** 2026-05-08 (M0 D2)
**Resolved:** 2026-05-09 (M2.A activation handler implementation)
**Surface:** `src/ToastRevival.Agent/Program.cs`, `src/ToastRevival.Api/Controllers/NotificationsController.cs`
**Resolution:** `AgentEntryPoint.TryFindActivationArg` detects the framework sentinel `----AppNotificationActivated:` in argv before mutex acquisition or hub spin-up. When matched, `ActivationMode.RunAsync` takes over: (1) loads `DeviceConfig` from disk; (2) subscribes to `AppNotificationManager.Default.NotificationInvoked`; (3) calls `Register()` (the framework fires `NotificationInvoked` synchronously during this call with the original toast's argument string); (4) parses click args; (5) if `source==hub`, posts to new device-JWT-authenticated `POST /api/notifications/{notificationId}/interactions` REST endpoint via `InteractionFallback.PostAsync`; (6) calls `Unregister()` and exits clean. 5-second timeout on the NotificationInvoked wait (exit 7) and 15-second timeout on the REST POST. Activation mode never spins up SignalR or contests the primary mutex.

### INFO-M2A-002 (M3 — security hardening) — DeviceConfig at rest is plaintext

**Filed:** 2026-05-09 (M2.A Code Sweep)
**Surface:** `src/ToastRevival.Agent/DeviceConfig.cs::ConfigStore`
**Issue:** Per-device JWT and per-tenant HMAC signing key are stored as plaintext JSON at `%LOCALAPPDATA%\Toast2IT\Toast Notification\config.json` (or the package's `LocalState` equivalent). Per-user LocalAppData ACLs gate ordinary access; admin-credential exfiltration is not gated. An attacker with admin on the endpoint can impersonate the device to the backend and forge HMAC-signed payloads.
**Fix:** Wrap `Save`/`TryLoad` with `ProtectedData.Protect`/`Unprotect` at `DataProtectionScope.CurrentUser`. Acceptable additional surface for the security-hardening milestone.
**Blocking:** No. M3 work.

### INFO-M2A-003 — **RESOLVED 2026-05-09 (M2.B)**

**Filed:** 2026-05-09 (M2.A Code Sweep)
**Resolved:** 2026-05-09 (M2.B, `NotificationQueueService.RecoverOrphansAsync`)
**Surface:** `src/ToastRevival.Api/Services/NotificationQueueService.cs::RecoverOrphansAsync` (new, called once at `ExecuteAsync` startup before the channel loop).
**Resolution:** Sweep `Notifications WHERE Status=Sending AND SentAt < now() - INTERVAL '5 minutes'` → Status=`Failed`, CompletedAt=now. **Pending deliveries are NOT touched** (Carl's M2.B overrule on the originally-planned "deliveries to Failed accordingly") — the `GET /pending` catch-up endpoint can still serve them to the agent on reconnect. The state divergence (notification Failed, deliveries Pending → Delivered later) is acceptable: dashboard sees Failed-fanout while delivery counts trickle up; the alternative (mark deliveries Failed) would have defeated catch-up entirely. Sweep is non-fatal (try/catch around it; the queue still serves new traffic if recovery fails). Idempotent — rerun after a fast restart finds nothing because the threshold rejects rows under 5 minutes old.

### INFO-M2A-004 — **RESOLVED 2026-05-09 (M2.B)**

**Filed:** 2026-05-09 (M2.A Code Sweep)
**Resolved:** 2026-05-09 (M2.B, `AgentHubClient.RenderAndReportAsync`)
**Surface:** `src/ToastRevival.Agent/AgentClient.cs::AgentHubClient` (`_renderedCache: MemoryCache<Guid, byte>`, 1-hour sliding expiration; checked in `RenderAndReportAsync`).
**Resolution:** Notification render + ReportDelivery now go through a shared `RenderAndReportAsync` helper called from both the hub-pushed path (`OnReceiveNotificationAsync`) and the catch-up path (`RunCatchupAsync`). Dedup short-circuits BOTH render AND ReportDelivery — once a notificationId has been delivered in this process, no path re-acknowledges it. The cache entry is set ONLY after `Show()` returns successfully, so a render failure does not poison the cache and prevents a future retry. Sliding window resets on every touch — a notification re-served on every reconnect for an hour stays cached.

### INFO-M2B-002 (M3 / M5) — Pending endpoint pagination beyond 100

**Filed:** 2026-05-09 (M2.B Code Sweep)
**Surface:** `src/ToastRevival.Api/Controllers/NotificationsController.cs::GetPending`
**Issue:** Hard cap of 100 items per call. A device with >100 backlog drains across multiple reconnect cycles. Functionally correct (dedup cache prevents replay during paging; remaining Pending deliveries get served on the next Reconnected catch-up cycle), but a long-offline endpoint with a heavy notification volume could take many reconnects to fully drain.
**Fix:** Add explicit pagination — return `(items, nextCursor)` and let the agent loop until `nextCursor==null`. Or raise the cap.
**Blocking:** No. Acceptable for current MVP scale.

### INFO-M2B-003 (M3 / M5) — DB index for catch-up query

**Filed:** 2026-05-09 (M2.B Code Sweep)
**Surface:** `src/ToastRevival.Api/Data/AppDbContext.cs` — `NotificationDelivery` entity model.
**Issue:** No composite index on `(DeviceId, Status, CreatedAt)`. The catch-up query filters on all three; PostgreSQL will currently scan or use the FK index on DeviceId. Acceptable at MVP scale; will become a real concern once a single MSP customer accumulates millions of delivery rows.
**Fix:** Add `e.HasIndex(d => new { d.DeviceId, d.Status, d.CreatedAt })` in `OnModelCreating` and generate a migration.
**Blocking:** No.

### INFO-M2B-004 — **RESOLVED 2026-05-08 (M3, commit `362f9d3`)**

**Filed:** 2026-05-09 (M2.B Code Sweep)
**Surface:** `src/ToastRevival.Agent/AgentClient.cs::AgentHubClient._renderedCache`
**Resolution:** `MemoryCacheOptions { SizeLimit = 50_000 }` + `Size = 1` on each entry. 50K × ~100 bytes ≈ 5MB ceiling.

### INFO-M2C-001 (M9 — pre-launch) — Tray icon HICON handles not freed

**Filed:** 2026-05-08 (M2.C Code Sweep)
**Surface:** `src/ToastRevival.Agent/TrayIconService.cs::CreateCircleIcon`
**Issue:** `Bitmap.GetHicon()` creates Win32 HICON handles. `Icon.FromHandle()` wraps them without taking ownership — handles are not freed when the Icon or TrayIconService is disposed. For process-lifetime tray icons (5 handles total), the leak is ~5 HICONs per agent session, released on process exit. Non-issue at current scale.
**Fix:** Before M9 GA: store HICON handles and call `DestroyIcon` (P/Invoke) in TrayIconService.Dispose(). Low priority until production tile assets replace placeholder GDI+ icons anyway.
**Blocking:** No.

### INFO-M2C-002 (M3) — SetupMode after OS version check

**Filed:** 2026-05-08 (M2.C Code Sweep)
**Surface:** `src/ToastRevival.Agent/Program.cs::AgentEntryPoint.RunAsync`
**Issue:** `--setup-bootstrap` detection is after the `IsWindowsVersionAtLeast(10,0,19041)` guard. On a sub-19041 machine, the WiX WriteBootstrapJson CA exits 2 and bootstrap.json is not written. This is the unsupported OS floor — the agent wouldn't run on that machine anyway. A MSI-level OS version condition (LaunchCondition or Condition on Feature) would prevent installs on unsupported OS entirely, eliminating the ambiguity.
**Fix:** Add `LaunchCondition` in WiX requiring `VersionNT64 >= 1904` (hex 0x774) at M3 or before M9.
**Blocking:** No.

### INFO-M2C-003 (acceptable) — async void ReconnectRequested lambda

**Filed:** 2026-05-08 (M2.C Code Sweep)
**Surface:** `src/ToastRevival.Agent/Program.cs::PrimaryMode.RunAsync`
**Issue:** `async void` lambda subscribed to `tray.ReconnectRequested`. Unhandled exceptions in async void crash the process. The entire body is wrapped in try/catch, which mitigates this. Pattern is consistent with existing `async void OnNotificationInvoked` in AgentClient.cs.
**Fix:** Acceptable as-is. If future modifications add code paths outside the try/catch, revisit.
**Blocking:** No.

### INFO-M2C-004 (acceptable) — TrayIconService 3s STA init wait

**Filed:** 2026-05-08 (M2.C Code Sweep)
**Surface:** `src/ToastRevival.Agent/TrayIconService.cs` constructor
**Issue:** Constructor blocks the calling thread up to 3 seconds waiting for `_uiReady`. In normal conditions the STA thread initializes in <50ms. Under extreme resource contention, if initialization exceeds 3 seconds, `_notifyIcon` may be null and the tray icon never appears. The agent functions normally — tray icon is cosmetic/UX surface, not correctness-critical.
**Fix:** Acceptable. The graceful degradation path (ApplyState null-checks _notifyIcon) is verified.
**Blocking:** No.

### INFO-M2D-003 (acceptable) — updateTask not awaited on shutdown

**Filed:** 2026-05-08 (M2.D Code Sweep)
**Surface:** `src/ToastRevival.Agent/Program.cs::PrimaryMode.RunAsync`
**Issue:** `updateTask` (background Velopack check loop) is started via `Task.Run` but never awaited in the cleanup path. On shutdown, `PeriodicTimer.WaitForNextTickAsync` returns false when the CancellationToken is cancelled; the task completes shortly after. All exceptions inside `RunUpdateLoopAsync` are caught within the loop body, so the task never faults. Fire-and-forget posture.
**Fix:** Acceptable. Same pattern as the conceptual model of `_pingLoop` in AgentHubClient. If an awaited-cleanup pattern is adopted at M9, add `updateTask` to the shutdown sequence.
**Blocking:** No.

### INFO-M2D-004 (acceptable) — _updateItem Font object not explicitly disposed

**Filed:** 2026-05-08 (M2.D Code Sweep)
**Surface:** `src/ToastRevival.Agent/TrayIconService.cs::RunMessageLoop`
**Issue:** `new System.Drawing.Font(SystemFonts.MenuFont!, FontStyle.Bold)` creates a GDI font object stored in the `_updateItem` ToolStripMenuItem's Font property. Not explicitly disposed in TrayIconService.Dispose(). Single process-lifetime object; negligible resource.
**Fix:** Acceptable. The ContextMenuStrip.Dispose() disposes child items but may not release the custom font. Before M9 GA: store reference in a field and Dispose() it explicitly.
**Blocking:** No.

### INFO-M2D-005 (M3 — security hardening) — TrySelfRedirect launches binary from user-writable path

**Filed:** 2026-05-08 (M2.D Code Sweep)
**Surface:** `src/ToastRevival.Agent/UpdateService.cs::TrySelfRedirect`
**Issue:** The redirect launches `%LocalAppData%\ToastNotification.Agent\current\ToastNotification.Agent.exe` based only on a version comparison. A local-user attacker could plant a higher-versioned binary at that path. This is the inherent Squirrel/Velopack per-user update model limitation.
**Fix:** M3 — verify Authenticode signature of `managedExe` via `AuthenticodeTools` or `Get-AuthenticodeSignature` P/Invoke before launching. Signer must be `CN="Toast2IT, LLC"`. This closes the gap alongside INFO-M2A-002 (DPAPI config protection).
**Blocking:** No. Threat model: local user compromise required, same as existing config.json exposure.

### INFO-M2D-006 (acceptable) — FastCallback hooks fire before DiagLog.Init()

**Filed:** 2026-05-08 (M2.D Code Sweep)
**Surface:** `src/ToastRevival.Agent/Program.cs` top-level statements
**Issue:** `VelopackApp.Build().OnAfterInstallFastCallback().OnAfterUpdateFastCallback().Run()` is called before `AgentEntryPoint.RunAsync` which calls `DiagLog.Init()`. The two FastCallback handlers call `DiagLog.Write()`. Because `DiagLog.LogFilePath` is `""` until `Init()` is called, `Write()` returns early and the messages are silently dropped.
**Fix:** Acceptable — FastCallbacks only fire during install/update lifecycle events, not normal startup. The lifecycle events are self-reporting (Velopack has its own log) and the dropped DiagLog messages carry no information that isn't already in Velopack's output. If verbose lifecycle logging becomes a requirement, call `DiagLog.Init()` before `VelopackApp.Build().Run()`.
**Blocking:** No.

### FIX-M3-001 — **PATCHED PRE-COMMIT 2026-05-08 (M3 Code Sweep — Abish caught)**

**Filed:** 2026-05-08 (M3 Code Sweep)
**Resolved:** 2026-05-08 (same session, before commit)
**Surface:** `installer/ToastRevival.Agent.Setup.wxs` `<Launch Condition>`
**Issue:** Condition written as `VersionNT64 >= 1904` — WiX `VersionNT64` is `major*100+minor` (Windows 10/11 = 1000), not the OS build number. `1000 >= 1904` evaluates false → MSI would have blocked installation on every Windows 10/11 machine with the message "requires Windows 10 version 2004".
**Fix:** Changed to `VersionNT64 >= 1000` (catches pre-Windows-10 installs; precise build-19041 floor is enforced at runtime by `Program.cs` line 54).
**Blocking:** WAS BLOCKING — patched before commit.

### INFO-M3-001 (M8) — TOTP replay within the same 30s step is accepted

**Filed:** 2026-05-08 (M3 Code Sweep)
**Surface:** `src/ToastRevival.Api/Services/MfaService.cs::Verify`
**Issue:** `VerifyTotp` with `VerificationWindow(1, 1)` accepts a code for 90s total. Within the same 30s step a replayed TOTP code from an intercepted request would be accepted. Standard TOTP limitation; no nonce/replay-cache.
**Fix:** M8 — add a used-code cache (e.g. per-user last-verified `long timeStep` stored in DB) to reject replay within the same step.
**Blocking:** No.

### INFO-M3-002 (M4) — BlocklistService is concrete injection in NotificationsController

**Filed:** 2026-05-08 (M3 Code Sweep)
**Surface:** `src/ToastRevival.Api/Controllers/NotificationsController.cs`
**Issue:** `BlocklistService` is a concrete class injected directly; no `IBlocklistService` interface. Makes the controller hard to unit test without the DB.
**Fix:** Extract `IBlocklistService` interface at M4 when unit tests are introduced.
**Blocking:** No.

### INFO-M3-003 (M4) — ContentSafetyService logs to Console.Error

**Filed:** 2026-05-08 (M3 Code Sweep)
**Surface:** `src/ToastRevival.Api/Services/ContentSafetyService.cs`
**Issue:** Azure scan failures are written to `Console.Error` — not structured logging. Will disappear in production without log capture.
**Fix:** Inject `ILogger<ContentSafetyService>` at M4 when the DI logging infrastructure is wired.
**Blocking:** No.

### INFO-M2B-005 — **RESOLVED 2026-05-08 (M3)**

**Filed:** 2026-05-09 (M2.B Code Sweep)
**Resolved:** 2026-05-08 (M3, commit `362f9d3`)
**Surface:** `src/ToastRevival.Api/Program.cs`, `NotificationsController.cs`
**Resolution:** Added `device-catchup-per-hour` fixed-window policy (60 req/hr). Catch-up endpoint (`GET /api/notifications/pending`) switched from `device-per-hour` to `device-catchup-per-hour`. Existing `device-per-hour` (10/hr) retained for `ReportInteraction` and heartbeat ping.

### FIX-M2B-001 — **PATCHED PRE-COMMIT 2026-05-09 (M2.B Code Sweep)**

**Filed:** 2026-05-09 (M2.B Code Sweep — Abish caught)
**Resolved:** 2026-05-09 (same session, before commit)
**Surface:** `src/ToastRevival.Agent/AgentClient.cs::AgentHubClient._lastCatchupSince`
**Issue:** First implementation initialized `_lastCatchupSince = DateTime.UtcNow` at ctor. The catch-up GET would then send `since=<ctor_time>` on the very first call. Server filter `delivery.CreatedAt >= since` would have excluded EVERY pre-existing Pending delivery — exactly the case M2.B exists to fix (agent rebooted, has Pending from before the reboot, reconnects). The catch-up endpoint would have returned zero results in its primary scenario.
**Fix:** Changed `_lastCatchupSince` to nullable `DateTime?`, default null. First catch-up call omits the `since` query param entirely so the server returns all Pending up to the cap. Subsequent calls send the captured `nextSince` timestamp from the previous call. Side benefit: avoids time-zone coercion issues with `DateTime.MinValue.Kind=Unspecified` against Npgsql `timestamptz` columns.
**Blocking:** WAS BLOCKING — patched before commit. Build clean post-patch.

### INFO-M2A-005 (M9 — deploy doc) — Migration backfill requires Postgres 13+

**Filed:** 2026-05-09 (M2.A Code Sweep)
**Surface:** `src/ToastRevival.Api/Migrations/20260509002218_AddTenantSigningKey.cs`
**Issue:** The backfill SQL uses `gen_random_uuid()` which is built-in to Postgres 13+ (previously required `pgcrypto` extension). Acceptable for any modern Postgres deployment but the floor should be documented.
**Fix:** Document Postgres minimum-version (13+) in M9 deployment infra.
**Blocking:** No.

### INFO-M4-001 — **RESOLVED 2026-05-09 (M5.A)**

**Filed:** 2026-05-08 (M4 Code Sweep — Abish caught)
**Resolved:** 2026-05-09 (M5.A)
**Surface:** `src/ToastRevival.Dashboard/src/pages/Compose.tsx`, `src/ToastRevival.Api/Controllers/TemplatesController.cs`
**Resolution:** `GET /api/templates` endpoint added (TemplatesController). 6 default templates seeded on tenant registration (AuthController.Register). Compose.tsx fetches templates on mount, builds slug→Guid map, includes `templateId` in `buildRequest()` when a template has been applied. Graceful degradation: if fetch fails, templateId stays undefined. `TemplateDbRecord` interface added to notifications.ts. `templateId` added to `SendNotificationRequest` interface.

### INFO-M4-002 — **RESOLVED 2026-05-09 (M5.A)**

**Filed:** 2026-05-08 (M4 Code Sweep — Abish)
**Resolved:** 2026-05-09 (M5.A)
**Surface:** `src/ToastRevival.Api/Controllers/DeviceGroupsController.cs` (new)
**Resolution:** `DeviceGroupsController` added with 6 endpoints: GET list, POST create, DELETE group, GET members, POST add member, DELETE remove member. DeviceCount maintained manually (increment on add, decrement with floor guard on remove). Admin-only for write operations, all-authenticated for reads.

### INFO-M4-003 — **RESOLVED 2026-05-09 (M5.A)**

**Filed:** 2026-05-08 (M4 Code Sweep — Abish)
**Resolved:** 2026-05-09 (M5.A)
**Surface:** `src/ToastRevival.Api/Controllers/NotificationsController.cs::History`
**Resolution:** `page` (default 1) and `pageSize` (default 25, clamped 1–100) query params added. `Skip/Take` applied server-side. Frontend's existing `notificationsApi.list(page, pageSize)` call now honored correctly.

### INFO-M5-001 — **RESOLVED 2026-05-09 (M6)**

**Resolved:** 2026-05-09 (M6). Template seeding in `AuthController.Register` now wrapped in try/catch with explicit `RollbackAsync` + clean 500 response: "Registration succeeded but template initialization failed. Contact support."

---

### INFO-M5-001 (M6 — hardening) — No explicit error handling on template seeding in AuthController.Register

**Filed:** 2026-05-09 (M5.A Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Controllers/AuthController.cs::Register`
**Issue:** Template seeding (`TemplatesController.BuildDefaultTemplates` → `SaveChangesAsync`) runs inside the existing registration transaction but without an explicit try/catch + RollbackAsync. If seeding throws, the EF exception propagates, the transaction is not committed, and the DB rolls back implicitly — correct behavior. But the caller receives a 500 response instead of a clean error message.
**Fix:** Wrap template seeding in try/catch at M6; on failure, roll back and return a clean 500 with a meaningful message: "Registration succeeded but template initialization failed. Contact support."
**Blocking:** No. Template model has no constraints that would cause legitimate failures under normal operation.

### INFO-M5-002 (acceptable) — UsersController.Invite has no role ceiling

**Filed:** 2026-05-09 (M5.A Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Controllers/UsersController.cs::Invite`
**Issue:** An Admin can invite a SuperAdmin. No role ceiling enforcement.
**Fix:** Acceptable for MSP context (admins are trusted operators). If compliance requires it, add: `if (req.Role > callerRole) return Forbid()` at M6.
**Blocking:** No.

### INFO-M5B-001 (performance, future) — AnalyticsController.Summary materializes statuses in memory

**Filed:** 2026-05-09 (M5.B Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Controllers/AnalyticsController.cs::Summary`
**Issue:** `_db.NotificationDeliveries.Where(d => d.CreatedAt >= since).Select(d => d.Status).ToListAsync()` brings all status values into memory for the period, then counts in C#. For MVP scale (thousands of records per MSP tenant), this is acceptable. For high-volume tenants (millions of deliveries), a server-side `GROUP BY Status COUNT(*)` would be significantly faster.
**Fix:** Replace with `GroupBy(d => d.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync()` then materialize to dict. EF Core 8 translates this to a server-side GROUP BY.
**Blocking:** No.

### INFO-M5B-002 (acceptable) — UpdateSettings silently ignores invalid DefaultScenario

**Filed:** 2026-05-09 (M5.B Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Controllers/TenantController.cs::UpdateSettings`
**Issue:** If the client sends `{"defaultScenario": "INVALID"}`, `Enum.TryParse` returns false, the field is not updated, and a 204 is returned with no indication that the value was rejected.
**Fix:** Return `BadRequest("Invalid defaultScenario value.")` when `req.DefaultScenario != null && Enum.TryParse fails`. M6+.
**Blocking:** No. Frontend dropdown is constrained to valid values.

### INFO-M5B-003 (acceptable) — PrimaryColor stored without hex-format validation

**Filed:** 2026-05-09 (M5.B Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Controllers/TenantController.cs::UpdateSettings`
**Issue:** `PrimaryColor` is stored as-is. A malicious admin could store arbitrary text. Downstream rendering uses the value only in a color picker input (not injected as CSS), so no XSS vector. But the data is untrusted.
**Fix:** Add regex validation (`^#[0-9A-Fa-f]{6}$`) at M6+.
**Blocking:** No.

### INFO-M5C-001 (M9 — deploy doc) — Uploaded assets are publicly accessible by URL
**Filed:** 2026-05-09 (M5.C Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Program.cs` — `app.UseStaticFiles()`
**Issue:** Files in `wwwroot/assets/` are served without authentication. Any client that knows a valid asset URL can fetch the image. This is intentional — the Windows agent must fetch hero/logo images from toast payloads without a user JWT.
**Fix:** Document in M9 deployment notes. If privacy of notification images is ever required, move to a signed-URL pattern (Azure Blob SAS, S3 presigned). Not a concern for MSP-managed endpoint images.
**Blocking:** No.

### INFO-M5C-002 — **RESOLVED 2026-05-09 (M6)**

**Resolved:** 2026-05-09 (M6). `IX_Notifications_Status_ScheduledAt` partial index added in M6Billing migration.

---

### INFO-M5C-002 (M6+) — No index on (Status, ScheduledAt) for scheduler sweep
**Filed:** 2026-05-09 (M5.C Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Services/NotificationQueueService.cs::EnqueueDueScheduledAsync`
**Issue:** The scheduler sweep queries `Notifications WHERE Status=Queued AND ScheduledAt<=now` across all tenants. No composite index. At MVP scale acceptable; at production scale (millions of rows) will become a sequential scan.
**Fix:** Add `HasIndex(n => new { n.Status, n.ScheduledAt }).HasFilter("scheduled_at IS NOT NULL")` in AppDbContext and generate a migration at M6+.
**Blocking:** No.

### INFO-M5C-003 (acceptable) — Drop zone MIME type accepts image/* in addition to extension whitelist
**Filed:** 2026-05-09 (M5.C Code Sweep — Abish)
**Surface:** `src/ToastRevival.Dashboard/src/pages/Assets.tsx` — file input `accept` attribute and drop handler
**Issue:** The frontend accepts any `image/*` MIME type in addition to the explicit extension list. The backend validates extension strictly (`.jpg/.jpeg/.png/.gif/.webp`). Frontend MIME check is UX-only — the backend is the real gate.
**Fix:** Acceptable. Backend extension whitelist is the authoritative check.
**Blocking:** No.

### INFO-M5D-001 — **RESOLVED 2026-05-09 (M6)**

**Resolved:** 2026-05-09 (M6). `CsvHelper` static class created in `Utilities/CsvHelper.cs`. `AuditController` and `NotificationsController` updated to use `CsvHelper.Cell()`. Private `CsvCell` methods removed from both controllers.

---

### INFO-M5D-001 (low) — CsvCell helper duplicated

**Filed:** 2026-05-09 (M5.D Code Sweep — Abish)
**Surface:** `Controllers/AuditController.cs`, `Controllers/NotificationsController.cs`
**Issue:** `CsvCell` private static helper implemented identically in both controllers.
**Fix:** Extract to `CsvHelper` static class in a `Utilities/` namespace at M6+.
**Blocking:** No.

### INFO-M5D-002 — **RESOLVED 2026-05-09 (M6)**

**Resolved:** 2026-05-09 (M6). `IX_AuditLogs_Timestamp` index added in M6Billing migration.

---

### INFO-M5D-002 (M6+) — No index on AuditLog.Timestamp

**Filed:** 2026-05-09 (M5.D Code Sweep — Abish)
**Surface:** `Data/AppDbContext.cs` — `AuditLog` entity model
**Issue:** `GET /api/audit/export?days=90` does a full-table scan on `AuditLogs`. At MVP scale (thousands of entries) acceptable; at production scale (millions of rows across many tenants) this becomes a concern.
**Fix:** Add `e.HasIndex(l => l.Timestamp)` in `OnModelCreating` + generate migration at M6+.
**Blocking:** No.

### INFO-M5D-003 (acceptable) — CSV injection risk in audit export

**Filed:** 2026-05-09 (M5.D Code Sweep — Abish)
**Surface:** `Controllers/AuditController.cs::BuildAuditCsv`
**Issue:** Audit log action strings (e.g. `notification.send`) are server-generated and safe. However, if an attacker can control an `AuditLog.Action` value, they could inject formula characters (`=CMD()`). The export is admin-only, limiting blast radius.
**Fix:** Acceptable for current scope. If user-supplied content ever reaches `Action` fields, prefix each cell with a tab character to neutralize formula injection. M9 review item.
**Blocking:** No.

### INFO-M5D-004 (M9 scale) — PdfExportService.GeneratePdf() is synchronous

**Filed:** 2026-05-09 (M5.D Code Sweep — Abish)
**Surface:** `Services/PdfExportService.cs` — both `GenerateAuditLogPdf` and `GenerateDeliveryReportPdf`
**Issue:** QuestPDF's `.GeneratePdf()` is a synchronous call that blocks the ASP.NET request thread. For an MSP admin exporting a 90-day audit log of 10K+ entries, this could block for >500ms. Acceptable for infrequent admin export; would become a concern under concurrent export load.
**Fix:** Wrap in `await Task.Run(() => _pdf.GenerateXxxPdf(...))` at the controller call site at M9 scale.
**Blocking:** No.

### INFO-M5-003 (low) — TemplatesController.BuildDefaultTemplates couples Auth and Templates

**Filed:** 2026-05-09 (M5.A Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Controllers/TemplatesController.cs::BuildDefaultTemplates`
**Issue:** `internal static` method on a controller is an unusual pattern. Creates implicit coupling between AuthController and TemplatesController.
**Fix:** Extract to a `TemplateSeederService` or `DefaultTemplates` static class at M6+.
**Blocking:** No. One caller only (AuthController.Register).

### INFO-M6-001 (M9 — deploy doc) — Stripe keys are placeholder values in appsettings.json

**Filed:** 2026-05-09 (M6 Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/appsettings.json`
**Issue:** `Stripe:SecretKey`, `Stripe:WebhookSecret`, `Stripe:ProPriceId`, `Stripe:EnterprisePriceId` are placeholder strings. Production must override via environment variables: `Stripe__SecretKey`, `Stripe__WebhookSecret`, `Stripe__ProPriceId`, `Stripe__EnterprisePriceId`. BillingController checks for placeholder prefix and returns 503 — safe degradation.
**Fix:** Document in M9 DEPLOY.md alongside existing JWT key guidance.
**Blocking:** No. Test and production configs handled via env vars.

### INFO-M6-002 (M9 scale) — SyncConsumedCountAsync on every plan fetch

**Filed:** 2026-05-09 (M6 Code Sweep — Abish)
**Surface:** `Controllers/BillingController.cs::Plan`
**Issue:** `SyncConsumedCountAsync` executes one extra DB query per `GET /api/billing/plan` call. At MSP scale (infrequent admin page loads) acceptable.
**Fix:** Add short-TTL in-memory cache keyed by tenantId at M9.
**Blocking:** No.

### INFO-M6-003 (M9 scale) — Invoice list makes live Stripe API call per request

**Filed:** 2026-05-09 (M6 Code Sweep — Abish)
**Surface:** `Controllers/BillingController.cs::Invoices`
**Issue:** `InvoiceService.ListAsync` is a live Stripe API call on every request. No caching.
**Fix:** Cache with 5-minute TTL per tenantId at M9.
**Blocking:** No.

### INFO-M6-004 (M7 design) — Onboarding.tsx welcome step uses emoji placeholder icons

**Filed:** 2026-05-09 (M6 Code Sweep — Abish)
**Surface:** `src/ToastRevival.Dashboard/src/pages/Onboarding.tsx`
**Issue:** Welcome step uses emoji (🔔, 📋, 📦, 🚀). Diana's standing preference: no emojis in UI. These are placeholder scaffolding — Diana will provide SVG replacements with the M7 onboarding design spec.
**Fix:** Replace with SVGs at M7.
**Blocking:** No. Functional for internal testing.

---

## Resolved

- **FIX-MSIX-004** (medium) - 2026-05-08, commit `6e3495c`. Packaged MSIX install did not fire toasts because `<com:ExeServer>` was missing `Arguments="----AppNotificationActivated:"`. Patched, signed, installed; visible toast verified on Win11 lab with button-click routing through `NotificationInvoked`. See entry above for full root-cause detail.

- **INFO-D5-001** (low) - 2026-05-09 (M2.A). Named mutex (`Local\Toast2IT.ToastNotification.PrimaryWorker`) gates primary worker mode. Activation + diagnostic modes short-circuit before mutex acquisition. See entry above.

- **INFO-MSIX-004-D** (low) - 2026-05-09 (M2.A). Activation-handler short-circuits before SignalR; routes button-click events to new REST `POST /api/notifications/{id}/interactions` endpoint. See entry above.

- **INFO-M2A-003** (M2.B) - 2026-05-09 (M2.B). Orphan `Sending` notification recovery sweep at queue-service startup. Marks stuck notifications Failed but leaves Pending deliveries Pending so catch-up can deliver. See entry above.

- **INFO-M2A-004** (M2.B) - 2026-05-09 (M2.B). Agent notificationId dedup via `MemoryCache` 1-hour sliding. Shared between hub-push and catch-up paths. See entry above.

- **FIX-M2B-001** (BLOCKING) - 2026-05-09 (M2.B Code Sweep, patched pre-commit). Agent `_lastCatchupSince` now nullable; first call omits `since` query param so server drains full Pending backlog. See entry above.
