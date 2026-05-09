# Datto RMM — Toast Notification install

## Component creation

1. Datto RMM portal → **ComStore** → **Components** → **New Component**.
2. **Category**: `Applications`.
3. **Component Type**: `Script`.
4. **Name**: `Install Toast Notification Agent`.
5. **Description**: `Silent install of the Toast Notification agent. Required: TenantId, ServerUrl. Optional: EnrollmentKey.`
6. **Script Type**: `PowerShell`.
7. **Script Body**: paste the contents of [`../install-toast-agent.ps1`](../install-toast-agent.ps1).

## Component variables (Datto's `usrTENANT_ID`-style)

Datto exposes user variables via `$env:` in the script execution context. The bundled script reads named PowerShell parameters, so add a small wrapper at the top of the script body that maps Datto variables to the parameter set:

```powershell
# Datto RMM passes user variables via env. Translate to the named
# parameters install-toast-agent.ps1 expects.
$TenantId      = $env:usrTenantId
$ServerUrl     = $env:usrServerUrl
$EnrollmentKey = $env:usrEnrollmentKey

# (followed by the rest of install-toast-agent.ps1, with the param() block
#  removed since we just set $TenantId / $ServerUrl / $EnrollmentKey above)
```

OR simpler: keep the script intact and invoke it via dot-source, passing the parameters explicitly:

```powershell
$ScriptPath = Join-Path $env:TEMP 'install-toast-agent.ps1'
@'
<paste install-toast-agent.ps1 here>
'@ | Set-Content -Path $ScriptPath -Encoding utf8

& $ScriptPath -TenantId $env:usrTenantId `
              -ServerUrl $env:usrServerUrl `
              -EnrollmentKey $env:usrEnrollmentKey
exit $LASTEXITCODE
```

8. Add the user variables in the component's **Input Variables** tab:
   - `usrTenantId` — Variable Type: **String**, Required.
   - `usrServerUrl` — Variable Type: **String**, Required, Default `https://toastnotification.com`.
   - `usrEnrollmentKey` — Variable Type: **String**, Required: No.

9. **Run As**: `Local System`.
10. **Save**.

## Deploy

1. **Sites** → pick the customer site.
2. **Jobs** → **New Job** → select the `Install Toast Notification Agent` component.
3. Fill the input variables for that customer's tenant config.
4. Targets: select **All Devices** for a fleet rollout, or filter by device group.
5. Schedule: **Run Now** or queue for next maintenance window.
6. **Save and Run**.

## Filter to skip already-installed

Datto RMM can apply a Filter Component before the install. Either:

- **Filter Component** that runs `Get-ItemProperty HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\* | Where-Object DisplayName -eq 'Toast Notification'` and returns 0 if absent (proceed) or 1 if present (skip), OR
- Trust the install script's built-in same-or-newer skip — exits 0 with no msiexec invocation when the agent is already current. Recommended.

## Uninstall component

Same procedure with `uninstall-toast-agent.ps1`. No input variables. Run as Local System.

## Reading exit codes in Datto

Datto marks job success by exit code. The install script's exit code map is:

| Code | Meaning | Datto status |
|---|---|---|
| `0` | Success or already-installed | green `Success` |
| `1`, `2`, `3` | Validation / download / Authenticode failure | red `Failed` |
| `1602`, `1603`, `1618` | msiexec error | red `Failed` |
| `3010` | Success, reboot pending | green `Success` (script maps to 0) |
| `124` | msiexec hung | red `Failed` |

For failed jobs, pull the install log via Datto's **Live Connect** → **File Browser** at `%ProgramData%\Toast2IT\Install\`.
