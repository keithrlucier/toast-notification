# Design Specification: Device IP Capture & Display

**Project:** Device IP Capture & Display  
**Designer:** Diana Reyes  
**Last Updated:** 2026-06-04  
**Status:** Approved

---

## Scope

One new column in the `Devices.tsx` admin table. No new pages, no new cards, no new modals. This is a table column spec.

---

## Column Spec: "IP"

### Placement

Insert after the "User" column (currently position 2). IP address is identifying/network information — it belongs near the front with DeviceName and Username, not buried after status columns.

**Resulting column order:** Machine | User | **IP** | Groups | OS | Agent Version | Status | Last Seen | Registered

### Cell Content

**Primary value:** `wanIpAddress`, rendered as monospace text.

**Truncation:** If the WAN IP string exceeds 20 characters (IPv6), truncate with ellipsis. Full value in tooltip. IPv4 addresses (≤15 chars) are never truncated.

**Null state:** When both `wanIpAddress` and `lanIpAddress` are null (old agent, not yet updated) — render a dash (`—`), same styling as other nullable columns in the table (muted text color, not empty cell).

**Typography:** Monospace font for the IP value. Same size as other data cells. No bold, no color callouts.

### Tooltip

Triggered on hover AND keyboard focus (accessibility requirement — the tooltip must be reachable without a mouse).

**Content:**
```
WAN  203.0.113.42
LAN  192.168.1.42
```

Two-line layout. Labels "WAN" and "LAN" in the secondary text color, values in primary. Monospace for the IP values. If WAN is null, omit that line. If LAN is null, omit that line. If both are null, show no tooltip at all (just the dash cell).

**Tooltip trigger:** Use the existing Radix UI Tooltip component pattern from the codebase. Match the delay and styling of any existing tooltips in the table (check agent version column — it may already have update-indicator tooltips).

### Column Width

Fixed or min-width at 120px. IPv4 WAN addresses are ≤15 chars; this is comfortable. IPv6 addresses are truncated to 20 chars + ellipsis before hitting the width constraint.

### Responsive / Overflow

The table already handles horizontal scroll at narrow viewports. This column adds ~120px to the total width. Do not add explicit hide-on-mobile logic — the existing table scroll behavior is sufficient.

---

## Non-Negotiables

1. **Monospace for IP values** — not system font, not Inter. These are technical strings.
2. **Tooltip on keyboard focus** — not hover-only. Accessibility is not optional.
3. **Dash, not empty** — null values get a dash. Empty cells are ambiguous.
4. **No color on the IP** — no green/red/yellow coding. IP address is informational, not a status indicator.
5. **No "N/A"** — a dash is clean. "N/A" is noise.

---

## Open Questions

*None — spec is complete for current scope.*

---

## Decisions Log

### 2026-06-04 — One column, not two
**Decision:** Single "IP" column, WAN primary, LAN in tooltip  
**Rationale:** Table was at 8 columns; adding 2 more IP columns crowded it to 10 — uncomfortable at 1366px viewport. WAN is the operationally relevant value at a glance; LAN is secondary context that belongs in the tooltip.  
**Rejected:** Two separate columns (WAN, LAN); combined cell with stacked values (breaks row height)
