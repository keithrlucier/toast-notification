# Testing Log

## Test Sessions

### 2026-06-05 — M1 Backend

**Build verification**
- `dotnet build` API project: **clean, 0 warnings / 0 errors**.
- `dotnet build` test project: **0 errors** (1 pre-existing `PostgreSqlBuilder` obsolete warning in `PostgresFixture.cs`, unrelated to M1).

**Migration DDL verification (no live DB required)**
- `dotnet ef migrations script 20260602041637_M17_EnrollmentTokens --idempotent` confirms `20260605150506_AddDeviceIpAddresses` is the single block after M17, emits valid `ALTER TABLE "Devices" ADD "WanIpAddress"/"LanIpAddress" character varying(64)`, guarded by an `IF NOT EXISTS(... __EFMigrationsHistory ...)` check (idempotent), and stamps the history row. "Migration applies cleanly" verified at the SQL-generation layer.

**Integration tests written** — `tests/ToastRevival.Api.Tests/DeviceIpCaptureTests.cs` (6 facts), compile-clean against the real API surface:
1. `Register_NewDevice_PersistsWanIp_AndNullLanWhenNotSent` — old-agent register → WAN set, LAN null
2. `Register_NewDevice_WithLanPayload_PersistsLan` — new-agent register → LAN persisted
3. `Ping_WithLanPayload_RefreshesWan_AndUpdatesLan`
4. `Ping_OldFormatBody_NoLanField_Returns204_AndDoesNotClearStoredLan` — raw `{ "agentVersion": "0.4.26" }`, no 400, LAN preserved
5. `Ping_EmptyBody_Returns204_AndDoesNotClearStoredLan` — pre-0.4.26 no-body heartbeat
6. `ListDevices_ProjectsWanAndLanIpFields`

**Execution status: PENDING CI.** The integration harness (`PostgresFixture`) requires Docker (Testcontainers `postgres:16-alpine`) or a `TOAST_TEST_CONNECTION_STRING`. This session's environment has neither (verified: no docker CLI, no `\\.\pipe\docker_engine`, no Postgres service, nothing on `:5432`). `dotnet test` fails at `Failed to connect to Docker endpoint` — container provisioning, **not** test logic. Tests will execute in the CI/Docker harness. Not claimed as passed.

---

## Regression Testing

*Run after each milestone.*

- 2026-06-05 (M1): No behavioral regression expected. Changes are additive (2 nullable columns, trailing optional DTO params, additive response fields). Old agent payloads (`RegisterDeviceRequest` without `lanIpAddress`, `PingRequest` with only `agentVersion` or no body) deserialize unchanged — covered by tests 4 & 5. Audit-IP source changed from bare `RemoteIpAddress` (Cloudflare edge in prod) to `ResolveTrustedClientIp` (the documented intent). Existing `EndToEndNotificationTests` register/list flow unaffected (DeviceResponse gained fields, not removed/reordered consumers).

---

## Evidence Index

| File | Description | Date |
|------|-------------|------|
| (inline, session log turn 4) | Idempotent migration SQL — ALTER TABLE Devices ADD Wan/Lan character varying(64) | 2026-06-05 |
| tests/ToastRevival.Api.Tests/DeviceIpCaptureTests.cs | 6 integration facts encoding M1 acceptance criteria | 2026-06-05 |

---

## Test Coverage Notes

**M1 — covered by DeviceIpCaptureTests.cs (CI-executed):**
- [x] New device registration: wanIpAddress populated on Device row (test 1)
- [x] Registration with LAN payload: lanIpAddress persisted (test 2)
- [x] Ping with new LAN payload: both fields update (test 3)
- [x] Ping with old payload (no lanIpAddress) / no body: no 400, existing lanIpAddress not cleared (tests 4, 5)
- [x] GET /api/devices: wanIpAddress and lanIpAddress in response (test 6)
- Note: re-register branch IP refresh is exercised in prod (the idempotent same-machine reinstall path); not yet a dedicated test — candidate add in M2.

**Needs testing (M2):**
- Agent registration on real Windows machine: lanIpAddress populates
- Admin panel IP column renders with tooltip
- Old-agent device: dash, no crash
- Table at 1366px: no horizontal scroll regression
