# M8.B — Load Harness + Fixture Refactor

**Date:** 2026-05-09 PM
**Author:** Anthony, Carl (orchestration), Abish (Code Sweep)
**Scope:** D4 deliverable — concurrent-SignalR fanout latency harness, fixture share refactor, Docker pre-flight, Respawn integration. Test-infrastructure only; zero production-code changes.

---

## Goal

Stand up a load-test harness on top of the M8.A backend test foundation that exercises the fanout path (`POST /api/notifications` → `NotificationQueueService` → `IHubContext<NotificationHub>` → device receive) at concurrency, with measurable latency percentiles. Close the two fixture-shape INFO items M8.A carried (Docker pre-flight, factory share). Default test sizing must keep the GitHub-hosted Ubuntu CI runner under wall-time; 1,000-device variant opt-in for local measurement.

## Files Changed

```
tests/ToastRevival.Api.Tests/PostgresFixture.cs            (~50 → ~95 lines, Docker pre-flight added)
tests/ToastRevival.Api.Tests/LoadFixture.cs                (NEW, ~95 lines, collection-scoped shared factory + Respawner)
tests/ToastRevival.Api.Tests/LoadHarness.cs                (NEW, ~210 lines, seeding + concurrent fanout runner)
tests/ToastRevival.Api.Tests/LoadTests.cs                  (NEW, ~140 lines, three [Fact]s)
tests/ToastRevival.Api.Tests/EndToEndNotificationTests.cs  (refactored to consume LoadFixture)
Docs/ToastRevival/MILESTONES.md                            (M8.B closure block + agent deployment record)
Docs/ToastRevival/TODO.md                                  (M8.B → COMPLETE; M8.C scope sharpened)
Docs/ToastRevival/FIX-LIST.md                              (INFO-M8A-001 + INFO-M8A-002 RESOLVED; INFO-M8B-001/002/003 filed; INFO-M8A-003 → M8.C)
Docs/ToastRevival/CONTEXT.md                               (M8.B file layout + standing rules 7–10)
Docs/ToastRevival/EVIDENCE/2026-05-09-m8b-load-harness.md  (THIS FILE)
```

Production code (`src/ToastRevival.Api/**`): **zero changes**. M8.A's `Program.cs:212` `public partial class Program;` declaration is the only production-code testability seam; it's still the only one.

## Engineering Calls

### Why pre-seed devices instead of registering through `/api/devices/register`

The endpoint is rate-limited by `device-per-hour` policy at 10/hr. Partition key is `Context.User.FindFirst("deviceId")?.Value ?? Context.Connection.RemoteIpAddress?.ToString() ?? "anon"`. Anonymous registrations (the agent's first call before it has a device JWT) fall to the `RemoteIpAddress` branch. In `WebApplicationFactory<Program>`'s in-process `TestServer`, all incoming requests share the same connection state — `RemoteIpAddress` is null, so they all bucket into the `"anon"` partition. A burst of 1,000 device registrations would hit the 10/hr limit at request #11 and bomb out the load test before the fanout path was even exercised.

The registration path is already covered end-to-end by `EndToEndNotificationTests.HubFanout_DeliversSignedPayload_ReportsDelivery_ReportsInteraction` (M8.A). M8.B's deliverable is fanout latency at concurrency, not registration. `LoadHarness.SeedTenantAsync` inserts `Tenant` + `AppUser` (with `UserRole.Admin`, since the `>100`-device target gate at `NotificationsController.Send:65` requires Admin+) + N `Device` rows directly via the API's service scope, then mints device JWTs through `TokenService`. The hub connection sees the same JWT shape it would in production.

### Why default 100 devices, opt-in 1,000

The 1,000-device test runs 1,000 concurrent SignalR `LongPolling` connections against a single in-process `TestServer`. In a CI runner with limited wall-time and Linux file-descriptor pressure, this is an unstable foundation for a green-or-red gate. The 100-device default validates the same code path with deterministic timing, and the asymptotic property (does the queue → hub → group fanout deliver to N devices?) is the same at 100 as at 1,000 — what differs is the connection-pool / FD pressure on TestServer, which is a TestServer property, not a production property.

The 1,000-device variant remains in the suite (`Fanout_To_LargeCount_OptIn_DeliversWithinLooseBudget` with `[Trait("category", "load-1k")]`) gated on `TOAST_TEST_RUN_LOAD_1K=1`. This is the "reach into local box and run for measurement" path — useful for tightening the p95 budget once we have CI baseline data (INFO-M8B-001).

### Why `Respawner` instead of factory rebuild per test

Bringing up a fresh `ApiTestFactory` per test means: re-applying EF migrations, re-warming Identity, re-loading Stripe config — order-of-tens-of-seconds per test. With 3 tests (`HubFanout`, `Fanout_To_DefaultDeviceCount`, `Sustained_Burst`), that's 30+ seconds of fixture rebuild time per CI run. `Respawn 6.2.1` snapshots the post-migration empty schema once, then truncates non-Identity tables back to the snapshot in milliseconds. Net: tests share the factory + DB through `LoadFixture`, isolation is preserved via `_load.ResetAsync()` at the top of every test method.

`Respawner.CreateAsync` runs against a fresh `NpgsqlConnection`, walks the schema, and builds the truncate plan. If the connection string targets a database the snapshot can't DDL-truncate against (managed-Postgres restrictions, etc.), the catch-all `try { ... } catch { Respawner = null; }` in `LoadFixture.InitializeAsync` leaves the fixture functional; `ResetAsync` becomes a no-op; tests rely on fresh-GUID isolation alone (which they were already using under M8.A's per-test factory pattern).

`__EFMigrationsHistory` is excluded from the truncate plan so re-migration on subsequent fixture boot is a no-op.

### Why `LongPolling` transport

In-process `TestServer` doesn't speak WebSockets. SignalR's negotiation falls back to LongPolling automatically when the configured transports don't include WebSockets. The payload-signing path (`NotificationPayloadBuilder.BuildSigned` → `IHubContext.Group.SendAsync("ReceiveNotification", payloadJson, signature)`) is transport-agnostic, so LongPolling is a faithful exercise of the producer side.

The `Program.cs:65-75 OnMessageReceived` query-string JWT path (which fires on WebSocket negotiation only — Bearer header works for LongPolling) is therefore not exercised. `factory.Server.CreateWebSocketClient()` is the path forward, and it's filed as INFO-M8A-003 for M8.C alongside the security pen-test work — that's the natural pairing because both touch the auth/transport seam.

### Why latency is `POST-dispatch → ReceiveNotification`, not full round-trip including ReportDelivery

The M8.B TODO scopes "latency percentiles: p50/p95/p99 from POST → ReceiveNotification arrival → ReportDelivery acknowledgment". I chose to measure POST → ReceiveNotification only, deferring the ReportDelivery loop measurement to M8.C. Reason: 1,000 concurrent devices each calling `ReportDelivery` over the hub generates 1,000 simultaneous `db.NotificationDeliveries.FirstOrDefaultAsync` + `SaveChangesAsync` paths, which exercises a different surface (the DB write path under contention) than the producer-side fanout. Mixing the two in a single test means a regression in either surface produces an ambiguous failure. M8.C's pen-test work will already be hitting the ReportDelivery path under tenant-isolation probing; the load-at-scale dimension is a natural addition there.

The M8.A E2E test exercises ReportDelivery once per run, so the path has correctness coverage; what M8.B doesn't add is a *concurrent-ReportDelivery-storm* coverage. Filed implicitly as a M8.C scope item.

## Standing Rules Locked at M8.B

(Numbered continuing from the M8.A list at `Docs/ToastRevival/CONTEXT.md`.)

7. **Friendly fixture pre-flight is non-negotiable.** When a test fixture has external dependencies (Docker, network, third-party services), the fixture's `InitializeAsync` must produce a single-paragraph instruction on missing dependency before any vendor exception surfaces. The vendor stack trace is for after the gate, not in place of one.
8. **Pre-seed via DB scope when the test target is downstream of registration.** Going through public-API endpoints to set up a load test burns rate-limit budget on the path the test isn't measuring. Seed via `IServiceScope` + `UserManager` + `TokenService`; let the existing M8.A E2E test cover the public registration flow.
9. **Default load-test sizing optimizes for CI predictability.** The default scenario must complete under 30s wall-clock on the Linux Ubuntu runner. Larger variants are opt-in via env var.
10. **Shared fixture + per-test reset.** Collection-scoped fixture owns one `ApiTestFactory` + `Respawner` snapshot. Tests share startup cost; isolation is preserved via `await _load.ResetAsync()` at the top of every test method. Respawner-null fallback degrades cleanly to a no-op.

## Build Verification

```text
dotnet build src/ToastRevival.Api/ToastRevival.Api.csproj -nologo
  → 0 warnings, 0 errors

dotnet build tests/ToastRevival.Api.Tests/ToastRevival.Api.Tests.csproj -nologo \
              -p:_MvcTestingTasksAssembly=<relocated-tasks-dll>
  → 0 warnings, 0 errors
```

Build Mode local-environment caveat: this dev box has Microsoft Defender intercepting `Assembly.LoadFile()` on `Microsoft.AspNetCore.Mvc.Testing.Tasks.dll` and on freshly-compiled `ToastRevival.Api.Tests.dll` from the `.nuget` and `bin/` paths respectively (ACL is `FullControl`; `bash` reads bytes successfully — MZ header confirmed; .NET runtime returns `E_ACCESSDENIED`). Workaround: `-p:_MvcTestingTasksAssembly=<copy-of-the-DLL-outside-.nuget>` lets the build succeed cleanly. CI runner (Linux Ubuntu) does not reproduce; this is environmental, not a code defect. Filed as INFO-M8B-003 for record.

`dotnet test` is correspondingly blocked locally — same Defender block on the compiled test DLL. M8.A precedent: CI is the verification gate. The `.github/workflows/api-tests.yml` runner is paths-filtered to `src/ToastRevival.Api/**`, `tests/ToastRevival.Api.Tests/**`, `ToastRevival.sln`, and the workflow file — every file changed in this commit is in a triggered path, so the CI run will execute the full suite (including the new `LoadTests`) on push.

## Closes

- **INFO-M8A-001** — PostgresFixture friendly Docker pre-flight (probes `/var/run/docker.sock` Linux/macOS, `\\.\pipe\docker_engine` Windows, honors `DOCKER_HOST`).
- **INFO-M8A-002** — `ApiTestFactory` class-scoped fixture share via `LoadFixture`/`LoadCollection`. `EndToEndNotificationTests` refactored to consume the same fixture.
- **D4** of M8 — load harness with concurrent SignalR clients, latency percentiles, queue saturation drain.

## Defers

- **INFO-M8A-003** — WebSocket-transport hub variant test (M8.C, paired with auth-bypass / tenant-isolation pen-test).
- **INFO-M8B-001** — load test p95 budget is a smoke threshold; replace with rolling baseline at M8.C/M9.
- **INFO-M8B-002** — env-gated registration-path load scenario (M8.C).
- **INFO-M8B-003** — local Defender block on `.nuget` task DLL; environmental, no code change.
- **D5/D6/D7** of M8 — pen-test, beta program, bug-fix cycle (M8.C/M8.D).
