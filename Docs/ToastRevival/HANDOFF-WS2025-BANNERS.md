# Handoff: Windows Server 2025 — banners not rendering

**Session date:** 2026-05-12
**Status:** Original "device never registers" bug **FIXED in 0.4.7 (deployed prod)**.
Follow-on issue — visual toast banners don't render on Server 2025 — **diagnosed but NOT yet fixed**.
Next session needs to ship a 0.4.8 with a legacy-API rendering path.

**Read this whole document before touching code.** Keith is angry — we shipped a Server 2025 fix without validating the full delivery chain. Don't repeat that mistake.

---

## TL;DR for the next agent

1. Read the **"What was fixed in 0.4.7"** section to understand what's currently in prod.
2. Read the **"What's still broken"** section to understand the remaining bug.
3. Read the **"Diagnosed root cause"** section — we have evidence, not a theory.
4. Implement the fix in the **"Proposed 0.4.8 fix"** section.
5. Honor Keith's anger. Don't ask permission, don't equivocate, don't promise Server 2025 works until you've verified end-to-end with banner visible on his box.

---

## What was fixed and shipped in 0.4.7

**Commit:** `cd27ef1 fix: agent registers on Windows Server 2025 (0.4.7)`
**Status in prod:** ✅ Live at https://toastnotification.com/downloads/ToastNotification.msi (SHA256 `5fa1c6e236d43e701c33cf61c168858b1308e893168454c21d2993831ae387b4`)
**Velopack feed:** ✅ Live at https://releases.toastnotification.com/agent/win-x64/releases.win.json

**Root cause that was fixed:** The `IsElevated()` exit-3 gate in `src/ToastRevival.Agent/Program.cs:87` killed the agent before `/api/devices/register` was called. On Windows Server, the built-in Administrator account has UAC disabled by default — the scheduled task at `LeastPrivilege` couldn't down-token to standard, so the agent ran with the unfiltered admin token and `IsInRole(Administrator)` returned true. Confirmed by `schtasks /Query` showing `Last Result: 3`.

**Fix:**
- Demoted `IsElevated()` exit-3 to a `WARN` log (agent now continues even when elevated).
- Wrapped `AppNotificationManager.Default.Register()` in try/catch in `PrimaryMode` so SignalR connects (and the device shows online in the dashboard) even if `Register()` fails on locked-down configs.
- Made `--diag` actually print to a parent console via `AttachConsole(ATTACH_PARENT_PROCESS)` and tee output to `%TEMP%\toastnotification-diag.txt` (the binary is `WinExe` so stdout was previously dropped on the floor — the original silent-diag bug that hid this for weeks).
- Bumped 0.4.6.1 → 0.4.7.

**Verified in prod:** Keith ran his install command on Server 2025 (COL-BU-001), device `530e0bc6-b57f-4567-9a77-b942183a74bc` registered, hub connected, agent stayed online. agent.log shows `Registration OK`, `Hub started: state=Connected`, `PrimaryMode: agent online`.

---

## What's still broken

**Symptom:** On Windows Server 2025 (`COL-BU-001`, full Desktop Experience, ScreenConnect console session), agent reports `Show()` succeeded for both hub-pushed notifications AND the tray "Send test" button — but **no toast banner appears anywhere on the desktop**, AND the toast does not appear in Action Center either.

**Keith's quote (verbatim):** _"Notifications DO NOT WORK on windows server the way this app is designed. They didnt work with the legacy app design either and that fucking sucks. I asked specifically if this would be supported and you fed me some bullshit"_

He's right to be angry. We don't have a record in memory of what was promised when he asked, but the agent shipped to him with a delivery model that has never worked on Server SKUs. Own this in your reply.

---

## What we ruled out (don't re-test these)

This is a partial list of things we already verified are NOT the cause. Don't waste tokens re-checking them.

| Suspect | Status | Evidence |
|---|---|---|
| OS version too old | Ruled out | Server 2025 = 10.0.26100; gate is 10.0.19041 |
| `IsElevated()` exit-3 | **Fixed in 0.4.7** | `Last Result: 3` in schtasks before fix; clean run after |
| Missing bootstrap.json | Ruled out | Keith uses prefilled msiexec with CLIENTID + SERVERURL + ENROLLMENTKEY |
| Scheduled task didn't fire | Ruled out | Last Run Time confirmed in schtasks; agent.log shows successful start |
| `Register()` threw | Ruled out | agent.log: `PrimaryMode: Register() returned.` |
| `Show()` threw | Ruled out | agent.log: `hub: rendered notificationId=…; title='Company Announcement'` AND `PrimaryMode: test notification sent from tray.` Both calls returned without exception. |
| `WpnUserService_*` not running | Ruled out | `WpnUserService_2f49cbfc2 Running Automatic` |
| `ToastEnabled = 0` | Ruled out | After we wrote `ToastEnabled=1` HKCU, still nothing |
| `NoToastApplicationNotification` GP suppression | Ruled out | Empty in HKLM and HKCU policies subkeys |
| Per-app block under Notifications\Settings | Ruled out | Settings subkey doesn't exist for our AUMID |
| Focus / Do Not Disturb | Ruled out | Keith toggled it on and off, no change |
| Settings UI master toggle "Notifications: Off" | Ruled out | Screenshot shows it's On |
| RDP session limitation | Ruled out | Keith uses ScreenConnect → console session |
| Server Core (no Desktop Experience) | Ruled out | `WindowsInstallationType: Server` (full Desktop Experience) |
| `CustomActivator` CLSID missing on AUMID | We wrote it manually; no change | Got us partway but not enough |

---

## Diagnosed root cause (with evidence)

Keith sent a screenshot of **Settings > System > Notifications**. The "Notifications from apps and other senders" list showed:

- ✅ Veeam Backup & Replication Console — On — Banners, Sounds
- ✅ Windows host process (Rundll32) — On — Banners, Sounds
- ✅ AutoPlay — On — Banners, Sounds
- ✅ Settings — On — Banners, Sounds
- ❌ **Toast Notification (our app) — NOT IN THE LIST**

**This is the smoking gun.** Our app is not registered with Server 2025's per-app notifier enumeration system. Apps that aren't in this list don't get their toasts rendered, even when their `Show()` calls return successfully.

**Why:** Our agent uses `Microsoft.Windows.AppNotifications.AppNotificationManager` (WinAppSDK 1.x). Veeam, Rundll32, AutoPlay, and Settings all use the older **`Windows.UI.Notifications.ToastNotificationManager`** (WinRT, Win8+). Server 2025's notification subsystem enumerates apps registered via the legacy WinRT API; it does **not** appear to enumerate apps that registered via the newer WinAppSDK API path on this Server build.

The newer API succeeds at `Show()` from the SDK's perspective, but Windows then has nowhere to render to because our app isn't in the registered-senders list. The toast is silently dropped before reaching either banner display or Action Center.

---

## Proposed 0.4.8 fix

**Strategy:** Keep WinAppSDK for COM activation (button click round-trip via the COM activator CLSID we declare in `Package.appxmanifest:48`). Switch every `Show()` call to use the legacy `Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(AUMID).Show(...)` path.

This works on Win10/11 client SKUs (legacy API has been stable since Windows 8) and gets us into Server 2025's enumeration list.

### Two pieces required

**Piece 1 — Code:** Reuse the existing `ToastTemplateBuilder` (which produces an `AppNotification`). Extract its XML payload via `AppNotification.Payload`, load into a `Windows.Data.Xml.Dom.XmlDocument`, construct a `Windows.UI.Notifications.ToastNotification`, show via the legacy notifier:

```csharp
// New file: src/ToastRevival.Agent/LegacyToastShim.cs
using Microsoft.Windows.AppNotifications;

namespace ToastRevival.Agent;

internal static class LegacyToastShim
{
    private const string Aumid = "Toast2IT.ToastNotification";

    public static void Show(AppNotification notification)
    {
        var xml = notification.Payload; // XML the WinAppSDK builder produced
        var doc = new Windows.Data.Xml.Dom.XmlDocument();
        doc.LoadXml(xml);

        var legacy = new Windows.UI.Notifications.ToastNotification(doc);
        var notifier = Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(Aumid);
        notifier.Show(legacy);
    }
}
```

**Replace 3 callsites:**

1. `src/ToastRevival.Agent/AgentClient.cs:451` — hub-pushed notifications (the main path)
   - Change `AppNotificationManager.Default.Show(notification)` → `LegacyToastShim.Show(notification)`
2. `src/ToastRevival.Agent/Program.cs:351` — `DiagnosticMode.RunAsync` template render
   - Same swap
3. `src/ToastRevival.Agent/Program.cs:483` — tray "Send test notification"
   - Same swap

**Keep:** `AppNotificationManager.Default.Register()` for COM activation. Button clicks on toasts still need the WinAppSDK COM round-trip via `ActivationMode.RunAsync` (Program.cs:167).

**Piece 2 — Installer:** Legacy `ToastNotificationManager` requires the AUMID be associated with either (a) a Start Menu shortcut whose `System.AppUserModel.ID` property is set to the AUMID, or (b) a registered COM activator. We have the Start Menu shortcut already in `installer/ToastRevival.Agent.Setup.wxs:58-72` but it has no AUMID property. Add it:

```xml
<Shortcut Id="StartMenuAgentShortcut"
          Name="Toast Notification"
          Target="[INSTALLFOLDER]ToastNotification.Agent.exe"
          WorkingDirectory="INSTALLFOLDER"
          Description="Launch the Toast Notification agent.">
  <ShortcutProperty Key="System.AppUserModel.ID" Value="Toast2IT.ToastNotification" />
  <ShortcutProperty Key="System.AppUserModel.ToastActivatorCLSID" Value="{7FA7762F-41EC-4D72-9F06-58964AB36FEA}" />
</Shortcut>
```

The CLSID is from `Package.appxmanifest:48` and `:53` — we already have it.

`ShortcutProperty` is supported in WiX 5 (which this repo uses — see `scripts/build-msi.ps1` finds `wix.exe` v5.x). If WiX rejects the syntax, the alternative is a deferred custom action that calls `propsys.dll`'s `IPropertyStore::SetValue` on the .lnk via PowerShell.

### Why both pieces are required

- Without the AUMID on the shortcut, `CreateToastNotifier(aumid)` will throw `Element not found` (`HRESULT 0x80070490`) because Windows doesn't recognize the AUMID as a registered notifiable app.
- Without switching the code path, even with the shortcut in place, we'd still be using WinAppSDK which Server 2025 doesn't enumerate.

### Verification plan

1. Build MSI 0.4.8 + Velopack release 0.4.8.
2. Keith signs both with SafeNet.
3. Deploy to `/opt/toast/api/wwwroot/downloads/ToastNotification.msi` AND `/opt/toast/releases/agent/win-x64/`. **DO NOT deploy to `/opt/toast/downloads/` — that's vestigial; nginx serves from the wwwroot path via Kestrel proxy. See "Deploy gotchas" below.**
4. Keith re-installs on Server 2025 box. Verify:
   - Device still registers in dashboard ✅ (regression test)
   - Open **Settings > System > Notifications** — confirm "Toast Notification" now appears in the per-app list with Banners + Sounds toggles
   - Send a tray test → banner appears on screen
   - Send a hub broadcast → banner appears on screen
   - Click an action button on a toast → activation round-trips back through `ActivationMode` (verify with --diag log)

---

## Deploy gotchas (learned the hard way this session)

**Two paths exist for the MSI; only one is served:**
- `/opt/toast/downloads/ToastNotification.msi` — **vestigial, ignored**
- `/opt/toast/api/wwwroot/downloads/ToastNotification.msi` — **actually served via UseStaticFiles() on Kestrel**, proxied by nginx `location /downloads/`

We deployed to the wrong path first and confused ourselves with a SHA mismatch. Always verify with:
```bash
curl -s -o /tmp/m.msi "https://toastnotification.com/downloads/ToastNotification.msi"
sha256sum /tmp/m.msi
# Compare against artifacts/installer/ToastNotification.Agent-0.4.X.msi
```

**Velopack feed lives at:** `/opt/toast/releases/agent/win-x64/` on TOASTWEB1, served at `https://releases.toastnotification.com/agent/win-x64/` (see `infrastructure/nginx/releases.conf`).

**SSH key:** `Docs/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem` — Windows ACL must be locked down before scp/ssh use it: `icacls "Docs\Assets\Toast_Web_LightsailDefaultKey-us-east-1.pem" /inheritance:r /grant:r "$env:USERNAME:F"`

**Server:** `ubuntu@54.82.103.160` (TOASTWEB1).

**Standing rule:** Every signed-MSI ship MUST also produce + sign + publish a Velopack release in the same session (build-release.ps1 + sign-msix.ps1 + scp). Otherwise the auto-update feed goes stale.

---

## Build & sign workflow (reference)

```powershell
# 1. Build MSI
.\scripts\build-msi.ps1 -Version 0.4.8

# 2. Build Velopack release
.\scripts\build-release.ps1 -Version 0.4.8

# 3. Keith runs (SafeNet token must be plugged in; PIN dialog will pop):
pwsh -ExecutionPolicy Bypass -File scripts\sign-msix.ps1 artifacts\installer\ToastNotification.Agent-0.4.8.msi
pwsh -ExecutionPolicy Bypass -File scripts\sign-msix.ps1 artifacts\releases\ToastNotification.Agent-win-Setup.exe

# 4. Deploy (us, not Keith):
scp -i "Docs/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem" \
    artifacts/installer/ToastNotification.Agent-0.4.8.msi \
    ubuntu@54.82.103.160:/tmp/

ssh -i "Docs/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem" ubuntu@54.82.103.160 \
    "sudo cp /tmp/ToastNotification.Agent-0.4.8.msi /opt/toast/api/wwwroot/downloads/ToastNotification.Agent-0.4.8.msi && \
     sudo cp /opt/toast/api/wwwroot/downloads/ToastNotification.msi /opt/toast/api/wwwroot/downloads/ToastNotification.msi.bak.0.4.7 && \
     sudo cp /tmp/ToastNotification.Agent-0.4.8.msi /opt/toast/api/wwwroot/downloads/ToastNotification.msi && \
     sudo chown toast:toast /opt/toast/api/wwwroot/downloads/ToastNotification.msi /opt/toast/api/wwwroot/downloads/ToastNotification.Agent-0.4.8.msi"

# 5. Same pattern for Velopack artifacts to /opt/toast/releases/agent/win-x64/
```

---

## Test environment (Keith's Server 2025 box)

- Hostname: `COL-BU-001`
- OS: Windows Server 2025 Standard, build 10.0.26100.32522
- Install type: Full Desktop Experience (NOT Server Core)
- Logged-in user: `COL-BU-001\Administrator` (built-in account)
- Access: ScreenConnect console session (not RDP)
- Agent install command (Keith's, prefilled from /devices/install):
  ```powershell
  $f="$env:TEMP\ToastNotification.msi"
  Invoke-WebRequest "https://toastnotification.com/downloads/ToastNotification.msi" -OutFile $f
  Start-Process msiexec -ArgumentList "/i `"$f`" /qn CLIENTID=995ea34c-120f-4743-b24e-dd910c0ae630 SERVERURL=https://toastnotification.com ENROLLMENTKEY=2nOWs1+4LEdAtnoyX82PzRpEGNlZ6za0" -Verb RunAs -Wait
  ```
- Tenant: Colo Solutions
- Device ID after 0.4.7 install: `530e0bc6-b57f-4567-9a77-b942183a74bc`

---

## --diag command (for support / next session)

```cmd
"C:\Program Files\Toast Notification\ToastNotification.Agent.exe" --diag
```

Now actually prints to console (fixed in 0.4.7) AND writes a copy to `%TEMP%\toastnotification-diag.txt`. Use this any time something doesn't behave; the agent.log content is what you need to debug the agent side.

---

## Open registry surface on Keith's box (post-manual-fixes from this session)

We had Keith manually write these as a diagnostic test. They're **still in place** on his box and don't need re-writing:

```
HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\PushNotifications
  ToastEnabled = 1 (DWORD)

HKCU\SOFTWARE\Classes\AppUserModelId\Toast2IT.ToastNotification
  DisplayName = "Colo Solutions"
  IconUri = "C:\Users\Administrator\AppData\Local\Toast2IT\Toast Notification\tenant-logo.png"
  CustomActivator = "{7FA7762F-41EC-4D72-9F06-58964AB36FEA}"

HKCU\SOFTWARE\Classes\CLSID\{7FA7762F-41EC-4D72-9F06-58964AB36FEA}
  (Default) = "Toast Notification Activator"
HKCU\SOFTWARE\Classes\CLSID\{7FA7762F-41EC-4D72-9F06-58964AB36FEA}\LocalServer32
  (Default) = "C:\Program Files\Toast Notification\ToastNotification.Agent.exe"
```

The 0.4.8 fix should make all of this redundant (the legacy API doesn't need CustomActivator on the AUMID; the Start Menu shortcut's AppUserModelID property is the canonical registration).

---

## Working tree state at handoff (commit `cd27ef1`)

Clean. Both this session's commits are pushed:
- `cd27ef1 fix: agent registers on Windows Server 2025 (0.4.7)`
- `eb0804f feat: Device Groups CRUD + membership management (Codex WIP)` — Codex's parallel-track WIP that we attribution-committed because it built clean and Keith asked us to clean up the working tree.

No uncommitted changes.

---

## What I would NOT do in 0.4.8

- **Don't write `ToastEnabled` from the agent.** It's a per-user Windows setting — overriding it is intrusive and may surprise users who deliberately turned it off.
- **Don't write `NoToastApplicationNotification` policy keys** to override Group Policy. Same reason.
- **Don't add a Server SKU detection branch with two render paths.** The legacy `ToastNotificationManager` works on Win10/11 client SKUs too — just use it everywhere. Less code, fewer code paths, fewer surprises.
- **Don't claim "0.4.8 is verified to work on Server 2025" until Keith confirms a banner actually rendered on his box.** Same mistake we made with 0.4.7 — registration ≠ visible delivery.

---

## Honest acknowledgment to Keith at session start

Keith is angry, and right to be. Lead with that. Don't bury it in optimism. Something like:

> "Picking up from yesterday's session — the 0.4.7 ship fixed device registration on Server 2025 but you correctly called out that banners still don't render. I read the handoff doc; we identified the root cause with evidence (our app isn't in Settings → Notifications → per-app list because we use WinAppSDK and Server 2025's notification UI only enumerates apps registered through the legacy WinRT API path). 0.4.8 ships the legacy API switch. I'll have the unsigned MSI + Velopack release in roughly 20 minutes; ready for your SafeNet when it lands. No promises this works until you see a banner — we made that mistake last session and I'm not making it twice."

---

## Files referenced

- Agent code: `src/ToastRevival.Agent/Program.cs`, `src/ToastRevival.Agent/AgentClient.cs`, `src/ToastRevival.Agent/ToastTemplates.cs`, `src/ToastRevival.Agent/NotificationDisplayName.cs`, `src/ToastRevival.Agent/Package.appxmanifest`
- Installer: `installer/ToastRevival.Agent.Setup.wxs`, `installer/ToastNotificationLogon.xml`
- Build scripts: `scripts/build-msi.ps1`, `scripts/build-release.ps1`, `scripts/sign-msix.ps1`
- Nginx config: `infrastructure/nginx/releases.conf`
- This handoff: `Docs/ToastRevival/HANDOFF-WS2025-BANNERS.md`
