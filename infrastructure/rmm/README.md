# RMM Deployment

Silent install / uninstall scripts for the Toast Notification Windows agent. Designed to be imported into MSP RMM platforms (NinjaOne, Datto RMM, ConnectWise Automate, Kaseya VSA, Atera, etc.) and pushed to managed endpoints.

## What's here

```
infrastructure/rmm/
├── install-toast-agent.ps1     ← canonical install script (PowerShell 5.1+)
├── uninstall-toast-agent.ps1   ← canonical uninstall script (PowerShell 5.1+)
├── ninjaone/                   ← NinjaOne-specific import notes
├── datto-rmm/                  ← Datto RMM component notes
└── connectwise-automate/       ← ConnectWise Automate script notes
```

The two PowerShell scripts are the source of truth. Per-RMM directories carry import instructions and any platform-specific wrappers.

## What the install script does

1. Validates `TenantId` is a GUID and `ServerUrl` is an absolute URL.
2. Skips if the agent at the same-or-higher version is already installed (idempotent).
3. Downloads the signed MSI to `%ProgramData%\Toast2IT\Install\` (TLS 1.2/1.3, no-progress fast path).
4. **Verifies the MSI Authenticode signature is issued to "Toast2IT, LLC" AND chains to a trusted root** before any execution. Refuses to install otherwise — protects against a malicious MSI substitute on the wire even if the hosting domain is compromised.
5. Runs `msiexec /i ... CLIENTID=... SERVERURL=... ENROLLMENTKEY=... /qn /norestart` with verbose logging.
6. Returns msiexec's exit code so the RMM can detect failures.

Exit code map:

| Code | Meaning |
|---|---|
| `0` | Install succeeded (or agent was already at-or-above target version) |
| `1` | Parameter validation failed |
| `2` | MSI download failed |
| `3` | Authenticode verification failed (refused to execute) |
| `124` | msiexec hung past timeout, killed |
| `1602` | msiexec canceled |
| `1603` | msiexec fatal — see `%ProgramData%\Toast2IT\Install\msiexec.log` |
| `1618` | Another install in progress — retry later |
| `3010` | Success, reboot pending (treated as success) |

## Parameters MSPs need

| Parameter | Source | Required |
|---|---|---|
| `TenantId` | Dashboard → Settings → Tenant → Tenant ID (GUID) | yes |
| `ServerUrl` | `https://toastnotification.com` (or your private deploy) | yes |
| `EnrollmentKey` | Dashboard → Settings → Tenant → "Require enrollment key" | only if gating is enabled |

The MSI URL defaults to the production hosted location; override via `-MsiUrl` only when running an internal mirror.

## PowerShell version compatibility

- **Floor: PowerShell 5.1** (ships with Windows 10 1607+ / Windows Server 2016+). RMM agents almost always have PS 5.1 available.
- **Tested on: 5.1 and 7.4.** Avoids ternary `?:`, null-coalescing `??`, null-conditional `?.`, and other PS 7-only operators. Uses explicit `if/else` and `try/catch` patterns.
- Tls13 is layered in defensively if the running runtime supports it; falls back to Tls12-only otherwise.

## Run-as context

- **SYSTEM** (RMM agent context): preferred. The MSI installs to `%ProgramFiles%` and registers a per-user Scheduled Task for the agent. SYSTEM has the privileges needed for both.
- **Any user with local admin**: also works.
- **Standard user**: msiexec will prompt for elevation; install will fail under `/qn`. Do not run the script as a standard user.

## Authenticode rejection — what it means in practice

If the script exits 3 with "Authenticode verification failed":

1. The MSI URL was wrong (typo, internal mirror serving a different file).
2. The hosting domain was compromised and is serving an unsigned or differently-signed MSI.
3. The signing certificate expired and a fresh build hasn't been re-signed yet (operations error on our side — file an issue at https://toastnotification.com/support).

Do not work around this by skipping the check. The check is the last defense against a malicious push.

## Per-RMM import

| RMM | Import notes |
|---|---|
| **NinjaOne** | [ninjaone/README.md](ninjaone/README.md) |
| **Datto RMM** | [datto-rmm/README.md](datto-rmm/README.md) |
| **ConnectWise Automate** | [connectwise-automate/README.md](connectwise-automate/README.md) |
| **Kaseya VSA / Atera / N-able / others** | Import `install-toast-agent.ps1` as a custom PowerShell script. Map the platform's variable substitution to the three named parameters. The script is RMM-agnostic. |

## Uninstall

`uninstall-toast-agent.ps1` reads the Windows Installer registry to find the product code, then runs `msiexec /x {ProductCode} /qn /norestart`. Idempotent — exits 0 if the agent isn't installed.

Per-user config at `%LocalAppData%\Toast2IT\Toast Notification\` is intentionally not removed. It's DPAPI-encrypted (CurrentUser scope) so it's only readable by the user who created it; without the agent running, it's inert. RMMs that want a full purge should add a separate user-profile cleanup task that deletes `%LocalAppData%\Toast2IT\` from each profile.

## Testing the install

The cleanest pre-flight is a single endpoint:

1. Spin up a fresh Windows 11 lab VM.
2. Open PowerShell as Admin.
3. Run `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`.
4. Invoke `.\install-toast-agent.ps1 -TenantId <yours> -ServerUrl https://toastnotification.com [-EnrollmentKey <yours>]`.
5. Verify exit code 0.
6. Check `Get-ScheduledTask -TaskName 'ToastNotificationAgentLogon' -TaskPath '\Toast2IT\'` shows `Ready`.
7. Sign out and sign back in. A toast notification should fire on first logon (the sample notification the agent sends to confirm registration).

If step 7 fails: `%ProgramData%\Toast2IT\Install\install-toast-agent.log` and `%ProgramData%\Toast2IT\Install\msiexec.log` are the first stops. The agent's own `%LocalAppData%\Toast2IT\Toast Notification\diag.log` carries runtime diagnostics.
