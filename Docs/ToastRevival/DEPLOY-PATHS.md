# Toast Notification — Canonical Deploy / Serving Paths

**Authoritative as of 2026-05-29 (verified against live nginx + curl). This supersedes
the `wwwroot/downloads` path claimed in `HANDOFF-WS2025-BANNERS.md` — that path is WRONG
and empty on the current server.**

Server: **TOASTWEB1** — `ubuntu@54.82.103.160` (service user `toast`).
SSH key: `Docs/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem`
(lock ACL before use: `icacls <key> /inheritance:r /grant:r "$env:USERNAME:F"`).
Cloudflare sits in front of `toastnotification.com`; the download + feed responses come back
`Cf-Cache-Status: DYNAMIC` (origin pass-through, NOT edge-cached) — a fresh deploy is visible
immediately, no cache purge required.

---

## 1. MSI installer (NEW installs + RMM fleet upgrades)

| | |
|---|---|
| **Disk path (origin)** | `/opt/toast/downloads/` |
| **Canonical file** | `/opt/toast/downloads/ToastNotification.msi` (always the current release) |
| **Versioned file** | `/opt/toast/downloads/ToastNotification.Agent-<version>.msi` |
| **nginx** | `sites-enabled/toast` → `location /downloads/ { alias /opt/toast/downloads/; }` (static; NOT proxied to the API) |
| **Public URL** | `https://toastnotification.com/downloads/ToastNotification.msi` |
| **Versioned URL** | `https://toastnotification.com/downloads/ToastNotification.Agent-<version>.msi` |

**WRONG / vestigial (do NOT deploy here):** `/opt/toast/api/wwwroot/downloads/` (empty),
`/opt/toast/downloads/` was *also* historically confused with `api/wwwroot`. The live nginx
`alias` is `/opt/toast/downloads/` — that is the only served path.

Deploy = `scp` to `/tmp`, then `sudo cp` to both the canonical and versioned names,
`sudo chown toast:toast`. Always back up the current `ToastNotification.msi` first.

---

## 2. Velopack auto-update feed (Setup.exe-channel installs ONLY)

| | |
|---|---|
| **Disk path (origin)** | `/opt/toast/releases/agent/win-x64/` |
| **Index** | `releases.win.json` (+ legacy `RELEASES`, `assets.win.json`) |
| **Packages** | `ToastNotification.Agent-<version>-full.nupkg`, `-delta.nupkg` |
| **Public URL** | `https://releases.toastnotification.com/agent/win-x64/` |
| **Default feed in agent** | `UpdateService.DefaultFeedUrl` = same URL |

Keep the previous version's `-full.nupkg` so Velopack can build a delta. Update the three
index files + the new nupkgs, `sudo chown toast:toast *`.

---

## 3. How the fleet actually updates (read this before promising anything)

### M15+ — MSI self-update (0.4.28+)

MSI-installed agents now poll `/api/agent/version` every 24h (`SelfUpdateService.RunMsiUpdateLoopAsync`).
When the server reports a newer version:
1. Agent downloads the signed MSI from the `msiDownloadUrl` in the response.
2. Authenticode-verifies it (Toast2IT, LLC cert required).
3. Writes a trigger file to `%ProgramData%\Toast2IT\Toast Notification\pending-action.txt`.
4. Fires the `\Toast2IT\ToastNotificationUpdater` SYSTEM scheduled task (installed by the MSI).
5. That task runs `msiexec /i /qn` as SYSTEM — silent over-the-top upgrade.
6. Agent restarts via the existing logon task at next user session.

**After every signed-MSI ship:** update `Agent__LatestVersion` in `/opt/toast/.env` on TOASTWEB1
to the new version string, then `sudo systemctl restart toast-api`. This is what gates the rollout.
If this env var isn't updated, agents see "up to date" and never pull the new MSI.

`DisableAutoUpdate=1` in `HKLM\SOFTWARE\Toast2IT\Toast Notification` suppresses the poll
(for MSPs that want RMM to own updates). Remote uninstall from the admin panel is NOT affected
by DisableAutoUpdate — it's a separate hub command path.

### RMM push (always available, bypasses the self-update mechanism entirely)

Push the new signed MSI through the RMM as: `msiexec /i ToastNotification.msi /qn`.
WiX `MajorUpgrade AllowSameVersionUpgrades="yes"` + stable `UpgradeCode=A6F3D8F1-7B22-4E5A-9E3C-2A4F8B1C9D70`
makes this a clean in-place upgrade. CLIENTID/SERVERURL/ENROLLMENTKEY are preserved by
existing `bootstrap.json` / `config.json` — only needed on first install.

### Velopack Setup.exe channel

Self-updates from the feed in §2 within 24h (delta download). Only for Setup.exe-installed agents,
not MSI/RMM fleet. `UpdateFeedUrl` in HKLM overrides the feed URL for internal mirrors.

---

## 3b. Clean removal / appearance reversal (0.4.32+)

The lock screen branding has two footprints with two owners, and removal must reverse both
("do no harm"):

- **Per-user lock screen image** — set by the agent via WinRT (`LockScreen.SetImageFileAsync`,
  user context). Reverted by the agent's `--revert-appearance` mode, which restores the
  `lockscreen_original.jpg` snapshot or, if that snapshot was lost, the Windows default image.
  This mode is invoked from: the remote-uninstall hub path (`RequestUninstallAsync`), the MSI
  uninstall (`RevertAppearance` impersonated CA, BEFORE `DeleteUserConfig` wipes the snapshot),
  and the uninstall script's transient user-context scheduled task.
- **Machine lock screen policy** — `HideSpotlightWindowsSpotlight`, `NoLockScreenCamera`,
  `LockScreenOverlaysDisabled` under `HKLM\SOFTWARE\Policies\...`. Set by `install-toast-agent.ps1
  -PinLockScreen` (admin context); reverted by `uninstall-toast-agent.ps1` AND by the MSI's
  `RevertLockScreenPolicy` SYSTEM CA on Control-Panel uninstall. The agent (LeastPrivilege) cannot
  touch these. `NoCloudApplicationNotification` is intentionally NOT pinned by the canonical
  installer (it suppresses toast delivery) but the reversal strips it if a prior script set it.

`uninstall-toast-agent.ps1` is the canonical **one-shot remove** — the exact command surfaced in
the dashboard's "Remove agent" modal: stop agent → restore lock screen (user-context task) →
revert policy → clear per-user Spotlight toggles across all hives → `msiexec /x` → purge per-user
config. The admin-panel "Uninstall" button no longer claims to remotely remove software; it opens
the modal (best-effort remote attempt remains for online 0.4.32+ agents).

**M12.B live appearance push:** saving the overlay/lock screen config (or disabling it) pushes
`AppearanceUpdated` to the `tenant-{id}` SignalR group; connected agents re-fetch
`/api/devices/appearance-config` and re-apply (including revert-on-disable) immediately instead of
at next restart. Pre-0.4.32 agents ignore the message (forward-compatible).

---

## 4. Standing rule

Any signed-MSI ship MUST also produce + sign + publish a Velopack release in the same session
(otherwise the auto-update feed for Setup.exe-channel installs goes stale). Verify both public
URLs with `curl -sI` after every deploy. Re-confirm the nginx `alias` path against the live
`sites-enabled/toast` before trusting any older doc.
