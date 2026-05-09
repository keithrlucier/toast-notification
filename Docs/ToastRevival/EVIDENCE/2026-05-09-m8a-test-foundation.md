# M8.A — Backend Integration Test Foundation (2026-05-09)

## Scope

First slice of M8 (Integration Testing & Beta). Establishes the xUnit + WebApplicationFactory + PostgreSQL test harness for the API and ships the first end-to-end scenario. Closes carry-forward INFO-M1-004 (zero automated tests since M1).

## What Shipped

### New test project

- `tests/ToastRevival.Api.Tests/ToastRevival.Api.Tests.csproj` — xUnit 2.9.2 + xunit.runner.visualstudio 2.8.2 + Microsoft.NET.Test.Sdk 17.11.1 + Microsoft.AspNetCore.Mvc.Testing 8.0.15 + Microsoft.AspNetCore.SignalR.Client 8.0.15 + Testcontainers.PostgreSql 4.0.0 + Respawn 6.2.1. ProjectReference to `src/ToastRevival.Api`.
- `tests/ToastRevival.Api.Tests/appsettings.Test.json` — dummy Stripe values, test JWT signing key, suppressed Microsoft.AspNetCore log noise. Connection string is overridden at runtime by `ApiTestFactory`.
- `tests/ToastRevival.Api.Tests/PostgresFixture.cs` — `IAsyncLifetime` collection-scoped fixture that spins up `postgres:16-alpine` via Testcontainers. Honors `TOAST_TEST_CONNECTION_STRING` env var as a fallback so the suite can run on CI service containers or any pre-provisioned Postgres.
- `tests/ToastRevival.Api.Tests/ApiTestFactory.cs` — `WebApplicationFactory<Program>` override. Forces the `Production` environment (CORS uses `AllowedOrigins`, no Swagger), layers `appsettings.Test.json`, then overrides `ConnectionStrings:DefaultConnection` to point at the fixture-managed Postgres. The API's existing `db.Database.Migrate()` startup hook runs against the test DB automatically.
- `tests/ToastRevival.Api.Tests/PayloadVerifier.cs` — agent-side HMAC reproduction. Uses `HMACSHA256` + `CryptographicOperations.FixedTimeEquals` — the same primitives as the production `NotificationPayloadBuilder.BuildSigned` / `HmacVerifier.Verify` pair. Lives in tests because the Windows-only `ToastRevival.Agent` project can't be referenced from a netstandard test assembly.
- `tests/ToastRevival.Api.Tests/EndToEndNotificationTests.cs` — first E2E scenario.

### First E2E scenario: `HubFanout_DeliversSignedPayload_ReportsDelivery_ReportsInteraction`

Eight assertions cover the full M2.A/M2.B critical path:

1. `POST /api/auth/register` creates tenant + admin user + tenant signing key. Returns user JWT.
2. `POST /api/devices/register` (unauthenticated) issues a device JWT and surfaces the tenant signing key to the agent.
3. `HubConnectionBuilder` opens a SignalR connection to `/hubs/notifications` over LongPolling transport (TestServer doesn't speak WebSockets — the payload-signing and hub-method paths are transport-agnostic, so this exercises the full agent loop). Device JWT supplied via `AccessTokenProvider`.
4. `POST /api/notifications` (admin user JWT, `TargetType.Device`, `TargetIds = [deviceId]`) returns 202 Accepted. `Notification` row + single `NotificationDelivery` row persisted with `Status = Pending`. Hosted `NotificationQueueService` picks up.
5. Background queue fans out via `IHubContext<NotificationHub>.Clients.Group($"device-{id}").SendAsync("ReceiveNotification", payloadJson, signature)`. Test client's `ReceiveNotification` handler captures the tuple via `TaskCompletionSource`.
6. `PayloadVerifier.Verify(payloadJson, signature, signingKey)` returns true — server-side HMAC matches client-side reproduction.
7. `connection.SendAsync("ReportDelivery", notificationId)` flips the delivery row to `Status = Delivered`, sets `DeliveredAt`. Test polls the DB at 100ms intervals up to 10 s.
8. `connection.SendAsync("ReportInteraction", notificationId, "acknowledge")` flips the row to `Status = Clicked`, sets `InteractedAt`, records `Action = "acknowledge"`. Parent `Notification.Status = Sent` and `CompletedAt` populated.

### Production code change

- `src/ToastRevival.Api/Program.cs:212` — appended four lines exposing `public partial class Program;` so `WebApplicationFactory<Program>` can resolve the auto-generated entry-point class. Zero behavior change — the compiled IL of `Program` is unchanged. No production callers affected.

### CI workflow

- `.github/workflows/api-tests.yml` — Ubuntu runner with `postgres:16-alpine` service container. Triggered on push/PR/manual dispatch when paths touching `src/ToastRevival.Api/**`, `tests/ToastRevival.Api.Tests/**`, `ToastRevival.sln`, or the workflow file change. Sets `TOAST_TEST_CONNECTION_STRING` env var so `PostgresFixture` skips Testcontainers and uses the GitHub-managed Postgres directly. Uploads `.trx` results as artifact (14 day retention).

### Solution registration

- `ToastRevival.sln` — added `tests` solution folder + `ToastRevival.Api.Tests` project entry + nesting. New project IDs: `{B5E1B3DC-8F3A-4E1B-9C2D-3F4A5B6C7D8E}` (folder), `{C7F2D4ED-90A1-4F2C-AD3E-4F5B6C7D8E9F}` (project).

## Build Verification

```text
dotnet restore ToastRevival.sln  → 0 errors
dotnet build ToastRevival.sln    → 0 warnings, 0 errors (Agent + Api + Api.Tests)
```

Local-machine `dotnet test` blocked: dev box has neither Docker (Testcontainers) nor a local Postgres on `localhost:5432` (probe negative). The CI workflow closes that gap — first push to `main` will run the test against the Postgres service container.

## Code Sweep

`SHIP WITH NOTES`. Five INFO items filed, zero HOLD. See `FIX-LIST.md`:

- INFO-M8A-001 — `PostgresFixture` Docker pre-flight check (M8.B polish)
- INFO-M8A-002 — `ApiTestFactory` per-test scope → IClassFixture share when test count grows
- INFO-M8A-003 — WebSocket-transport hub variant test (covers query-string JWT path)
- INFO-M8A-004 — Solution file BOM dropped on rewrite (no observed impact)
- INFO-M8A-005 — `PayloadVerifier` mirrors production HMAC logic (drift risk minimal)

INFO-M1-004 (zero automated tests, carry-forward since M1 shipped 2026-05-08) — **CLOSED** by this milestone.

## Cross-AI Handoff State

Working tree at session start was clean of source-code modifications — first time in three milestones. Codex's last `CODEX-HANDOFF.md` block was the Track A close (`581a4ee` Stripe billing-config UX). No incoming Codex note this session. Untracked files at session start (17 evidence/screenshot artifacts at repo root + `.playwright-mcp/`) were left untouched — `tracka-*.png` belong to Codex's Track A record, the rest are M7.B/C/D screenshots that need cleanup separately.

## What This Unblocks

- **M8.B**: Load testing harness can reuse `ApiTestFactory` + `PostgresFixture` to drive 1,000 concurrent agents against the same in-process API, measuring fanout latency. Respawn (already in csproj) handles per-test cleanup.
- **M8.C**: Security pen-test surface scripts tenant-isolation probes, auth-bypass attempts, content-injection vectors via the same test harness — every probe runs as a regular xUnit fact, evidence is the `.trx` output.
- **M8.D**: Beta program coordination is Keith-driven; the test suite gives us a regression net for whatever bug fixes the beta surfaces.
- **D1 / D2 / D3** Windows-side E2E (Store / MSI / Intune) remain Keith-lab work. M8.A's server-side equivalent proves the API/SignalR/DB loop those lab tests will exercise once the signed packages are flighted.

## Standing Rules Locked in M8.A

1. **Production code minimally extended for testability** — `public partial class Program;` is the only acceptable production-side modification. Tests run on the public HTTP/SignalR surface; never reach internal types via `InternalsVisibleTo`.
2. **Test fixture is hermetic** — Testcontainers per collection by default, env-var override for CI / Docker-less dev boxes. Never points at a shared dev DB or production.
3. **Payload verification mirrors production primitives** — HMAC-SHA256 + `FixedTimeEquals` only. If production format changes, both paths update at once via `NotificationPayloadBuilder` — `PayloadVerifier` only checks the HMAC algorithm and key encoding.
4. **CI workflow is paths-filtered** — `api-tests.yml` triggers on api/sln/test changes only; frontend-only PRs do not pay the Postgres-spin-up cost.
5. **Banned-terms grep is part of test-code review too** — internal test code may reference milestone codes in doc-comments, but never customer-facing surfaces. Grep before commit.
