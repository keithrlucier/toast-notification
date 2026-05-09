# M2.A — Agent ↔ Backend Pipeline + HMAC

**Date:** 2026-05-09
**Slice scope:** D1 SignalR client + auto-reconnect, D2 payload-driven toast renderer, D3 device registration + heartbeat, D4 HMAC payload verification, D5 interaction tracking, INFO-D5-001 single-instance mutex, INFO-MSIX-004-D activation handler. Plus new device-JWT-authenticated REST `POST /api/notifications/{id}/interactions` endpoint.

## Architecture

```
                 ┌───────────────────────────────────────────────┐
                 │  ToastRevival.Api (M1 + M2.A delta)           │
                 │                                               │
                 │  AuthController.Register                      │
                 │    → Tenant{ SigningKey = base64(32b) }       │
                 │                                               │
                 │  DevicesController.Register                   │
                 │    → DeviceTokenResponse{ Token, DeviceId,    │
                 │                            TenantId,          │
                 │                            SigningKey }       │
                 │                                               │
                 │  NotificationQueueService.ProcessAsync        │
                 │    → BuildSignedPayload(notification, key)    │
                 │      = (payloadJson, HMACSHA256(json, key))   │
                 │    → hub.SendAsync("ReceiveNotification",     │
                 │                    payloadJson, signature)    │
                 │                                               │
                 │  NotificationsController                      │
                 │    POST /api/notifications/{id}/interactions  │
                 │      device-JWT auth + tenant cross-check     │
                 │      → DeliveryStatus update                  │
                 │      → DeliveryUpdate broadcast to tenant grp │
                 │                                               │
                 │  Migration AddTenantSigningKey                │
                 │    + Backfill via gen_random_uuid()           │
                 └───────────────────────────────────────────────┘
                              ▲                       ▲
                  POST /register         WS /hubs/notifications
                              │                       │
                 ┌────────────┴───────────────────────┴──────────┐
                 │  ToastRevival.Agent                           │
                 │                                               │
                 │  Program.cs — 3-mode entry dispatch           │
                 │   ├── ActivationMode (-AppNotificationActivat)│
                 │   │     short-circuit BEFORE mutex/SignalR    │
                 │   │     → Register() → wait NotificationInvok │
                 │   │     → InteractionFallback.PostAsync (REST)│
                 │   │     → exit                                │
                 │   ├── DiagnosticMode (--template)             │
                 │   │     legacy M0A path; no hub               │
                 │   └── PrimaryMode (no special args)           │
                 │         → Local\ mutex (per-session)          │
                 │         → ConfigStore.TryLoad / RegisterAsync │
                 │         → AppNotificationManager.Register     │
                 │         → AgentHubClient                      │
                 │             ├── HubConnection                 │
                 │             │     WithAutomaticReconnect      │
                 │             │       [0, 2, 5, 10, 30] s       │
                 │             ├── On("ReceiveNotification")     │
                 │             │     → HmacVerifier.Verify       │
                 │             │       (FixedTimeEquals)         │
                 │             │     → BuildFromPayload(payload) │
                 │             │     → Show(notification)        │
                 │             │     → ReportDelivery            │
                 │             ├── NotificationInvoked           │
                 │             │     → ReportInteraction(id, act)│
                 │             └── 30-min ping loop              │
                 │                                               │
                 │  ConfigStore — atomic temp+Move, packaged     │
                 │  LocalState OR %LOCALAPPDATA%\Toast2IT\...    │
                 └───────────────────────────────────────────────┘
```

## Files Changed

**Backend (10 files)**:

- `src/ToastRevival.Api/Models/Tenant.cs`: + `SigningKey` property.
- `src/ToastRevival.Api/Controllers/AuthController.cs`: generate 32-byte signing key on tenant create.
- `src/ToastRevival.Api/Controllers/DevicesController.cs`: load tenant in Register, return SigningKey.
- `src/ToastRevival.Api/Controllers/NotificationsController.cs`: + `POST /{id}/interactions` (device-JWT auth) + IHubContext injection.
- `src/ToastRevival.Api/DTOs/DeviceDtos.cs`: + SigningKey field on `DeviceTokenResponse`, + `InteractionRequest`.
- `src/ToastRevival.Api/Services/NotificationQueueService.cs`: + `BuildSignedPayload`, send `(payloadJson, signature)`.
- `src/ToastRevival.Api/Migrations/20260509002218_AddTenantSigningKey.cs` (NEW): + column + backfill SQL.
- `src/ToastRevival.Api/Migrations/20260509002218_AddTenantSigningKey.Designer.cs` (NEW): autogen.
- `src/ToastRevival.Api/Migrations/AppDbContextModelSnapshot.cs`: autogen update.

**Agent (5 files, 3 new)**:

- `src/ToastRevival.Agent/ToastRevival.Agent.csproj`: + `Microsoft.AspNetCore.SignalR.Client 8.0.15`, + `Microsoft.Extensions.Http 8.0.1`.
- `src/ToastRevival.Agent/Program.cs`: rewritten to 3-mode entry dispatch (Activation / Diagnostic / Primary).
- `src/ToastRevival.Agent/ToastTemplates.cs`: + `BuildFromPayload(ToastPayload)` and shared helpers (TryUri, TryParseScenario, ApplyAudio).
- `src/ToastRevival.Agent/AgentClient.cs` (NEW): RegistrationService + AgentHubClient + InteractionFallback + ThisAssembly version helper.
- `src/ToastRevival.Agent/DeviceConfig.cs` (NEW): DeviceConfig record, BootstrapConfig, ConfigStore (Load / Save / atomic-write / TryLoadBootstrap).
- `src/ToastRevival.Agent/ToastPayload.cs` (NEW): ToastPayload record + PayloadButton + HmacVerifier (constant-time compare via `CryptographicOperations.FixedTimeEquals`).

## Build Results

- `dotnet build ToastRevival.sln`: **0 warnings, 0 errors**.
- MSIX smoke check (`-p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:TargetPlatformVersion=10.0.22621.0`): clean modulo pre-existing FIX-MSIX-003 cosmetic.
- All M0 D5 manifest standing checks intact (`windows.comServer` + `windows.toastNotificationActivation` + `windows.startupTask`, four-dash sentinel on `<com:ExeServer>`, CLSID byte-identical, MaxVersionTested=10.0.22621.0).
- `dotnet ef migrations add AddTenantSigningKey`: passed (modulo pre-existing INFO-M1-001 DeviceGroupMember warning).

## Smoke Tests

- **DiagnosticMode regression**: `dotnet run -- --template plain --no-wait` → `Toast Notification sent. Template: Plain` → exit code 0. M0A argv path preserved.
- **PrimaryMode unconfigured-exit**: no env vars + no bootstrap.json → exit code 9 with clear stderr message.

## Code Sweep — FIX-M2A-001 (BLOCKING, patched pre-commit)

`Program.cs:14` mutex name was `Global\Toast2IT.ToastNotification.PrimaryWorker`. The `Global\` prefix uses the kernel's system-wide BaseNamedObjects namespace, meaning two interactive users on the same Win11 box (Fast User Switching, RDP, Terminal Services) would have user 2's agent collide with user 1's mutex and exit code 5. **Regresses M0 D4 multi-user verification.** Patched to `Local\` prefix (per-session BaseNamedObjects). Each Windows session gets its own primary worker. Build re-verified clean post-patch.

## Code Sweep — INFO findings (deferred)

| ID | Surface | Owner | Defer to |
|----|---------|-------|----------|
| INFO-M2A-002 | DeviceConfig plaintext at rest | Anthony | M3 (DPAPI) |
| INFO-M2A-003 | Orphan `Sending` rows on service crash | Anthony | M2.B (startup recovery) |
| INFO-M2A-004 | Agent has no notificationId de-dup | Anthony | M2.B (1-hour sliding cache) |
| INFO-M2A-005 | Migration backfill needs Postgres 13+ | Carl | M9 (deploy doc) |

## Hand-off

This slice does NOT include end-to-end runtime verification. That requires:

1. PostgreSQL instance running with `Jwt:Key` ≥ 32 chars set in `appsettings.json` env override.
2. `dotnet run --project src/ToastRevival.Api` with the migration applied (`dotnet ef database update --project src/ToastRevival.Api`).
3. Agent build → set `TOAST_TENANT_ID` + `TOAST_SERVER_URL` → `dotnet run --project src/ToastRevival.Agent` to verify first-run registration + hub connect.
4. Send a notification via `POST /api/notifications` with the user JWT and observe agent render.
5. Click a button on the toast and observe `ReportInteraction` reach the hub (and an `InteractionTracked` row).
6. Kill the agent, click a toast button, observe activation-handler launch + REST POST to `/api/notifications/{id}/interactions`.

End-to-end is a future-session lab gate paired with MSI/MSIX rebuild + signing.

## Boundaries

- The wire-protocol contract is structurally correct (server signs the byte sequence it sends; agent verifies the byte sequence it receives; both sides operate on the same UTF-8 byte string thanks to pre-serialization on the server).
- Multi-tenant isolation continues to flow through JWT claims + EF global query filters (no change from M1).
- Test coverage gap (INFO-M1-004) inherited; first tests at M8.
