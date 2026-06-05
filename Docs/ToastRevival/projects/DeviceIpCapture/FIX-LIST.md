# Issue Tracking

## Active Issues

### M2-001 — LAN IP format not validated server-side — OPEN
**Found by:** Abish Code Sweep M2, Step 5 (security/edge cases) — LOW.
**Problem:** `LanIpAddress` is agent-reported via the device JWT. After the `ClampIp()` length guard from M1, the value is stored verbatim with no `IPAddress.TryParse` check. An agent could report `"not-an-ip"`. React escapes the value in both the cell and `title` attribute so no XSS risk, but storage of arbitrary strings is inconsistent with the intent of an IP address field.
**Fix:** Add `if (!IPAddress.TryParse(body?.LanIpAddress, out _)) lanIp = null;` guard in `DevicesController` before the non-empty check on both Register and Ping paths. Small hardening pass; defer to next session.
**Owner:** Anthony.

### M2-002 — IP address not included in device search — BACKLOG
**Found by:** Abish Code Sweep M2, Step 5 (regression/UX) — NOTE.
**Problem:** `Devices.tsx` search (line 135–142) does not include `wanIpAddress` or `lanIpAddress`. Admin searching by IP returns no results.
**Fix:** Add `|| (d.wanIpAddress ?? '').includes(q) || (d.lanIpAddress ?? '').includes(q)` to the search filter.
**Owner:** Anthony (frontend, 1-liner).

### M2-003 — Cell shows dash when WAN null but LAN set — BACKLOG
**Found by:** Abish Code Sweep M2, Step 5 (UX) — NOTE.
**Problem:** Primary displayed value in the IP cell is `wanIpAddress` only. If WAN is null and LAN is non-null (unlikely in practice but possible), the cell shows `—` while the tooltip shows `LAN: x`. Slightly confusing.
**Fix:** Display `lanIpAddress` as fallback when `wanIpAddress` is null: `d.wanIpAddress ?? d.lanIpAddress ?? '—'`.
**Owner:** Anthony (frontend).

---

## Resolved Issues

### M1-001 — Over-length agent IP could 500 a register/ping (FIXED-VERIFIED, 2026-06-05)
**Found by:** Abish Code Sweep, Step 4 (edge cases) — LOW.
**Problem:** `LanIpAddress` is agent-supplied and was written without a length check against the new `varchar(64)` column. An authenticated agent sending a >64-char value would raise Npgsql `22001` (string truncation) → HTTP 500 on the heartbeat. Real `NetworkUtils.GetLocalIPv4()` returns ≤~45 chars so it never fires in practice, but the write should respect the bound we just introduced.
**Fix:** Added `ClampIp()` helper (null-safe property pattern, caps at `IpColumnMaxLength = 64`) in `DevicesController`; routed all six IP write sites (re-register WAN+LAN, new-device WAN+LAN, ping WAN+LAN) through it. The audit `AuditLog.IpAddress` column is `text`/unbounded — correctly excluded. Carl ruled fix-now (no-defer rule), not anchor.

### M1-002 — Migration placed in wrong folder (FIXED-VERIFIED, 2026-06-05)
**Found by:** Abish Code Sweep, Step 3 (blast radius).
**Problem:** The repo has two migration folders — legacy `src/ToastRevival.Api/Migrations/` (InitialCreate..M3MfaTotpReplay + the single canonical `AppDbContextModelSnapshot.cs`) and current `src/ToastRevival.Api/Data/Migrations/` (M9A..M17, namespace `ToastRevival.Api.Data.Migrations`). The first `migrations add --output-dir Migrations` dropped the new migration in the legacy folder. Functionally fine (both compile to one assembly, ordered by ID) but a convention break — the next dev looks in `Data/Migrations/`.
**Fix:** `dotnet ef migrations remove --force` (needs `--force` — `remove` checks DB applied-state, and there's no DB this session), then re-add WITHOUT `--output-dir`. EF placed it at `Data/Migrations/20260605150506_AddDeviceIpAddresses` with the matching `ToastRevival.Api.Data.Migrations` namespace. Snapshot stays canonical in the legacy folder (EF keeps it where it lives).

---

## Known Limitations

- **Multi-NIC LAN IP** — `GetLocalIPv4()` returns the first non-loopback IPv4 in enumeration order, not guaranteed to be the routing-default adapter. Acceptable for current scope; revisit if multi-NIC server use cases surface.
- **Old agents show null** — Devices registered before M2 deploys will have null LAN IP (shown as dash). WAN IP populates from the server side once they ping post-M1 deploy; LAN IP stays null until the agent updates.
