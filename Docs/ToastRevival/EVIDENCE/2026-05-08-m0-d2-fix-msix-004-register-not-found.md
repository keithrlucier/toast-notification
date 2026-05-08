# M0 D2 / FIX-MSIX-004 - Register() ERROR_NOT_FOUND, Arguments Token Fix - 2026-05-08

## Result of 0.2.0.2 Install

DiagLog earned its keep on the first packaged install attempt. Trace from Win11 lab:

```
2026-05-08T14:20:58.8286500Z ==> Toast Notification agent start; pid=11104; args=[];
   baseDir=C:\Program Files\WindowsApps\Toast2IT.ToastNotification.Agent_0.2.0.2_x64__8gxm9tzcy3sby\;
   packaged=True;
   logPath=C:\Users\COLO\AppData\Local\Packages\Toast2IT.ToastNotification.Agent_8gxm9tzcy3sby\LocalState\agent.log
2026-05-08T14:20:58.9181182Z Calling AppNotificationManager.Default.Register()...
2026-05-08T14:20:58.9980134Z EXIT 1: exception System.Runtime.InteropServices.COMException: Element not found.

Element not found.

System.Runtime.InteropServices.COMException (0x80070490): Element not found.

   at WinRT.ExceptionHelpers.<ThrowExceptionForHR>g__Throw|38_0(Int32 hr)
   at WinRT.ExceptionHelpers.ThrowExceptionForHR(Int32 hr)
   at ABI.Microsoft.Windows.AppNotifications.IAppNotificationManagerMethods.Register(IObjectReference _obj)
   at Microsoft.Windows.AppNotifications.AppNotificationManager.Register()
   at Program.<Main>$(String[] args)
```

What it tells us:

- **Packaged context: confirmed.** baseDir is in `C:\Program Files\WindowsApps\...`, log went to LocalState. The DiagLog packaged/unpackaged detection works.
- **Register() throws before Show() is reached.** The original FIX-MSIX-004 hypothesis (Show silently no-ops without manifest extensions) was wrong; Register() itself fails.
- **HRESULT 0x80070490 = HRESULT_FROM_WIN32(ERROR_NOT_FOUND).** The framework's COM activator class registration lookup returns NOT_FOUND.

## Root Cause

The `<com:ExeServer>` element in Package.appxmanifest is missing the `Arguments="----AppNotificationActivated:"` attribute. WinAppSDK's `AppNotificationManager::Register()` looks up the activator COM class registration in the manifest and uses the four-dash sentinel as the marker for "this is the toast activator entry point." Without it, the framework's class registration step fails with `ERROR_NOT_FOUND`.

Microsoft's official packaged-WinAppSDK quickstart sample includes the attribute:

```xml
<com:ExeServer Executable="myapp.exe" Arguments="----AppNotificationActivated:" DisplayName="Toast activator">
  <com:Class Id="..." DisplayName="Toast activator" />
</com:ExeServer>
```

We had everything else right (CLSID match, Application-level Extensions placement, namespaces, IgnorableNamespaces). Just missed this one attribute.

## Patch (0.2.0.3)

```
src/ToastRevival.Agent/Package.appxmanifest:
  - Identity Version: 0.2.0.2 -> 0.2.0.3
  - <com:ExeServer ... DisplayName="Toast Notification Activator">
    +<com:ExeServer ... Arguments="----AppNotificationActivated:" DisplayName="Toast Notification Activator">

scripts/build-msix.ps1:
  - default $Version: 0.2.0.2 -> 0.2.0.3
```

DiagLog stays in. CLSID stays at `7FA7762F-41EC-4D72-9F06-58964AB36FEA`.

## Build Verification

```
.\scripts\build-msix.ps1 -Version 0.2.0.3 -SkipAssetGeneration
   ToastRevival.Agent -> C:\SOURCE\toast\artifacts\installer\msix\ToastNotification.Agent-0.2.0.3.msix
Build succeeded.
   1 Warning(s) (mspdbcmf cosmetic, FIX-MSIX-003)
   0 Error(s)
   Time Elapsed 00:00:13.22

Path : C:\SOURCE\toast\artifacts\installer\msix\ToastNotification.Agent-0.2.0.3.msix
Size : 63.53 MB
```

Post-build manifest extracted; Arguments token survived intact.

## Code Sweep (Abish)

SHIP. One INFO finding:

- **INFO-MSIX-004-D**: Agent's AgentOptions.Parse silently ignores unknown args. When the user clicks a toast button on a deployed notification, the framework launches the exe with `----AppNotificationActivated:...` prepended; the agent will fall through to a default Plain-template re-send instead of routing to the NotificationInvoked handler. Not blocking for M0 D2 (we still need Register() to succeed first); flag for M2 agent work to detect the activation arg and route appropriately.

## Hand-Off

Same flow as 0.2.0.2 except the file path:

```powershell
.\scripts\sign-msix.ps1 -Path artifacts\installer\msix\ToastNotification.Agent-0.2.0.3.msix
Add-AppxPackage -Path <signed-msix> -ForceUpdateFromAnyVersion
```

Then launch from Start menu (non-elevated) and ship `agent.log` from
`%LOCALAPPDATA%\Packages\Toast2IT.ToastNotification.Agent_8gxm9tzcy3sby\LocalState\`.

If Register() now succeeds and Show() runs clean → toast should fire. If Register() still throws or throws a different HRESULT → the log tells us the next branch.

## Standing Rule Updates

- For any packaged-WinAppSDK toast activator declaration, the `<com:ExeServer>` element MUST include `Arguments="----AppNotificationActivated:"`. The four-dash sentinel is what the framework uses to identify the toast activator surface. Without it, `Register()` returns `ERROR_NOT_FOUND` (0x80070490). Documented in CONTEXT.md "Toast Activator Class ID" section.
- Code Sweep Step 4 standing check for any new toast COM activator manifest: verify the Arguments token is present alongside CLSID match and Extensions placement. The token is doc-required, not optional.
