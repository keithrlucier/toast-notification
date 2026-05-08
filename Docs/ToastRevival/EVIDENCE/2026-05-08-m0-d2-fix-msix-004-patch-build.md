# M0 D2 / FIX-MSIX-004 Patch Build - 2026-05-08

## Summary

FIX-MSIX-004 patch built locally as `ToastNotification.Agent-0.2.0.2.msix` (unsigned). Adds the COM activator + toast notification activation extension blocks that the packaged WinAppSDK toast pipeline requires, plus file-based diagnostic logging in the agent so the next install attempt produces a readable trace regardless of outcome.

Awaiting Keith's signing turn (Thales token + Sectigo OV) and Win11 lab install validation.

## Hypothesis Recap

`AppNotificationManager.Default.Register()` in a packaged context does NOT auto-inject the activator CLSID into `HKCU\SOFTWARE\Classes\CLSID\...` the way it does for unpackaged. The packaged framework reads the activator CLSID from the manifest. Without `<com:Extension>` + `<desktop:Extension>` declarations the registration succeeds at the API surface but the activation channel never wires, so `Show()` returns clean and produces no visible toast.

## CLSID Locked

`7FA7762F-41EC-4D72-9F06-58964AB36FEA`

Generated 2026-05-08 via `[guid]::NewGuid()`. Documented in `Docs/ToastRevival/CONTEXT.md` -> Toast Activator Class ID.

Used identically in:

- `<com:Extension Category="windows.comServer">` -> `<com:Class Id="7FA7762F-41EC-4D72-9F06-58964AB36FEA" />`
- `<desktop:Extension Category="windows.toastNotificationActivation">` -> `ToastActivatorCLSID="7FA7762F-41EC-4D72-9F06-58964AB36FEA"`

## Manifest Patch (lessons)

Initial attempt placed `<Extensions>` at the **Package level** (after `</Applications>`, before `<Capabilities>`) per the FIX-LIST plan. That failed `MakeAppx` schema validation:

```
error C00CE014: App manifest validation error: The app manifest must be valid
as per schema: Reason: Element '{...desktop/windows10}Extension' is unexpected
according to content model of parent element '{...foundation/windows10}Extensions'.
```

Correct placement is **inside `<Application>`** per Microsoft's quickstart for packaged WinAppSDK toast activation. FIX-LIST.md updated to reflect the corrected shape.

## DiagLog (Program.cs)

New `DiagLog` static class:

- `Init()` -> tries `Windows.Storage.ApplicationData.Current.LocalFolder.Path` (packaged); falls back to `%LOCALAPPDATA%\Toast2IT\Toast Notification` (unpackaged) on WinRT throw.
- `Write(string)` -> appends an ISO-8601 UTC-timestamped line to `agent.log`. Per-process lock; cross-process append is "best effort interleave" which is acceptable for diagnostics.
- All exceptions in Init/Write are swallowed silently — a diagnostic logger must never crash its host.

Trace points (every exit and every WinAppSDK toast call):

```
==> Toast Notification agent start; pid=...; args=[...]; baseDir=...; packaged=...; logPath=...
EXIT 2 / EXIT 3 / EXIT 4 (gates)
Calling AppNotificationManager.Default.Register()...
Register() returned without throwing.
Assets resolved: hero=... logo=... inline=...
Notification built. Template=... Scenario=... Sound=... Buttons=...
Calling AppNotificationManager.Default.Show()...
Show() returned without throwing. Notification.Id=... ExpiresOnReboot=...
NotificationInvoked: argument='...'    <-- if user clicks
Calling AppNotificationManager.Default.Unregister()...
Unregister() returned.
EXIT 0: clean.   <-- happy path
EXIT 1: exception ...   <-- failure path
```

## Build Output

```
.\scripts\build-msix.ps1 -Version 0.2.0.2 -SkipAssetGeneration
==> Stamping manifest version 0.2.0.2 (in-memory)
==> Building MSIX (-c Release -p:Platform=x64 -p:WindowsPackageType=MSIX)
   ...
   ToastRevival.Agent -> C:\SOURCE\toast\artifacts\installer\msix\ToastNotification.Agent-0.2.0.2.msix
Build succeeded.
1 Warning(s) (mspdbcmf.exe; cosmetic, FIX-MSIX-003)
0 Error(s)

Path : C:\SOURCE\toast\artifacts\installer\msix\ToastNotification.Agent-0.2.0.2.msix
Size : 63.53 MB
```

## Post-Build Manifest Verification

Extracted `AppxManifest.xml` from the produced .msix. Confirmed:

- `Identity Version="0.2.0.2"`.
- `Publisher="CN=&quot;Toast2IT, LLC&quot;, O=&quot;Toast2IT, LLC&quot;, S=Florida, C=US"` (matches cert subject across all four RDNs).
- `xmlns:com` and `xmlns:desktop` declared on `<Package>`.
- `IgnorableNamespaces="uap rescap com desktop build"` (build pipeline added `build` for its own metadata).
- Extensions block survived inside `<Application>`.
- Both CLSIDs identical: `7FA7762F-41EC-4D72-9F06-58964AB36FEA`.
- `ExeServer Executable="ToastNotification.Agent.exe"` matches `Application Executable`.
- No regression to `Capabilities` (still `runFullTrust` only).

## Code Sweep (Abish)

SHIP. Three INFO findings:

- **INFO-MSIX-004-A**: DiagLog has no rotation / size cap. Acceptable for M0 D2 diagnostics; flag for M1/M2 cleanup.
- **INFO-MSIX-004-B**: Cross-process concurrent log writes can interleave. Acceptable for diagnostics.
- **INFO-MSIX-004-C**: DiagLog.Write swallows all exceptions. Intentional design.

## Hand-Off Steps for Keith

1. **Sign**:
   ```
   .\scripts\sign-msix.ps1 -Path artifacts\installer\msix\ToastNotification.Agent-0.2.0.2.msix
   ```
   SafeNet token plugged in + unlocked. PIN dialog will pop.

2. **Install on Win11 lab**:
   ```
   Add-AppxPackage -Path <signed-msix> -ForceUpdateFromAnyVersion
   ```
   The `-ForceUpdateFromAnyVersion` is needed because 0.2.0.1 is still installed.

3. **Launch from Start menu tile** (NON-elevated; do not run from elevated PowerShell — the IsElevated guard at Program.cs exits 3).

4. **Capture evidence** (whichever applies):
   - Visible toast banner (bottom-right): screenshot.
   - Action Center entry (Win+N): screenshot.
   - Settings -> System -> Notifications -> Toast Notification -> Notification history: screenshot.
   - `agent.log` from `%LOCALAPPDATA%\Packages\Toast2IT.ToastNotification.Agent_8gxm9tzcy3sby\LocalState\agent.log`.

5. **Ship the log back regardless of outcome.** If toast fires: confirms hypothesis. If toast still doesn't fire: log shows whether Register threw, what Show did, what AUMID was used — feeds into the fallback diagnostic tree in FIX-LIST FIX-MSIX-004.

## Build Artifact

`artifacts/installer/msix/ToastNotification.Agent-0.2.0.2.msix` (63.53 MB, UNSIGNED).

`artifacts/` is `.gitignored`; the file does not enter source control. The change set that produced it (manifest patch + DiagLog + version bump + CONTEXT.md doc) is committed.
