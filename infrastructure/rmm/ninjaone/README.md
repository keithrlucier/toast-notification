# NinjaOne — Toast Notification install

## Import

1. NinjaOne console → **Administration** → **Library** → **Automation**.
2. **Add** → **PowerShell Script**.
3. Name: `Install Toast Notification Agent`.
4. Description: `Silent install of the Toast Notification agent. Required parameters: TenantId, ServerUrl. Optional: EnrollmentKey.`
5. Operating System: **Windows**.
6. Architecture: **All**.
7. Run As: **System**.
8. Paste the contents of [`../install-toast-agent.ps1`](../install-toast-agent.ps1) into the script body.
9. **Add Variable** for each of:
   - `TenantId` — String, required, no default.
   - `ServerUrl` — String, required, default `https://toastnotification.com`.
   - `EnrollmentKey` — String, optional, no default.
10. **Save**.

## Deploy to a policy

1. **Administration** → **Policies** → pick the customer/site policy.
2. **Scheduled Automation** → **Add** → select `Install Toast Notification Agent`.
3. Set the three custom variables to that customer's tenant config (TenantId from their dashboard, ServerUrl, EnrollmentKey if their tenant requires gating).
4. Schedule: `Run Once Now` for an immediate fleet rollout, or `On Patch Day` to roll into the next maintenance window.
5. Targets: filter by site / device group as appropriate.
6. **Save**.

## Detection (skip-if-installed)

NinjaOne can apply a custom condition before running. Recommended:

- **Condition**: `Application Inventory` → `Toast Notification` → `Not Installed`
- This makes the script idempotent at the policy level too — devices that already have the agent skip the run entirely. The script itself is also idempotent (skips if same-or-newer is installed) so this is belt-and-suspenders.

## Uninstall

Repeat steps 2–10 with `uninstall-toast-agent.ps1`. No parameters required. Schedule against a "decommissioning" device group or run ad-hoc against individual machines.

## Custom MSI mirror

If your customer is running an internal NinjaOne mirror with a private MSI hosting URL, add a fourth variable:

- `MsiUrl` — String, optional, default `https://toastnotification.com/downloads/agent/ToastNotification.Agent-latest.msi`.

Override per-policy. The Authenticode check still runs against whatever binary is downloaded — if your mirror serves a re-signed MSI signed by anyone other than `Toast2IT, LLC`, the install will refuse to execute. Mirrors must serve the unmodified original MSI.

## Reading exit codes in NinjaOne

NinjaOne marks the script result by exit code. The install script returns:

| Code | Meaning | NinjaOne shows |
|---|---|---|
| `0` | Success or already-installed | green `Success` |
| `1`, `2`, `3` | Validation / download / Authenticode failure | red `Failure` |
| `1602`, `1603`, `1618` | msiexec error | red `Failure` |
| `3010` | Success, reboot pending | green `Success` (script maps to 0) |
| `124` | msiexec hung | red `Failure` |

For failed runs, pull `%ProgramData%\Toast2IT\Install\install-toast-agent.log` and `msiexec.log` via NinjaOne's File Manager remote tool.
