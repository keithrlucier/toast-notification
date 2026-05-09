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

### INFO-M2B-004 (M3) — Agent dedup MemoryCache is unbounded

**Filed:** 2026-05-09 (M2.B Code Sweep)
**Surface:** `src/ToastRevival.Agent/AgentClient.cs::AgentHubClient._renderedCache`
**Issue:** No `SizeLimit` on `MemoryCacheOptions`. ~100 bytes per entry × notification volume × agent uptime — a long-running agent on a high-volume tenant could grow the cache unboundedly. At 10K entries (~1MB) it's still fine; at 1M entries (~100MB) it isn't.
**Fix:** Set `SizeLimit = 50_000` on `MemoryCacheOptions` and `Size = 1` on each entry's `MemoryCacheEntryOptions`.
**Blocking:** No.

### INFO-M2B-005 (M3) — Catch-up rate limit during reconnect storms

**Filed:** 2026-05-09 (M2.B Code Sweep)
**Surface:** `src/ToastRevival.Api/Controllers/NotificationsController.cs::GetPending` `[EnableRateLimiting("device-per-hour")]`
**Issue:** Catch-up endpoint shares the `device-per-hour` (10 req/hr fixed window) policy with `ReportInteraction`. A flaky network with frequent reconnects could exhaust the budget. Fire-and-forget semantics mean a 429 just delays delivery to the next successful reconnect — not catastrophic — but a separate higher-budget policy for catch-up could improve real-world behavior on bad networks.
**Fix:** Add `device-catchup-per-hour` policy at e.g. 60/hr. Or accept current limit (10/hr is plenty for normal connectivity).
**Blocking:** No.

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

## Resolved

- **FIX-MSIX-004** (medium) - 2026-05-08, commit `6e3495c`. Packaged MSIX install did not fire toasts because `<com:ExeServer>` was missing `Arguments="----AppNotificationActivated:"`. Patched, signed, installed; visible toast verified on Win11 lab with button-click routing through `NotificationInvoked`. See entry above for full root-cause detail.

- **INFO-D5-001** (low) - 2026-05-09 (M2.A). Named mutex (`Local\Toast2IT.ToastNotification.PrimaryWorker`) gates primary worker mode. Activation + diagnostic modes short-circuit before mutex acquisition. See entry above.

- **INFO-MSIX-004-D** (low) - 2026-05-09 (M2.A). Activation-handler short-circuits before SignalR; routes button-click events to new REST `POST /api/notifications/{id}/interactions` endpoint. See entry above.

- **INFO-M2A-003** (M2.B) - 2026-05-09 (M2.B). Orphan `Sending` notification recovery sweep at queue-service startup. Marks stuck notifications Failed but leaves Pending deliveries Pending so catch-up can deliver. See entry above.

- **INFO-M2A-004** (M2.B) - 2026-05-09 (M2.B). Agent notificationId dedup via `MemoryCache` 1-hour sliding. Shared between hub-push and catch-up paths. See entry above.

- **FIX-M2B-001** (BLOCKING) - 2026-05-09 (M2.B Code Sweep, patched pre-commit). Agent `_lastCatchupSince` now nullable; first call omits `since` query param so server drains full Pending backlog. See entry above.
