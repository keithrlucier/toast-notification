# Project Milestones: Device IP Capture & Display

## Overview

**Total Milestones:** 2  
**Estimated Timeline:** 2–3 sessions  
**Current Status:** Planning Complete — Ready for Milestone 1

---

## Milestone 1: Backend — Migration, Model, DTOs, Controller

**Status:** ✅ COMPLETE (2026-06-05)  
**Priority:** High  
**Dependencies:** None

**What we're delivering:**
- [x] EF Core migration: add `WanIpAddress varchar(64)` and `LanIpAddress varchar(64)` (both nullable) to `Devices` table — `Data/Migrations/20260605150506_AddDeviceIpAddresses`
- [x] `Device` model: add `string? WanIpAddress` and `string? LanIpAddress` properties
- [x] `RegisterDeviceRequest` DTO: add `string? LanIpAddress = null` (optional parameter — backward compat)
- [x] `PingRequest` DTO: add `string? LanIpAddress = null` (optional parameter — backward compat)
- [x] `DeviceResponse` DTO: add `string? WanIpAddress` and `string? LanIpAddress`
- [x] `DevicesController.Register()`: WAN via `CloudflareIpValidator.ResolveTrustedClientIp(HttpContext)` on both new-device and re-register branches; stale bare `RemoteIpAddress` in the audit call replaced with `ResolveTrustedClientIp`. LAN guarded with a non-empty check on re-register (never nulls an old agent's value); set directly on new-device.
- [x] `DevicesController.Ping()`: after `LastPing` / `AgentVersion`, refresh `device.WanIpAddress`; update `device.LanIpAddress` only when `body?.LanIpAddress` is non-empty
- [x] `DevicesController.List()` and `Get()`: include `WanIpAddress` and `LanIpAddress` in `DeviceResponse` projection (`ToResponse`)
- [x] **Added beyond plan (QA finding):** `ClampIp()` defensive cap at all six IP write sites so an authenticated agent's over-length value can't 500 a register/ping against the new `varchar(64)` bound

**How we know it's done:**
- Migration applies cleanly against dev database
- `POST /api/devices/register` (new device): Device row in DB has non-null WanIpAddress; LanIpAddress is null (no agent sending it yet — that's M2)
- `POST /api/devices/ping`: device row WanIpAddress refreshes; LanIpAddress updates if body carries it
- `GET /api/devices` response includes `wanIpAddress` and `lanIpAddress` fields
- Old PingRequest with only `{ agentVersion }` body still deserializes cleanly (no 400)

**Technical notes:**
- Use `ResolveTrustedClientIp(HttpContext)` consistently — it handles the Cloudflare/nginx/loopback cases. The existing bare `RemoteIpAddress` at line 162 is wrong in production.
- `LanIpAddress` in `PingRequest` is nullable: if an old agent sends no LAN, the existing value is NOT overwritten (only update when the incoming value is non-null/non-empty).
- String max 64 chars is sufficient for any IPv4 or IPv6 address including zone IDs.
- Abish QA gate runs before this milestone merges.

**Testing required:**
- Dev registration with a local agent: confirm WanIpAddress populated as loopback/127.0.0.1 (expected in dev where peer is loopback, ResolveTrustedClientIp falls back to XFF or socket peer)
- Ping endpoint: POST with `{ "agentVersion": "x", "lanIpAddress": "192.168.1.100" }` — confirm device row updated
- Ping endpoint: POST with old-format `{ "agentVersion": "x" }` only — confirm no 400, existing lanIpAddress not cleared
- Abish blast-radius + five-perspective review before merge

---

## Milestone 2: Agent + Frontend — LAN IP Collection, UI Column

**Status:** ✅ COMPLETE (2026-06-05)  
**Priority:** High  
**Dependencies:** Milestone 1 (backend must be live before agent ships)

**What we delivered:**
- [x] `NetworkUtils.cs` (new): static `GetLocalIPv4()` extracted from `DesktopOverlayService.cs`. First UP, non-loopback, non-link-local IPv4.
- [x] `DesktopOverlayService.cs`: delegates to `NetworkUtils.GetLocalIPv4()`; three `System.Net*` usings removed.
- [x] `AgentClient.cs` — registration + both ping sites (`ReportVersionAsync` + `RunPingLoopAsync`): `lanIpAddress = NetworkUtils.GetLocalIPv4()` added.
- [x] Agent 0.4.39 → 0.4.40: csproj + appxmanifest + appsettings.json bumped.
- [x] MSI built, signed (thumbprint 19B07B46), MSIX unsigned, Setup.exe signed. SHA256 verified on TOASTWEB1.
- [x] `devices.ts`: `wanIpAddress: string | null` + `lanIpAddress: string | null` in `Device`, `DeviceApiResponse`, `normalizeDevice`.
- [x] `Devices.tsx`: IP column after User — mono/dim, 20-char WAN truncation, native title tooltip (WAN+LAN when non-null), em dash when both null.
- [x] Diana sign-off: column styling approved.
- [x] Abish QA: SHIP WITH NOTES. 3 findings filed to FIX-LIST (M2-001/002/003).

**How we know it's done:**
- Registered device (using updated agent) shows WAN IP and LAN IP in admin panel
- Hovering the IP cell shows the tooltip with both values
- A device registered pre-feature shows a dash — no crash, no empty cell jank
- Agent on a machine with only one active NIC shows its IPv4 address correctly
- Agent build carries updated version, MSI signed, deploy confirmed on TOASTWEB1

**Technical notes:**
- `NetworkUtils.GetLocalIPv4()` is `public static string?` — null-safe return, same try/catch wrapping as the current DesktopOverlayService implementation.
- The registration anonymous object in `AgentClient.cs` uses camelCase property names for JSON serialization — `lanIpAddress` (lowercase l) must match the DTO field name for default System.Text.Json deserialization.
- The ping anonymous object similarly — `lanIpAddress`.
- Frontend: use the existing Radix UI Tooltip component pattern from the codebase (check how other columns handle tooltips — the agent version column may already have one for update indicators).
- After agent deploy: update `Agent__LatestVersion` on TOASTWEB1 so the update-check endpoint offers the new version to existing agents.
- Mirror to public repo after tag (sync-public-mirror.ps1 per the PUBLIC-MIRROR.md workflow).
- Abish QA gate before merge.

**Testing required:**
- Full registration flow with new agent on a dev machine
- Verify WAN IP is real client IP (not Cloudflare edge) in production
- Verify LAN IP matches expected local interface
- Table renders at 1366px without horizontal scroll regression
- Tooltip accessibility: keyboard focus triggers tooltip
- Old-agent device in admin panel: dash displayed, no errors

---

## Dependency Map

```
Milestone 1 (Backend)
       ↓
Milestone 2 (Agent + Frontend)
```

Backend must be live before agent ships — new agent payload fields must be accepted by the API.

---

## Post-MVP / Future Work

- IPv6 LAN address capture (currently filtered by AddressFamily == InterNetwork)
- GetBestInterface() for multi-NIC primary-adapter detection
- IP change history / audit trail per device

---

## Completion Log

| Milestone | Completed | Notes |
|-----------|-----------|-------|
| 1 | 2026-06-05 | Backend complete. 6 src files + migration. Build clean, migration DDL verified via idempotent script, integration tests written (DeviceIpCaptureTests.cs, 6 facts) — CI-executed (no local Docker/PG this session). Abish Code Sweep: SHIP. One finding fixed (over-length IP clamp). Blast-radius catch: migration relocated to Data/Migrations to match convention. |
| 2 | 2026-06-05 | Agent 0.4.40 + dashboard IP column. NetworkUtils extracted, 3 payloads updated, MSI/EXE/Setup.exe signed, MSIX unsigned. Dashboard bundle deployed. SHA256 verified. Public mirror v0.5.37. Abish: SHIP WITH NOTES (3 FIX-LIST items). |
