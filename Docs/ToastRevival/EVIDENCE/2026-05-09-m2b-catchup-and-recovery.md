# M2.B — Missed Catch-up + Orphan Recovery + Agent Dedup

**Date:** 2026-05-09
**Milestone:** M2.B (D6 + INFO-M2A-003 + INFO-M2A-004)
**Status:** SHIP WITH NOTES (FIX-M2B-001 patched pre-commit; 4 INFO deferred to M3/M5)

## Summary

M2.B closes the agent's offline-recovery story. Three structural pieces:

1. **Backend `GET /api/notifications/pending`** — device-JWT-authenticated endpoint that returns the same signed `(payloadJson, signature)` pairs the hub fanout would have pushed for any `Pending` deliveries the agent missed while disconnected.
2. **Backend orphan recovery sweep** — `NotificationQueueService.RecoverOrphansAsync` runs once at startup, marks notifications stuck in `Sending` past 5 minutes as `Failed`. **Pending deliveries are NOT touched** (Carl's overrule on the original plan) so the catch-up endpoint can still deliver them.
3. **Agent reconnect catch-up + dedup** — on every `Reconnected` event and once after cold `StartAsync`, the agent fetches pending and runs each through the same verify→render→report pipeline as live hub messages. A 1-hour `MemoryCache` dedup window prevents double-render across hub redelivery and catch-up overlap.

## Files Changed

| File | Change |
|---|---|
| `src/ToastRevival.Api/Services/NotificationPayloadBuilder.cs` | NEW — single source of truth for the wire shape + HMAC; called by both hub fanout and catch-up endpoint. |
| `src/ToastRevival.Api/Services/NotificationQueueService.cs` | `RecoverOrphansAsync` runs once at `ExecuteAsync` startup; `BuildSignedPayload` deleted in favor of the shared helper. |
| `src/ToastRevival.Api/Controllers/NotificationsController.cs` | New `GET /pending` action — device-JWT auth (`type=device` claim), `device-per-hour` rate limit, cap 100 items, ordered `CreatedAt` asc. |
| `src/ToastRevival.Api/DTOs/NotificationDtos.cs` | New `PendingNotificationItem` record. |
| `src/ToastRevival.Agent/AgentClient.cs` | New `RunCatchupAsync` + `_renderedCache` MemoryCache; render+ReportDelivery extracted into shared `RenderAndReportAsync`; `_lastCatchupSince` (nullable, FIX-M2B-001). |
| `src/ToastRevival.Agent/ToastRevival.Agent.csproj` | +`Microsoft.Extensions.Caching.Memory 8.0.1`. |

## Wire Shapes

### Catch-up Request

```http
GET /api/notifications/pending HTTP/1.1
Authorization: Bearer <device-JWT>
```

Or with the `since` filter:

```http
GET /api/notifications/pending?since=2026-05-09T18%3A30%3A45.1234567Z HTTP/1.1
Authorization: Bearer <device-JWT>
```

### Catch-up Response

```json
[
  {
    "notificationId": "f3...",
    "payloadJson": "{\"notificationId\":\"f3...\",\"title\":\"...\",...}",
    "signature": "base64-hmac",
    "createdAt": "2026-05-09T18:25:00Z"
  },
  ...
]
```

`payloadJson` and `signature` are byte-identical to what the hub would have sent via `ReceiveNotification(payloadJson, signature)`. Agent runs the same `HmacVerifier.Verify` (constant-time compare) regardless of which channel delivered the payload.

## Build Evidence

```
$ dotnet build ToastRevival.sln
  ToastRevival.Api -> bin\Debug\net8.0\ToastRevival.Api.dll
  ToastRevival.Agent -> bin\Debug\net8.0-windows10.0.19041.0\ToastNotification.Agent.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

```
$ dotnet build src\ToastRevival.Agent\ToastRevival.Agent.csproj `
    -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=false `
    -p:AppxPackageSigningEnabled=false -p:TargetPlatformVersion=10.0.22621.0
  ToastRevival.Agent -> bin\Debug\net8.0-windows10.0.19041.0\win-x64\ToastNotification.Agent.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

(MSIX smoke check confirms the new `Microsoft.Extensions.Caching.Memory` package reference resolves cleanly under the MSIX build path. Manifest standing checks unchanged from M0 D5 — no manifest edit this session.)

## Code Sweep Outcome

**FIX-M2B-001** caught and patched **pre-commit** by Abish:

> First implementation initialized `_lastCatchupSince = DateTime.UtcNow` at ctor. The catch-up GET would send `since=<ctor_time>` on the first call. Server filter `delivery.CreatedAt >= since` would have excluded EVERY pre-existing Pending delivery — exactly the case M2.B exists to fix (agent rebooted, has Pending from before the reboot, reconnects). The catch-up endpoint would have returned zero results in its primary scenario.

Fix: `_lastCatchupSince` made nullable; first call omits the `since` query param so the server returns all Pending up to the cap. Subsequent calls send the captured `nextSince` timestamp. Build clean post-patch.

**4 INFO findings deferred** (non-blocking; M3/M5 work):

- INFO-M2B-002: pending endpoint pagination beyond 100
- INFO-M2B-003: composite DB index `(DeviceId, Status, CreatedAt)`
- INFO-M2B-004: MemoryCache `SizeLimit` to bound dedup growth
- INFO-M2B-005: separate higher-budget rate-limit policy for catch-up

## New Standing Rules (Carl, project context)

1. **Orphan recovery semantic** — notification → `Failed`; pending deliveries STAY pending. Original FIX-LIST plan to mark deliveries Failed would have defeated catch-up.
2. **Single signed-payload source of truth** — any new path emitting a notification payload to an agent must call `NotificationPayloadBuilder.BuildSigned`. Byte-deterministic equivalence between channels is structural, not coincidental.
3. **Catch-up `since` initialization** — any future catch-up endpoint with a `since` param must initialize the agent-side tracking in a way that does NOT exclude pre-existing pending state on first run. Nullable + omit-on-first-call is the canonical pattern (FIX-M2B-001 lesson).

## Boundaries

- Structural correctness (handler shape, auth boundary, dedup wiring, recovery semantic, byte-identical signing) confirmed via build + Code Sweep.
- End-to-end runtime confirmation requires a lab run: real Postgres + API + signed agent → simulate disconnect → send notification → reconnect → observe catch-up GET fires + delivery completes. Hand-off pattern matches M2.A.
- Test coverage gap (INFO-M1-004) inherited from M1; first tests land at M8.

## Next Session

**M2.C** — Diana-engaged. D7 system tray icon (Diana to ship iconography spec for the four states: connected / reconnecting / disconnected / error/unconfigured) + D9 WiX MSI properties (`CLIENTID`, `SERVERURL` → `bootstrap.json` next to the exe at install). The agent already reads `bootstrap.json` at first run (M2.A); D9 just wires the MSI to write it.
