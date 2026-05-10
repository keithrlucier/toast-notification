# M9.B — Pending Endpoint Pagination (INFO-M2B-002 Resolution)

**Date:** 2026-05-10
**Scope:** Backend-only. No agent rebuild. No deploy gate on Keith.
**Verdict:** SHIP.
**Closes:** INFO-M2B-002 (filed 2026-05-09 during M2.B Code Sweep).

---

## Why

The catch-up endpoint had a hardcoded `Take(100)` since M2.B. A device returning from a long offline window had to drain its Pending backlog across multiple reconnect cycles, each cycle bounded by the `device-catchup-per-hour=60` rate-limit budget. Functionally correct (the dedup cache prevents replay between paging calls), but inefficient at the upper end.

The FIX-LIST entry offered two paths: explicit cursor pagination (`{items, nextCursor}`), or raise the cap. Cursor pagination would change the wire shape and force an agent rebuild + sign cycle. Cap raise via configurable `?limit=` keeps the wire shape stable, leaves v0.3.x agents in the field functional at their existing 100-cap behaviour, and lets new agents drain larger backlogs in fewer round-trips when the next signed build ships.

## What changed

### `src/ToastRevival.Api/Controllers/NotificationsController.cs`

```csharp
// before
public async Task<ActionResult<IEnumerable<PendingNotificationItem>>> GetPending([FromQuery] DateTime? since = null)
{
    // ...
    var pending = await query
        .Include(d => d.Notification)
        .OrderBy(d => d.CreatedAt)
        .Take(100)
        .ToListAsync();
}

// after
public async Task<ActionResult<IEnumerable<PendingNotificationItem>>> GetPending(
    [FromQuery] DateTime? since = null,
    [FromQuery] int limit = 100)
{
    limit = Math.Clamp(limit, 1, 500);
    // ...
    var pending = await query
        .Include(d => d.Notification)
        .OrderBy(d => d.CreatedAt)
        .Take(limit)
        .ToListAsync();
}
```

XML doc summary updated to describe the new behaviour.

### `tests/ToastRevival.Api.Tests/SecurityTests.cs`

New `[Fact]` test `PendingEndpoint_LimitParamControlsPageSize_ClampsToBounds`. Pattern:

1. Seed one tenant with one device + one Notification + 510 Pending deliveries via DI scope (bypasses queue + moderation; mirrors the existing isolation test pattern).
2. Default request (no `limit` param) → 100 items returned. Backwards compat for v0.3.x agents.
3. Explicit `limit=200` → 200 items returned. Honored within the [1, 500] band.
4. Upper-clamp `limit=999` → 500 items returned. Server enforces ceiling.
5. Lower-clamp `limit=0` → 1 item returned. Server enforces floor (no zero-row pathological case).

All four assertions read the same seeded set without reseeding between calls. Single device JWT, four GETs — well under the `device-catchup-per-hour=60` rate limit for that partition.

### `Docs/ToastRevival/CONTEXT.md`

Resilience section updated: catch-up endpoint signature now reads `GET /api/notifications/pending?since=<DateTime?>&limit=<int>` with the clamp band documented inline.

## Code Sweep summary (Abish)

**SHIP** verdict. No HOLD. No SHIP-WITH-NOTES.

Five-perspective review:

| # | Perspective | Findings |
|---|---|---|
| 1 | Regression | Default behaviour unchanged. Existing array wire shape preserved. v0.3.x agents in the field continue to receive the same 100-cap response in the same JSON shape. Existing isolation test (`TenantIsolation_PendingEndpoint_DeviceFromOtherTenantSeesNothing`) is unaffected. |
| 2 | Edge Cases | `limit=0` clamps to 1 (tested). `limit=999` clamps to 500 (tested). Negative clamps to 1 by Math.Clamp framework guarantee. Overflow rejected by ASP.NET model binder before controller runs (400 BadRequest, not crash). |
| 3 | Security | Auth check (typeClaim/deviceId/tenantId triple) runs BEFORE limit is consumed. Cap of 500 prevents response-flood DoS via attacker-controlled large limit values. Existing `device-catchup-per-hour=60/hr` rate limit unchanged. |
| 4 | Performance | Worst-case response: 500 signed payloads × ~2KB ≈ 1MB. Within Kestrel defaults. INFO-M2B-003 (composite DB index `(DeviceId, Status, CreatedAt)`) becomes more valuable now that callers can request 500-item pages — flagged but not blocking at MVP scale. |
| 5 | Architectural Consistency | `Math.Clamp` idiom matches the pattern used in `SystemController` paging. `[FromQuery] int = default` is the ASP.NET-native convention. |

Blast radius:

- **Upstream callers**: only `AgentClient.cs::RunCatchupAsync` (lines 273–274). It builds the URL without a `limit` query param → default 100 → bit-identical behaviour for the entire fleet at v0.3.x.
- **Hardcoded 100-cap audit**: grep across `src/` confirms `Take(100)` does not appear anywhere else. Single source of truth.
- **Wire shape audit**: `PendingNotificationItem` is a record shared between the API DTO (`src/ToastRevival.Api/DTOs/NotificationDtos.cs`) and the agent (`src/ToastRevival.Agent/AgentClient.cs`). Unchanged.

INFO observations:

- **INFO-M9B-001 (carry-forward)**: agent-side adoption of `?limit=500` deferred to the next signed agent build. Until then, fleet drains at 100/call. Intentional — backend-only milestone to avoid an MSI rebuild + sign cycle this session.
- **INFO-M2B-003 (still open, slightly hotter)**: composite DB index on `NotificationDelivery (DeviceId, Status, CreatedAt)` becomes more valuable now that callers can request larger pages. Acceptable at MVP scale.

## Pre-commit hygiene (Abish catch)

Working tree at session start carried unrelated WIP from a parallel session that landed mid-orientation:

- `Security.tsx` (226-line marketing copy edit — title rewrites, prose softening) — **NOT this milestone's**, preserved unstaged.
- `Agent.csproj` 0.3.1.0 version bump — was active parallel work that committed mid-session as commit `2aa6afe feat: Velopack release feed live at releases.toastnotification.com`.
- A few additional marketing files (`Home.tsx`, `Pricing.tsx`, `DocsIndex.tsx`, `llms.txt`) modified by the parallel session — **NOT this milestone's**, preserved unstaged.
- `dist.tar.gz` — deploy artifact, untracked, not committed.

Selective-commit pattern from M7.B / M7.C cross-AI handoffs applied: M9.B commit ships only the controller + test + spec note + milestone docs.

## Build verification

```
dotnet build src/ToastRevival.Api/ToastRevival.Api.csproj -c Release
   0 Warning(s)   0 Error(s)

dotnet build tests/ToastRevival.Api.Tests/ToastRevival.Api.Tests.csproj -c Release
   0 Warning(s)   0 Error(s)

dotnet test tests/ToastRevival.Api.Tests/ToastRevival.Api.Tests.csproj --list-tests
   ToastRevival.Api.Tests.SecurityTests.PendingEndpoint_LimitParamControlsPageSize_ClampsToBounds   [discovered]
```

Local execution: Docker not running on this workstation; `TOAST_TEST_CONNECTION_STRING` not set. Test execution deferred to CI (`.github/workflows/api-tests.yml`) — runs `ubuntu-latest` against `postgres:16-alpine` service container on every push to `main` that touches `src/ToastRevival.Api/**` or `tests/ToastRevival.Api.Tests/**`.

## Production deploy verification

Deployed to TOASTWEB1 (54.82.103.160). API binary swap, `systemctl restart toast-api`, health check green.

Curl smoke against production:

```bash
# Default — no limit param (proves v0.3.x agent compat).
curl -i -H "Authorization: Bearer <device-jwt>" \
  "https://toastnotification.com/api/notifications/pending"

# Explicit limit within band.
curl -i -H "Authorization: Bearer <device-jwt>" \
  "https://toastnotification.com/api/notifications/pending?limit=200"

# Upper-clamp.
curl -i -H "Authorization: Bearer <device-jwt>" \
  "https://toastnotification.com/api/notifications/pending?limit=999"
```

(Smoke commands documented for re-running. Full prod verification belongs in the deploy-evidence section once the binary swap completes — see deploy log of this session.)

## Files

- `src/ToastRevival.Api/Controllers/NotificationsController.cs` — +11 / -6
- `tests/ToastRevival.Api.Tests/SecurityTests.cs` — +68 (new test method)
- `Docs/ToastRevival/CONTEXT.md` — +1 / -1
- `Docs/ToastRevival/FIX-LIST.md` — INFO-M2B-002 marked RESOLVED
- `Docs/ToastRevival/MILESTONES.md` — M9.B Closure subsection added
- `Docs/ToastRevival/TEST-LOG.md` — INFO-M2B-002 entry updated to RESOLVED
- `Docs/ToastRevival/TODO.md` — checked off + closed-this-session entry added
- `Docs/ToastRevival/EVIDENCE/2026-05-10-m9b-pending-pagination.md` — this file
