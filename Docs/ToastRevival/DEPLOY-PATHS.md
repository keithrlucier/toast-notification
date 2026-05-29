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

`UpdateService.cs` is explicit: **MSI-installed agents at `%ProgramFiles%` are NOT
Velopack-managed and do NOT self-update.** `UpdateManager.IsInstalled` is false for them, so
the 24h update loop no-ops with `"not a Velopack-managed install — update check skipped"`.
This is intentional — MSP/RMM tools own the update lifecycle for MSI deployments.

Therefore the two channels are:

- **MSI / RMM fleet (the hundreds of devices):** push the new signed MSI through the same RMM
  (Ninja / ConnectWise / Datto / Intune) as a silent upgrade: `msiexec /i ToastNotification.msi /qn`.
  The WiX is authored as a `MajorUpgrade AllowSameVersionUpgrades="yes"` with a stable
  `UpgradeCode=A6F3D8F1-7B22-4E5A-9E3C-2A4F8B1C9D70` (perMachine), so installing 0.4.25 over an
  older build is a clean in-place upgrade — one Programs-and-Features entry, old ProductCode
  replaced, no side-by-side duplicate agent. CLIENTID/SERVERURL/ENROLLMENTKEY are preserved by
  the existing `bootstrap.json` / registered `config.json`; they are only needed on first install.
- **Velopack Setup.exe channel:** self-updates from the feed in §2 within 24h (delta download).
  Not the fleet path.

**MSP override knobs (HKLM\SOFTWARE\Toast2IT\Toast Notification):** `DisableAutoUpdate=1`
suppresses Velopack checks; `UpdateFeedUrl` points the Velopack channel at an internal mirror.

---

## 4. Standing rule

Any signed-MSI ship MUST also produce + sign + publish a Velopack release in the same session
(otherwise the auto-update feed for Setup.exe-channel installs goes stale). Verify both public
URLs with `curl -sI` after every deploy. Re-confirm the nginx `alias` path against the live
`sites-enabled/toast` before trusting any older doc.
