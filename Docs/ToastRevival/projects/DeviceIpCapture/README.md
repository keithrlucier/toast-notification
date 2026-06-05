# Device IP Capture & Display

## What This Is

Adds WAN IP (server-captured) and LAN IP (agent-reported) to each registered device record and surfaces both in the admin panel Devices table. Gives admins network context per device — useful for troubleshooting, auditing, and fleet visibility.

## Current Status

**Phase:** Milestone 1 — Backend ✅ COMPLETE (pending deploy) → Milestone 2 next (Agent + Frontend)  
**Last Updated:** 2026-06-05

## Architecture Overview

- **WAN IP:** Captured server-side on device registration and every 30-minute ping heartbeat using `CloudflareIpValidator.ResolveTrustedClientIp()`. Not spoofable by the agent.
- **LAN IP:** Reported by the agent from `NetworkUtils.GetLocalIPv4()` (refactored from DesktopOverlayService — the overlay already uses this logic). Sent in registration and ping payloads.
- **Display:** Single "IP" column in Devices.tsx — WAN primary, tooltip shows both WAN and LAN. Dash for devices not yet updated.

## Key Decisions

- **One column, not two** — table was already at 8 columns; WAN is the useful-at-a-glance value, LAN lives in the tooltip
- **ResolveTrustedClientIp() not RemoteIpAddress** — bare RemoteIpAddress returns Cloudflare edge IP in production
- **Refresh on every ping** — keeps IP current as devices move between networks (VPN, DHCP)

## Documentation

| Document | Purpose |
|----------|---------|
| CONTEXT.md | Architecture, data flow, technical decisions |
| MILESTONES.md | Build plan (2 milestones) |
| TODO.md | Current task list |
| FIX-LIST.md | Issue tracking |
| TEST-LOG.md | Test results |
| DESIGN-SPEC.md | Diana's UI spec for the IP column |
| EVIDENCE/ | Screenshots and test artifacts |
