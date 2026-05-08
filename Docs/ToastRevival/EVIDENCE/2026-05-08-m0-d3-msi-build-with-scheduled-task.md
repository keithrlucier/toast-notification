# M0 D3 — MSI build with Scheduled Task in user context (2026-05-08)

## Summary

Built `ToastNotification.Agent-0.3.0.0.msi` locally with a per-machine MSI that registers a logon-triggered Scheduled Task in the interactive user's context, replacing the M0A all-users Startup-folder shortcut. Better GPO and Intune compatibility for the MSI/RMM deployment channel. Toast notifications can only render in the unelevated interactive user's context (Program.cs:17-22 hard-exits on elevated processes), so the task uses the BUILTIN\Users group principal (`S-1-5-32-545`) with `RunLevel=LeastPrivilege` — ensuring whoever logs in fires the toast under their own session token, not SYSTEM.

Pre-install validation only on this machine (XML structural parse, MSI Custom Action table inspection, admin-install payload extract). Lab install + signed MSI verification is Keith's hand-off step.

## Files Changed

- **NEW** `installer/ToastNotificationLogon.xml` — Task Scheduler v1.4 XML, UTF-16 LE with BOM (matches the encoding `schtasks /Query /XML` exports). Group principal Users, AtLogon trigger, `LeastPrivilege` run level, Action runs `%ProgramFiles%\Toast Notification\ToastNotification.Agent.exe --template alert --no-wait`.
- **MODIFIED** `installer/ToastRevival.Agent.Setup.wxs` — Removed `StartupShortcut` component and `<StandardDirectory Id="StartupFolder">`. Added `LogonTaskAssets` component group shipping the XML to INSTALLFOLDER. Added two deferred custom actions (`InstallScheduledTask`, `UninstallScheduledTask`) calling `[System64Folder]schtasks.exe` with `/Create /XML /F` (install) and `/Delete /F` (uninstall) against task path `\Toast2IT\ToastNotificationAgentLogon`. Sequenced after `InstallFiles` and before `RemoveFiles` respectively, gated on `NOT REMOVE` and `REMOVE="ALL"`.
- **MODIFIED** `scripts/build-msi.ps1` — Bumped default `$Version` to `0.3.0.0`. Added `$logonTaskXml` resolution and `LogonTaskXmlPath` variable passthrough to the `wix build` invocation. Added a fail-fast existence check on the XML before invoking wix.

## Build verification (local, no install)

### MSI build

```
==> Publishing self-contained agent (win-x64)...
  ToastRevival.Agent -> C:\SOURCE\toast\artifacts\ToastRevival.Agent\win-x64-self-contained\
==> Building MSI (0.3.0.0) -> C:\SOURCE\toast\artifacts\installer\ToastNotification.Agent-0.3.0.0.msi

MSI ready:
  Path : C:\SOURCE\toast\artifacts\installer\ToastNotification.Agent-0.3.0.0.msi
  Size : 50.61 MB
```

Build clean — no schema warnings beyond the pre-existing FIX-MSIX-003 mspdbcmf.exe symbols-package warning (unrelated to D3, tracked separately).

### XML structural parse

```
[OK] XML parses cleanly. Root: Task version=1.4
  URI:        \Toast2IT\ToastNotificationAgentLogon
  Trigger:    LogonTrigger Enabled=true
  Principal:  GroupId=S-1-5-32-545  RunLevel=LeastPrivilege
  Action:     Command=%ProgramFiles%\Toast Notification\ToastNotification.Agent.exe
  Args:       --template alert --no-wait
```

### MSI Custom Action table

Deferred + non-impersonate + ExeCommand + Directory source — Type 3106 for install (return=check), Type 3170 for uninstall (return=ignore so a missing-task on uninstall doesn't break removal):

```
Action=InstallScheduledTask    Type=3106  Source=INSTALLFOLDER
  Target="[System64Folder]schtasks.exe" /Create /TN "\Toast2IT\ToastNotificationAgentLogon" /XML "[INSTALLFOLDER]ToastNotificationLogon.xml" /F

Action=UninstallScheduledTask  Type=3170  Source=INSTALLFOLDER
  Target="[System64Folder]schtasks.exe" /Delete /TN "\Toast2IT\ToastNotificationAgentLogon" /F
```

### MSI InstallExecuteSequence

```
Seq=3499  UninstallScheduledTask  Condition=REMOVE="ALL"
Seq=3500  RemoveFiles
Seq=4000  InstallFiles
Seq=4001  InstallScheduledTask    Condition=NOT REMOVE
```

Uninstall fires before file removal so schtasks.exe and the XML are still on disk when the deferred action runs. Install fires after file copy so the XML is in INSTALLFOLDER when `schtasks /Create /XML` reads it.

### MSI Shortcut table — StartupShortcut removed

```
Shortcut=StartMenuAgentShortcut  Dir=AppStartMenuFolder  Name=Toast Notification  Target=[INSTALLFOLDER]ToastNotification.Agent.exe
```

Only the Start Menu shortcut remains; the M0A Startup-folder shortcut is gone from the table.

### MSI File payload (admin-install extract)

```
ToastNotification.Agent.exe       276.0 KB
ToastNotificationLogon.xml          3.7 KB
RestartAgent.exe                   76.6 KB   (WindowsAppSDK self-contained dep)
createdump.exe                     70.3 KB   (WindowsAppSDK self-contained dep)
```

XML is byte-identical from repo source through cab compression and admin-install extract — UTF-16 LE BOM preserved (`FF FE` first two bytes).

## Pre-flight: schtasks /Create /XML expected-failure smoke test

Ran `schtasks.exe /Create /TN \Toast2IT\_PreflightTest_M0D3 /XML installer\ToastNotificationLogon.xml /F` from the unprivileged dev shell:

```
ERROR: Access is denied.
exit=1
```

This is the **expected** outcome and validates the architecture, not a bug:

- `schtasks /Create` with a group principal (`S-1-5-32-545` BUILTIN\Users) requires admin elevation.
- The error reached "Access is denied" instead of an XML schema rejection ("ERROR: The task XML is not valid"), confirming Task Scheduler accepted the XML schema and principal and only refused at the authorization step.
- The MSI custom action runs deferred + `Impersonate="no"` — so during install it executes as SYSTEM and has the privilege to create the per-group task.
- A user-shell failure here is precisely what we want; if a non-admin shell could create this task, the privilege model would be broken.

If Keith wants belt-and-suspenders pre-flight, an elevated PowerShell session can run `schtasks /Create /TN \Toast2IT\_PreflightTest /XML installer\ToastNotificationLogon.xml /F`, then `Get-ScheduledTask -TaskPath '\Toast2IT\' -TaskName '_PreflightTest'` to inspect, then `schtasks /Delete /TN \Toast2IT\_PreflightTest /F` to clean up. Not required — the MSI install on the lab is the canonical integration test.

## Hand-off (Keith, lab machine)

1. **Sign the MSI**: existing flow — Thales token unlocked via SafeNet tray, then `signtool sign /a /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 "artifacts\installer\ToastNotification.Agent-0.3.0.0.msi"`. (No MSIX-specific quirks here — this is a classic Authenticode MSI sign; DigiCert Cert Utility will work too.)
2. **Uninstall any prior version on lab**: `msiexec /x {GUID-of-prior} /qn` or via Settings -> Apps. M0A's `0.1.0.0` and the rebranded `0.2.0.0` share the same UpgradeCode (`A6F3D8F1-7B22-4E5A-9E3C-2A4F8B1C9D70`), so a clean upgrade should also work — `MajorUpgrade` element handles it. Either path is fine.
3. **Install the signed MSI**: `msiexec /i ToastNotification.Agent-0.3.0.0.msi /qn` (or double-click for UAC + interactive).
4. **Verify task created**: as the same logged-on user (or any user), open an elevated PowerShell and run:
   ```powershell
   Get-ScheduledTask -TaskPath '\Toast2IT\' -TaskName 'ToastNotificationAgentLogon' | Format-List TaskPath,TaskName,State,Principal,Triggers,Actions
   ```
   Expect: `State=Ready`, `Principal.GroupId=S-1-5-32-545`, `Principal.RunLevel=Limited` (Task Scheduler renders `LeastPrivilege` as `Limited`), `Triggers[0]` is `MSFT_TaskLogonTrigger`, `Actions[0].Execute` is `%ProgramFiles%\Toast Notification\ToastNotification.Agent.exe`.
5. **Verify task fires at logon**: log out, log back in as a non-admin user. Look for the Critical-scenario alert toast banner (bottom-right). Action Center entry (Win+N) confirms the toast registered with the system. The toast title and body come from the `alert` template in `ToastTemplates.cs`.
6. **Verify uninstall removes the task**: `msiexec /x ToastNotification.Agent-0.3.0.0.msi /qn` — then re-run the `Get-ScheduledTask` command. Expect the task to be gone.
7. **Verify uninstall is idempotent**: if the task was manually removed before uninstall, the MSI should still complete without error. The `Return="ignore"` on `UninstallScheduledTask` makes that safe.
8. **Capture follow-up evidence**: write `EVIDENCE/2026-05-08-m0-d3-task-fires-at-logon.md` (or whatever date the lab install happens) with screenshots of the task in Task Scheduler MMC and the toast banner. Mark M0 D3 COMPLETE in MILESTONES.md when verified.

## Open Items / Known Limitations (defer to M0 D4 or M2)

- **One-shot demo behavior remains**: the agent fires a single `alert` toast at logon and exits. This is the same demonstration behavior as M0A — the deployment plumbing is what changed, not the product behavior. M2 evolves the agent into a long-running SignalR-connected process; the same scheduled task primitive remains the launcher, but the action arguments and lifecycle change.
- **Per-machine vs per-user**: this MSI installs per-machine (Scope="perMachine") and the task is registered for the BUILTIN\Users group. Every interactive logon by any user gets the toast. M0 D4 GPO matrix should validate this against domain-joined and Intune-managed scenarios.
- **No Win10 1809 verification**: same blocker as M0 D2 — no Win10 1809 lab on hand. M0 D4 GPO matrix is the canonical check.
- **MSIX channel parity**: M0 D2's MSIX uses Microsoft's COM activator path for toast registration but does NOT yet declare a Startup task (`<uap5:Extension Category="windows.startupTask">`). M0 D5 (Store flight) will need that for parity with the MSI's logon-trigger behavior. Tracked in TODO.md, not blocking D3.
