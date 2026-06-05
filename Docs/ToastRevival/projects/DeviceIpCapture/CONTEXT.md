# Project Context: Device IP Capture & Display

## Overview

**What this is:** A backend + agent + frontend feature that persists each registered device's WAN IP address (server-captured from the HTTP connection) and LAN IP address (agent-reported from its primary network interface) on the Device database row, and surfaces both in the admin panel Devices table.

**Why we're building it:** Admins managing large Windows endpoint fleets have no network-level context in the device list today. IP visibility is table-stakes for troubleshooting ("which subnet is this machine on?"), security auditing ("is this device connecting from an expected location?"), and general operational clarity. The data is already available at the API layer — it just isn't being persisted or displayed.

**Success looks like:** Every device in the admin panel shows a WAN IP column. Hovering reveals the LAN IP (and full WAN if truncated). The IP refreshes automatically within 30 minutes of a network change via the existing ping heartbeat. Old agents (pre-feature) show a dash; no breaking behavior.

---

## Technical Architecture

### High-Level Design

```
Agent (Windows)                  API (ASP.NET Core)              Dashboard (React)
──────────────────               ──────────────────────          ──────────────────
NetworkUtils.GetLocalIPv4()  →   Register / Ping endpoint   →   Devices.tsx
  (refactored from                 ResolveTrustedClientIp()       "IP" column
   DesktopOverlayService)          persists WAN + LAN on          WAN primary
                                   Device row                     LAN in tooltip
```

### Technology Stack

| Component | Technology | Rationale |
|-----------|------------|-----------|
| Database migration | EF Core migrations | Consistent with existing migration pattern |
| IP capture (WAN) | CloudflareIpValidator.ResolveTrustedClientIp(HttpContext) | Already handles CF-Connecting-IP correctly; raw RemoteIpAddress returns Cloudflare edge IP in prod |
| IP capture (LAN) | NetworkInterface enumeration (agent-side) | GetLocalIPv4() already written, tested, and running in DesktopOverlayService |
| Frontend display | React, single column + Radix tooltip | One column, not two — keeps table from exceeding comfortable scroll width |

### Data Flow

1. **Registration:** Agent collects `NetworkUtils.GetLocalIPv4()`, sends in `RegisterDeviceRequest.LanIpAddress`. API server calls `ResolveTrustedClientIp(HttpContext)` for WAN, stores both on `Device` row.
2. **Heartbeat (30-min cadence):** Agent re-collects LAN IP, sends in `PingRequest.LanIpAddress`. API refreshes `device.WanIpAddress` and `device.LanIpAddress` alongside the existing `LastPing` / `AgentVersion` updates.
3. **List endpoint:** `GET /api/devices` returns `WanIpAddress` and `LanIpAddress` in `DeviceResponse`. Dashboard renders the "IP" column.

### Integration Points

- `DevicesController.Register()` — capture + persist WAN and LAN, both new-device and re-register branches
- `DevicesController.Ping()` — refresh WAN and LAN on every heartbeat
- `DevicesController.List()` / `GetById()` — map new Device fields to DeviceResponse
- `AgentClient.cs` — include `lanIpAddress` in both registration and ping payloads
- `DesktopOverlayService.cs` — refactor GetLocalIPv4() call-site to use shared NetworkUtils

---

## Component Details

### NetworkUtils.cs (new — Agent project)

**Purpose:** Shared static LAN IP helper extracted from DesktopOverlayService  
**Technology:** .NET System.Net.NetworkInformation  
**Responsibilities:** Enumerate network interfaces, return first UP, non-loopback, non-link-local IPv4. Return null on failure.  
**Connects to:** DesktopOverlayService (existing caller refactored), AgentClient (new caller)

**Note on selection logic:** "First wins" enumeration order — not guaranteed to return the routing-default adapter on multi-NIC machines. Acceptable for admin display purposes. A future improvement (if needed) would use GetBestInterface() P/Invoke.

### Device Model (updated)

**New fields:**
```csharp
public string? WanIpAddress { get; set; }
public string? LanIpAddress { get; set; }
```

**Max length:** 64 chars (covers IPv4 and full IPv6 with zone ID headroom).

### RegisterDeviceRequest (updated)

```csharp
public record RegisterDeviceRequest(
    Guid TenantId,
    string DeviceName,
    string Username,
    string? OsVersion = null,
    string? AgentVersion = null,
    string? EnrollmentKey = null,
    string? LanIpAddress = null);   // new — nullable for old-agent compat
```

### PingRequest (updated)

```csharp
public record PingRequest(
    string? AgentVersion = null,
    string? LanIpAddress = null);   // new — nullable for old-agent compat
```

### DeviceResponse (updated)

Add `string? WanIpAddress` and `string? LanIpAddress` to the response record.

---

## Security Considerations

**Authentication:** WAN IP is server-derived — not spoofable by the agent. LAN IP is agent-reported — trusted because it arrives on an authenticated device JWT. An agent could lie about its LAN IP, but there's no meaningful threat model there.  
**Authorization:** No change to existing device JWT auth.  
**Data Protection:** IP addresses are PII in some jurisdictions. Stored in the existing Device table — same access controls apply. No additional exposure beyond what the admin panel already shows.  
**CF-Connecting-IP correctness:** ResolveTrustedClientIp() only trusts the header when the peer is a Cloudflare egress IP or loopback (nginx passthrough) — already protects against IP spoofing via forged header.

---

## Technical Decisions Log

### Decision: Single "IP" column in admin panel, WAN primary, LAN in tooltip
**Date:** 2026-06-04  
**Decision:** One column labeled "IP" showing WAN IP. Tooltip on hover shows both WAN and LAN. Truncate at 20 chars for IPv6 compatibility.  
**Rationale:** The Devices table already has 8 columns. Two separate IP columns pushed it to 10 — uncomfortable at 1366px. WAN is the more operationally useful value at a glance; LAN is secondary context.  
**Alternatives Rejected:** Two separate columns (too wide); LAN-only (less useful); combined cell with two lines (breaks row height consistency).

### Decision: Use ResolveTrustedClientIp() for WAN, not HttpContext.Connection.RemoteIpAddress
**Date:** 2026-06-04  
**Decision:** WAN IP captured via CloudflareIpValidator.ResolveTrustedClientIp(HttpContext).  
**Rationale:** The existing line 162 in DevicesController uses the bare RemoteIpAddress — which returns the Cloudflare edge IP in production, not the actual client IP. ResolveTrustedClientIp() is already the approved method for this (used by rate limiter).  
**Alternatives Rejected:** Bare RemoteIpAddress (wrong in prod).

### Decision: Refresh IPs on every ping
**Date:** 2026-06-04  
**Decision:** Both WAN and LAN IP are updated on every POST /api/devices/ping heartbeat.  
**Rationale:** Devices can change networks (VPN connect/disconnect, Wi-Fi roaming, DHCP renewal). The 30-minute ping cadence is an acceptable staleness bound for an informational display.  
**Alternatives Rejected:** Update only on registration (stale after any network change); update on hub reconnect (more complex, not all clients always reconnect).

---

## Known Limitations

- LAN IP is "first" non-loopback IPv4, not guaranteed to be the routing-default adapter on multi-NIC machines. Acceptable for current scope.
- Old agents (pre-M2) will have null WAN IP shown as dash in the admin panel until they re-register or re-ping with the new agent. WAN IP will populate from the server side once they ping — LAN IP stays null until the agent is updated.

## Future Considerations

- Use GetBestInterface() for more reliable LAN IP selection on multi-NIC servers
- IPv6 LAN address support (currently filtered out by AddressFamily == InterNetwork check)
- IP history / change log (track IP changes over time)

---

## Environment Setup

This is a feature within the existing Toast Notification codebase. Follow the main README-SELF-HOST.md and existing dev setup. The migration runs automatically in development via Program.cs IsDevelopment() bypass.

**Migration gotcha (discovered M1):** the API project has **two** migration folders. New migrations belong in `src/ToastRevival.Api/Data/Migrations/` (namespace `ToastRevival.Api.Data.Migrations`) — that's where everything since `M9A_RegistrationFlow` lives. The legacy `src/ToastRevival.Api/Migrations/` folder holds only `InitialCreate..M3MfaTotpReplay` plus the single canonical `AppDbContextModelSnapshot.cs`. Run `dotnet ef migrations add <Name> --project src/ToastRevival.Api --startup-project src/ToastRevival.Api` **without** `--output-dir` — EF defaults to the last migration's folder (`Data/Migrations/`). Do not pass `--output-dir Migrations`; it drops the file in the legacy folder. The DB-backed integration tests require Docker (Testcontainers) or `TOAST_TEST_CONNECTION_STRING`.
