# Active TODO List

## Current Focus: ▶ Milestone 2 — Agent + Frontend

**M1 Backend ✅ COMPLETE (2026-06-05)** — all High/Medium items below done; details in MILESTONES.md + TEST-LOG.md. **Next session: start Milestone 2** (NetworkUtils extraction, AgentClient LAN payload, agent build/sign/deploy, devices.ts + Devices.tsx IP column). M2 is blocked only on the M1 backend being **deployed to TOASTWEB1** — see "Deploy gate" below.

> **Deploy gate for M2:** the M1 backend (migration + new API fields) must be live on TOASTWEB1 before the updated agent ships, so the API accepts the new `lanIpAddress` payload. M1 code is committed-ready; deploy is the team's job (commit → push → pull/migrate on TOASTWEB1 → mirror to public repo).

### High Priority (M1 — ✅ all done)

- [ ] Write EF Core migration: `WanIpAddress varchar(64) NULL` and `LanIpAddress varchar(64) NULL` on `Devices` table
- [ ] Add `string? WanIpAddress` and `string? LanIpAddress` to `Device` model (`src/ToastRevival.Api/Models/Device.cs`)
- [ ] Add `string? LanIpAddress = null` to `RegisterDeviceRequest` record (`DTOs/DeviceDtos.cs`) — optional param, backward compat
- [ ] Add `string? LanIpAddress = null` to `PingRequest` record (`DTOs/DeviceDtos.cs`) — optional param, backward compat
- [ ] Add `string? WanIpAddress` and `string? LanIpAddress` to `DeviceResponse` record (`DTOs/DeviceDtos.cs`)
- [ ] Update `DevicesController.Register()`:
  - Capture WAN via `CloudflareIpValidator.ResolveTrustedClientIp(HttpContext)` — replace bare `HttpContext.Connection.RemoteIpAddress` in audit call
  - Set `device.WanIpAddress` and `device.LanIpAddress` in BOTH the re-register branch (~line 119) and new-device branch (~line 136)
- [ ] Update `DevicesController.Ping()`:
  - After existing `LastPing` / `AgentVersion` updates, add: `device.WanIpAddress = CloudflareIpValidator.ResolveTrustedClientIp(HttpContext)`
  - Add: `if (!string.IsNullOrWhiteSpace(body?.LanIpAddress)) device.LanIpAddress = body.LanIpAddress`
- [ ] Update `DevicesController.List()` and `GetById()`: include `WanIpAddress` and `LanIpAddress` in `DeviceResponse` projections

### Medium Priority

- [ ] Verify migration applies cleanly against dev DB (`dotnet ef database update`)
- [ ] Manual test: POST /api/devices/register — confirm wanIpAddress in device row
- [ ] Manual test: POST /api/devices/ping with `{ "agentVersion": "x", "lanIpAddress": "192.168.1.100" }` — confirm device row updated
- [ ] Manual test: POST /api/devices/ping with old-format body — confirm no 400, lanIpAddress not cleared

### Blocked / Waiting

- [ ] Agent changes — **Blocked by:** M1 backend must deploy first

---

## Up Next (Milestone 2)

- [ ] Create `NetworkUtils.cs` in agent project (extract GetLocalIPv4 from DesktopOverlayService)
- [ ] Refactor DesktopOverlayService to call NetworkUtils.GetLocalIPv4()
- [ ] AgentClient.cs registration: add `lanIpAddress = NetworkUtils.GetLocalIPv4()`
- [ ] AgentClient.cs ping: add `lanIpAddress = NetworkUtils.GetLocalIPv4()`
- [ ] Bump agent version
- [ ] Build, sign, deploy agent to TOASTWEB1, update Agent__LatestVersion
- [ ] devices.ts: add wanIpAddress and lanIpAddress to DeviceApiResponse + Device interfaces
- [ ] Devices.tsx: add "IP" column (after "User"), WAN primary, tooltip for LAN + full WAN, dash when null

---

## Notes & Technical Debt

- The audit log call in Register() also uses the bare RemoteIpAddress — replace with ResolveTrustedClientIp there too (same fix, same line, don't leave the stale call in the audit path)
- LanIpAddress must only be overwritten on ping when the incoming value is non-empty — never null out an existing value because old agents send nothing

---

## Completed This Session

- [x] Codebase research: confirmed Device model gaps, correct WAN IP capture method, LAN IP in agent
- [x] Framework files created (CONTEXT.md, MILESTONES.md, TODO.md, FIX-LIST.md, TEST-LOG.md, README.md, DESIGN-SPEC.md)
