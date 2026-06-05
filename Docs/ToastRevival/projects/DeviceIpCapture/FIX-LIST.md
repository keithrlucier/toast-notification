# Issue Tracking

## Active Issues

*No issues yet — populated during development.*

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
