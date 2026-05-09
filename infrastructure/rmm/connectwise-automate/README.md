# ConnectWise Automate — Toast Notification install

## Script creation

1. Automate Control Center → **Tools** → **Script Manager** → **New Script**.
2. **Name**: `Install Toast Notification Agent`.
3. **Category**: `Applications` → `Maintenance` (or your preferred category).
4. **Script Notes**: `Silent install of the Toast Notification agent. Required: TenantId, ServerUrl. Optional: EnrollmentKey.`

## Script logic

ConnectWise Automate scripts mix native script-engine steps with embedded PowerShell. The cleanest pattern: a single PowerShell step that pulls the canonical script from your Automate solution server and runs it with the customer-specific parameters.

### Option A — embed inline (recommended for tight version control)

1. **Step 1**: `Function: Shell` (PowerShell shell) with payload:
   ```powershell
   $ScriptPath = Join-Path $env:TEMP 'install-toast-agent.ps1'
   @'
   <paste contents of install-toast-agent.ps1 here>
   '@ | Set-Content -Path $ScriptPath -Encoding utf8

   & $ScriptPath -TenantId '@TenantId@' `
                 -ServerUrl '@ServerUrl@' `
                 -EnrollmentKey '@EnrollmentKey@'
   exit $LASTEXITCODE
   ```
2. Replace `<paste contents of install-toast-agent.ps1 here>` with the file's contents.
3. **Run As**: `LTService` (the agent service runs as SYSTEM).
4. **Save**.

`@TenantId@`, `@ServerUrl@`, and `@EnrollmentKey@` are Automate's variable substitution syntax. Define them as **Script Variables** on the script's **Variables** tab so operators are prompted when the script is queued.

### Option B — host externally (recommended at scale)

If you've staged the canonical script on your Automate solution server's hosted-files share, replace the inline embed with a `Remote Get File` step:

1. **Step 1**: `Function: Remote Get File`
   - URL: `https://files.<your-automate>.example/scripts/install-toast-agent.ps1`
   - Save As: `%TEMP%\install-toast-agent.ps1`
2. **Step 2**: `Function: Shell` (PowerShell)
   ```powershell
   & "$env:TEMP\install-toast-agent.ps1" -TenantId '@TenantId@' `
                                          -ServerUrl '@ServerUrl@' `
                                          -EnrollmentKey '@EnrollmentKey@'
   exit $LASTEXITCODE
   ```

Option B keeps the script under one-source-of-truth on your hosted-files server and lets you push updates without re-saving the Automate script.

## Script variables

Add to the script's **Variables** tab:

| Name | Type | Required | Default |
|---|---|---|---|
| `TenantId` | Text | Yes | — |
| `ServerUrl` | Text | Yes | `https://toastnotification.com` |
| `EnrollmentKey` | Text | No | (blank) |

## Schedule

1. **Automation** → **Schedule Script**.
2. Pick `Install Toast Notification Agent`.
3. Targets: client / location / group filter.
4. Fill the three variables for that client's tenant.
5. Schedule: **Run Once** or **Maintenance Window**.

## Detection (skip-if-installed)

Automate's **EDF** (Extra Data Field) on a computer can carry an `Agent Installed` boolean. Either:

- Apply an **Auto-Join Search** that excludes computers where `Programs` contains `Toast Notification`, OR
- Trust the install script's built-in same-or-newer skip — exits 0 with no msiexec invocation when the agent is already current. Recommended.

## Uninstall script

Repeat the same procedure with `uninstall-toast-agent.ps1`. No script variables required.

## Reading exit codes in Automate

Automate marks script step success by exit code. The install script's exit code map (also see [`../README.md`](../README.md)):

| Code | Meaning | Automate behavior |
|---|---|---|
| `0` | Success or already-installed | Step succeeds, ticket auto-closes if configured |
| `1`, `2`, `3` | Validation / download / Authenticode failure | Step fails, ticket flags for technician |
| `1602`, `1603`, `1618` | msiexec error | Step fails |
| `3010` | Success, reboot pending | Step succeeds (script maps to 0) |
| `124` | msiexec hung | Step fails |

For failed runs, the install log is at `%ProgramData%\Toast2IT\Install\install-toast-agent.log` on the endpoint. Pull via **Computer → Remote → File Browser** or by adding a follow-up `Get File from Remote` step that uploads the log to the ticket.

## Solution Center / partner-share

If you maintain a community Solution Center entry, the script + uninstall pair are hostable as a `.zip` containing:

- `install-toast-agent.ps1`
- `uninstall-toast-agent.ps1`
- `README.md` (this file)

Re-export from the canonical paths in this repo on every release; do not maintain a fork.
