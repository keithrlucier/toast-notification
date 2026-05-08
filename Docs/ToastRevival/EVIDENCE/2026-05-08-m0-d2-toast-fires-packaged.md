# M0 D2 - Toast Fires from Packaged Context - 2026-05-08

## Summary

Signed `ToastNotification.Agent-0.2.0.3.msix` installed on Win11 lab via `Add-AppxPackage`. Single visible toast fires from packaged Start menu launch; "Acknowledge" button click routes through the in-process `NotificationInvoked` handler with the expected argument payload. M0 D2 closed.

Keith's confirmation in chat: "Worked spawned a single notification."

## DiagLog Trace

Full agent.log from `%LOCALAPPDATA%\Packages\Toast2IT.ToastNotification.Agent_8gxm9tzcy3sby\LocalState\agent.log`:

```
2026-05-08T14:34:46.5588174Z ==> Toast Notification agent start; pid=7584; args=[];
   baseDir=C:\Program Files\WindowsApps\Toast2IT.ToastNotification.Agent_0.2.0.3_x64__8gxm9tzcy3sby\;
   packaged=True;
   logPath=C:\Users\COLO\AppData\Local\Packages\Toast2IT.ToastNotification.Agent_8gxm9tzcy3sby\LocalState\agent.log
2026-05-08T14:34:46.9564655Z Calling AppNotificationManager.Default.Register()...
2026-05-08T14:34:46.9658920Z Register() returned without throwing.
2026-05-08T14:34:46.9682462Z Assets resolved:
   hero=file:///C:/Program Files/WindowsApps/Toast2IT.ToastNotification.Agent_0.2.0.3_x64__8gxm9tzcy3sby/Assets/toast-hero.png;
   logo=file:///.../toast-logo.png;
   inline=file:///.../toast-inline.png
2026-05-08T14:34:46.9833876Z Notification built. Template=Plain; Scenario=Default; Sound=(none); Buttons=1
2026-05-08T14:34:46.9837994Z Calling AppNotificationManager.Default.Show()...
2026-05-08T14:34:47.0208939Z Show() returned without throwing. Notification.Id=43; ExpiresOnReboot=False
2026-05-08T14:34:49.5407053Z NotificationInvoked: argument='action=acknowledge;source=m0a;template=Plain'
2026-05-08T14:34:57.0891233Z EXIT 0: clean.
2026-05-08T14:34:57.0901926Z Calling AppNotificationManager.Default.Unregister()...
2026-05-08T14:34:57.0917178Z Unregister() returned.

[second cycle - Keith launched again from Start menu]
2026-05-08T14:35:04.1476030Z ==> Toast Notification agent start; pid=4692; args=[]; ...
2026-05-08T14:35:04.1936366Z Calling AppNotificationManager.Default.Register()...
2026-05-08T14:35:04.2033845Z Register() returned without throwing.
2026-05-08T14:35:04.2206760Z Notification built. Template=Plain; Scenario=Default; Sound=(none); Buttons=1
2026-05-08T14:35:04.2211261Z Calling AppNotificationManager.Default.Show()...
2026-05-08T14:35:04.2445462Z Show() returned without throwing. Notification.Id=44; ExpiresOnReboot=False
2026-05-08T14:35:09.1266971Z NotificationInvoked: argument='action=acknowledge;source=m0a;template=Plain'
```

## What This Validates

- **Packaged context detection works.** `IsPackaged=True`, `baseDir` is in `C:\Program Files\WindowsApps\...`, log path is in the per-package LocalState. DiagLog's WinRT-throw fallback never triggered.
- **`AppNotificationManager.Default.Register()` succeeds in packaged context** with the COM activator declarations (FIX-MSIX-004 fix landed): namespaces, IgnorableNamespaces, Extensions inside `<Application>`, both CLSIDs identical (`7FA7762F-41EC-4D72-9F06-58964AB36FEA`), and the `Arguments="----AppNotificationActivated:"` token on `<com:ExeServer>`.
- **`Show()` returns clean and the toast actually renders.** Confirmed by Keith ("spawned a single notification") and indirectly by the `NotificationInvoked` event firing 2.5 seconds after `Show()` — Keith clicked the toast's "Acknowledge" button; that only fires if the toast is on screen and clickable.
- **Activation routing works in-process.** Button click on the rendered toast triggered the `NotificationInvoked` event handler before the agent's wait-for-activation timer expired. Argument payload arrived in the expected `action=acknowledge;source=m0a;template=Plain` format that `ToastTemplateBuilder.Build` writes via `AppNotificationButton.AddArgument(...)`.
- **Single notification, no duplicates.** Each Show() call produced exactly one toast. The Arguments token did not cause a re-launch loop.
- **Unregister() exits cleanly.** No process hang, no orphan COM registration.

## Lessons Locked In (CONTEXT.md, project context, persona memory)

1. **Packaged-WinAppSDK toast activator declarations** require ALL of:
   - `xmlns:com` and `xmlns:desktop` on `<Package>`.
   - `com desktop` in `IgnorableNamespaces`.
   - `<Extensions>` block INSIDE `<Application>`, NOT at Package level (different categories belong in different parents — COM server and toast activation are app-scoped).
   - `<com:ExeServer Arguments="----AppNotificationActivated:">` — the four-dash sentinel is non-optional. Without it, `AppNotificationManager.Default.Register()` throws `COMException 0x80070490` (`HRESULT_FROM_WIN32(ERROR_NOT_FOUND)`).
   - `<com:Class Id>` and `<desktop:ToastNotificationActivation ToastActivatorCLSID>` byte-for-byte identical CLSIDs.
2. **Toast Activator CLSID for this product**: `7FA7762F-41EC-4D72-9F06-58964AB36FEA`. Locked in `Docs/ToastRevival/CONTEXT.md` -> Toast Activator Class ID. Immutable post-Store-flight.
3. **Diagnostic logging carried alongside a hypothesis-driven fix earns its keep.** DiagLog isolated FIX-MSIX-004 to the exact failing API call in one install cycle. Recommended pattern for any future packaging or COM activation surface: ship the trace in the same rebuild as the fix.

## Verified This Session
- 0.2.0.3 install via `Add-AppxPackage -ForceUpdateFromAnyVersion` from 0.2.0.2 baseline.
- Start menu tile launch (non-elevated).
- Visible toast on first paint.
- `NotificationInvoked` callback wired (button click → handler).
- `Unregister()` exits cleanly.

## Deferred / Follow-Up
- **INFO-MSIX-004-D** (M2): detect `----AppNotificationActivated:` arg in `AgentOptions.Parse`; route to one-shot activation handler instead of falling through to default Plain re-send. Not exercised in this validation (Keith's clicks were captured by the in-process handler before exit).
- **INFO-MSIX-004-A/B/C** (M1/M2 hygiene): gate DiagLog behind `--diag` flag or add rotation before launch.
- **FIX-MSIX-001** (M0 D5): `TargetPlatformVersion` -> 10.0.22621.0 before Store flight.
- **FIX-MSIX-002** (M0 D4): manifest `MinVersion` vs runtime gate divergence.
- **Win10 1809 install** validation: no lab machine; deferred to M0 D4 GPO matrix.
