# Toast Notification - Immediate Actions

## Current Reality

Project status: **M0A through M8.C ALL COMPLETE. M8.C COMPLETE 2026-05-09 — Security pen-test + WebSocket variant + reg-path load shipped.** Four new test files: `SecurityHarness.cs` (seed + JWT forge), `SecurityTests.cs` (20 [Fact] probes across tenant-isolation, auth-bypass, content-injection, privilege-escalation), `WebSocketTransportTests.cs` (closes INFO-M8A-003 via `factory.Server.CreateWebSocketClient` + `SkipNegotiation=true`), `RegistrationLoadTests.cs` (closes INFO-M8B-002 via env-gated `TOAST_TEST_RUN_REGISTRATION_LOAD=1`, 8 concurrent registrations against fresh factory). **FIX-M8C-001 caught in-milestone**: AuditController.List + Export were not scoping audit log queries by caller's tenantId, leaking cross-tenant audit rows to any tenant admin. Patched in same commit — both endpoints now filter by `User.tenantId` claim before timestamp range. Pre-commit cleanup commit (`8ff8e59`) consolidated 16 stray screenshot PNGs from prior milestones into `Docs/ToastRevival/EVIDENCE/screenshots-2026-05-09/` and gitignored `.playwright-mcp/`.

M8.B (load harness + fixture refactor + Docker pre-flight + EndToEndNotificationTests refactor to shared LoadFixture) and M8.A (backend test foundation, xUnit + WebApplicationFactory + Testcontainers PostgreSQL + first E2E scenario) both closed earlier same day.

M7.D earlier same day shipped SEO + LLM-discovery layer (`llms.txt`, `robots.txt`, `sitemap.xml`, per-page JSON-LD via the new `useSeo` hook, favicon set, 1200×630 OG card). M7.A/B/C earlier shipped marketing site rebuilt corporate-grade + public docs hub. Production live at https://toastnotification.com — TOASTWEB1 (54.82.103.160) + TOASTDATA1 (172.26.3.164 private). HTTPS via Let's Encrypt.

**Next milestone: M8.D** — beta program coordination (D6: invite 3-5 MSP partners; D7: bug fix cycle from beta feedback). Keith-driven outreach. D1/D2/D3 (Store / MSI / Intune Windows-side E2E) wait on Keith's lab + signed-package flight.

**Codex handoff tracks closed 2026-05-09:**
- Pricing v2 shipped: single Standard plan, $0.22/device/month, 100-device monthly floor ($22), 14-day Stripe trial, no Free/Pro/Enterprise plan picker.
- PlatformAdmin shipped: `AppUser.IsPlatformAdmin`, JWT `platformAdmin` claim, `PlatformAdmin` policy, `/api/system/*` cross-tenant endpoints, and production promotion for `keith@colosolutions.com` to `Role=SuperAdmin` + `IsPlatformAdmin=true`.
- Admin dashboard UI redesign shipped: authenticated routes now use a VMware/Horizon-style dark enterprise rail with restrained light workspace, blue accent, no purple theme, and corrected Platform Admin/Tenant Owner labels.
- Production verified: https://toastnotification.com/login 200, emitted script `/static/index-DTLiw4aQ.js` 200, `toast-api` active, bad login returns 401, smoke tenants cleaned up.

Next decision: configure the real Stripe per-device price in production as `Stripe__PerDevicePriceId` after the Stripe dashboard price is created. Until then checkout degrades to a 503 instead of creating an invalid subscription.

## Keith

- [x] Renew code signing certificate (Sectigo OV, NotAfter 2027-04-15).
- [x] Confirm signing requires a hardware token (Thales).
- [x] Sign first MSI (ToastRevival.Agent-0.1.0.0.msi).
- [ ] Re-sign the rebranded MSI when needed (`artifacts/installer/ToastNotification.Agent-0.2.0.0.msi`).
- [x] Sign the M0 D2 MSIX (`artifacts/installer/msix/ToastNotification.Agent-0.2.0.1.msix`) - DONE 2026-05-07 via `signtool.exe`.
- [x] Validate install of signed M0 D2 MSIX on Win11 lab. 0.2.0.1 installed but did not fire toasts (FIX-MSIX-004); 0.2.0.3 patched + signed + installed 2026-05-08, visible toast fires, NotificationInvoked routes cleanly.
- [x] Confirm Microsoft Partner Center access (Keith signed in 2026-05-07). Verifying app ID `9PFD6004DVTN` is reachable from this account is M0 D5 work.
- [x] Accept the updated App Developer Agreement in Partner Center if prompted (handled when M0 D5 was flighted to the live listing).
- [x] Confirm domain/DNS control for `toastnotification.com` — DNS pointed 2026-05-09. Live at https://toastnotification.com.
- [ ] Confirm Stripe account status — needed for M6 billing integration.
- [ ] Terminate AWS Windows VM (52.21.249.120) — CI moved to GitHub Actions, VM has no remaining purpose.

## Engineering - M0A (closed)

- [x] Install .NET SDK on the development machine.
- [x] Install/verify Windows App SDK (1.7.250310001 via NuGet), `<EnableMsixTooling>true</EnableMsixTooling>` set.
- [x] Install WiX 5.0.2 dotnet global tool.
- [x] Create repository baseline and push to GitHub.
- [x] Build the minimal Windows agent (sends one hardcoded local app notification).
- [x] Produce local Release publish artifacts (framework-dependent + self-contained).
- [x] Build a rich local notification spike with hero image, logo override, action buttons, audio. Six Diana templates covered.
- [x] Package the agent locally as a per-machine MSI (Startup-folder shortcut + Start-menu shortcut).
- [x] Sign the package with the renewed certificate (Thales token + Sectigo OV cert).
- [x] Test install / run / toast behavior on a clean Windows 11 lab machine.
- [x] Rebrand all user-facing strings to "Toast Notification" (commit 56b0adb).
- [x] Record evidence in `Docs/ToastRevival/EVIDENCE` (4 entries this milestone).

## Engineering - M0 D2 (CLOSED 2026-05-08)

- [x] **M0 D2 build:** MSIX package built via WinAppSDK 1.7 SingleProject path. First build (0.2.0.0) failed signing with 0x80091005 (manifest Publisher missing O=Toast2IT, LLC). Manifest corrected, rebuilt as 0.2.0.1.
- [x] **M0 D2 sign:** Signed 2026-05-07 via `signtool.exe`. `scripts/sign-msix.ps1` codifies the working flow.
- [x] **M0 D2 install:** 0.2.0.1 installed cleanly via Add-AppxPackage but did not fire toasts.
- [x] **M0 D2 visible-toast confirmation:** Resolved via FIX-MSIX-004 (0.2.0.3 commit `6e3495c`). Single toast fires from packaged Start menu launch, button click routes through `NotificationInvoked` handler with expected argument payload.
- [x] **Push Codex's runner-setup evidence note:** Committed 2026-05-08 (commit `3c702fc`).

## Engineering - M0 D3 (CLOSED 2026-05-08)

- [x] M0 D3 build: WiX installer registers `\Toast2IT\ToastNotificationAgentLogon` Scheduled Task via deferred schtasks.exe /Create /XML /F custom action; uninstall via /Delete /F (Return=ignore for idempotency). StartupShortcut component dropped. Built `ToastNotification.Agent-0.3.0.0.msi` 50.61 MB.
- [x] **Keith**: signed `ToastNotification.Agent-0.3.0.0.msi` via Thales+Sectigo OV.
- [x] **Keith**: installed signed MSI on Win11 lab cleanly.
- [x] **Keith**: verified task created via Get-ScheduledTask — State=Ready, MSFT_TaskLogonTrigger, MSFT_TaskExecAction.
- [x] **Keith**: confirmed alert toast fires at next user logon (visible Critical-scenario banner with hero/logo/Acknowledge/Report buttons).
- [x] Captured `EVIDENCE/2026-05-08-m0-d3-task-fires-at-logon.md` with Get-ScheduledTask output and toast description.
- [ ] (Deferred to M0 D4) Uninstall idempotency check + 0.3.x → 0.3.y major-upgrade race validation. Roll into the GPO matrix testing.

## Engineering - M0 D4 (in progress 2026-05-08)

- [x] Apply FIX-MSIX-002: `Package.appxmanifest` MinVersion 17763→19041, `TargetPlatformMinVersion` updated, manifest version 0.2.0.3→0.2.1.0.
- [x] Build `ToastNotification.Agent-0.3.1.0.msi` for major-upgrade test (UpgradeCode matches 0.3.0.0).
- [x] Write `scripts/verify-d4-matrix.ps1` — pass/fail script for each test checkpoint.
- [x] GPO standing rules documented in `CONTEXT.md` (notification policy, AppLocker, Intune, OS floor).
- [x] Evidence shells: `EVIDENCE/2026-05-08-m0-d4-fix-msix-002.md`, `EVIDENCE/2026-05-08-m0-d4-matrix-results.md`.
- [x] **Keith**: signed + installed 0.3.1.0.msi on Win11 lab. Task created, toast fires, second user toast confirmed, uninstall clean.
- [x] Mark M0 D4 COMPLETE in MILESTONES.md. **CLOSED 2026-05-08.**

## Engineering - M0 next deliverables

### M0 D5 — **CLOSED 2026-05-09** (Store listing live)
- [x] FIX-MSIX-001: `-p:TargetPlatformVersion=10.0.22621.0` added to `build-msix.ps1` command line. (csproj PropertyGroup approach does not work — TFM late-import override; see CONTEXT.md rule #4.)
- [x] Add `uap5:StartupTask` extension to manifest for Store/MSIX logon-launch parity.
- [x] Build MSIX 0.2.1.0: `.\scripts\build-msix.ps1 -Version 0.2.1.0 -SkipAssetGeneration`. Verified: MinVersion=10.0.19041.0, MaxVersionTested=10.0.22621.0, all 3 extensions present. 63.82 MB unsigned artifact at `artifacts/installer/msix/ToastNotification.Agent-0.2.1.0.msix`.
- [x] **Keith**: Sign `ToastNotification.Agent-0.2.1.0.msix` via `scripts/sign-msix.ps1`. **DONE.**
- [x] **Keith**: Accept updated App Developer Agreement in Partner Center. **DONE.**
- [x] **Keith**: Flight signed MSIX to Partner Center listing **9PFD6004DVTN**. **DONE — listing live at https://apps.microsoft.com/detail/9PFD6004DVTN.**
- [ ] **Diana** (deferred to M9 polish): Deliver curated tile assets (Square44x44, Square150x150, Wide310x150, StoreLogo) before any expansion beyond the current markets. Not blocking the listing — Store accepted whatever assets were submitted at flight time.

### M0 D6 — DEFERRED
Deployment documentation deferred. Everything worth capturing is in CONTEXT.md already. Fold into M1 prep or skip — not on the critical path.

---

## M1 — Backend API — COMPLETE 2026-05-08

All 8 deliverables shipped. `src/ToastRevival.Api` — ASP.NET Core 8 / EF Core 8 / Npgsql / ASP.NET Identity / SignalR. Clean build, migration generated. INFO-M1-001 to INFO-M1-006 in FIX-LIST.md.

## M2 — Windows Agent Full Implementation (sliced 2026-05-09)

### M2.A — COMPLETE 2026-05-09
- [x] D1: SignalR client — connect to `/hubs/notifications`, auto-reconnect with `WithAutomaticReconnect` `[0,2,5,10,30]`s. `Microsoft.AspNetCore.SignalR.Client 8.0.15`.
- [x] D2: Toast rendering from backend payload — `ToastTemplateBuilder.BuildFromPayload` (extends, doesn't fork). Title, body, hero, logo, scenario, audio, action buttons all wired; click args carry `source=hub;notificationId=<guid>;action=<string>`.
- [x] D3: Device registration flow — `RegistrationService` posts to `/api/devices/register`, `ConfigStore` persists to `%LOCALAPPDATA%\Toast2IT\Toast Notification\config.json` via atomic temp+Move; bootstrap from env vars or `bootstrap.json` next to the exe. 30-min REST `/api/devices/ping` heartbeat.
- [x] D4: Payload HMAC verification — `Tenant.SigningKey` (32-byte random base64), server signs JSON byte sequence in `NotificationQueueService.BuildSignedPayload`, sends `(payloadJson, signature)` as separate SignalR args; agent verifies via `HmacVerifier.Verify` with `CryptographicOperations.FixedTimeEquals` constant-time compare.
- [x] D5: Interaction tracking — `ReportDelivery` after render via hub, `ReportInteraction` on `NotificationInvoked` via hub, plus REST `POST /api/notifications/{id}/interactions` for activation-handler exit path.
- [x] INFO-D5-001 (session-local mutex `Local\Toast2IT.ToastNotification.PrimaryWorker` — FIX-M2A-001 patched `Global\` → `Local\` pre-commit).
- [x] INFO-MSIX-004-D (activation-handler short-circuit before mutex/SignalR; `ActivationMode` runs Register() → wait for `NotificationInvoked` → POST REST → exit clean).

### M2.B — COMPLETE 2026-05-09
- [x] Backend: `GET /api/notifications/pending?since=<DateTime?>` device-JWT-authenticated, `device-per-hour` rate limit, returns `PendingNotificationItem[]` of (NotificationId, PayloadJson, Signature, CreatedAt) for the device's Pending deliveries; cap 100 per call.
- [x] Backend: `NotificationQueueService.RecoverOrphansAsync` runs once at `ExecuteAsync` startup. Notifications stuck in `Sending` past 5 minutes → Failed; pending deliveries STAY pending so catch-up can still deliver. Carl's overrule on the original FIX-LIST plan.
- [x] Backend: shared `NotificationPayloadBuilder.BuildSigned` helper — hub fanout + catch-up endpoint sign byte-identical UTF-8 sequences via the same code path.
- [x] Agent: on `_hub.Reconnected` and once after cold `StartAsync`, `RunCatchupAsync` GETs `/pending`, verifies + de-dups + renders + ReportDeliverys each item.
- [x] Agent: `MemoryCache<Guid,byte>` 1-hour sliding dedup. Short-circuits BOTH render and ReportDelivery; entry only set after `Show()` succeeds (render failures don't poison the cache).
- [x] FIX-M2B-001 patched pre-commit: agent `_lastCatchupSince` nullable + omit-on-first-call. Original `=DateTime.UtcNow` at ctor time would have excluded every pre-existing Pending delivery on first run.
- [x] Build clean (0 warn / 0 err); MSIX smoke check clean. No EF migration required.

### M2.C — COMPLETE 2026-05-08 (commit ecf79ce)
- [x] D7: System tray icon — WinForms NotifyIcon on STA thread; 5 states; Diana spec colors; 700ms amber pulse; context menu wired.
- [x] D9: WiX MSI properties CLIENTID + SERVERURL → bootstrap.json via SetupMode; WriteBootstrapJson/DeleteBootstrapJson CAs; version 0.4.0.0.
- [ ] D9: Silent install/uninstall verification + MSIX→MSI upgrade-path smoke check — **Keith to verify with lab machine on next MSI build**.

### M2.D — Auto-update (Velopack) — COMPLETE 2026-05-08
- [x] D8: Velopack 0.0.1298 integrated. UpdateService (registry toggle, TrySelfRedirect, 24h loop). Tray "Update Available" menu item. scripts/build-release.ps1. Feed URL placeholder (production setup at M9).

## Engineering - M2 follow-up (logged during M0 D2 — INFO-MSIX-004-A/B/C only remain)

- [ ] **INFO-MSIX-004-A/B/C** (DiagLog hygiene): Before any production launch, gate DiagLog behind a `--diag` flag or add log rotation. Currently writes append-only with no size cap and silently swallows all I/O exceptions (intentional for diagnostics phase, not for steady state). M2.D or M3 candidate.

## M7 — Marketing Site (sliced 2026-05-09)

### M7.A — COMPLETE 2026-05-09
- [x] DESIGN-SPEC.md Marketing Site section expanded from stub to full ~480-line spec.
- [x] Four onboarding SVG components (`OnboardingBell`/`OnboardingTemplate`/`OnboardingPackage`/`OnboardingLaunch`) at `src/ToastRevival.Dashboard/src/icons/onboarding/index.tsx`.
- [x] `Onboarding.tsx` welcome step swapped from 🔔📋📦🚀 emojis to the four SVG components.
- [x] INFO-M6-004 closed.
- [x] Pre-commit fix (INFO-M7A-001): `*.pem` / `*.key` added to `.gitignore`.
- [x] Code Sweep SHIP WITH NOTES (INFO-M7A-002 deferred to M7.D, INFO-M7A-003 deferred to M7.B, INFO-M7A-004 standing vigilance).

### M7.B Corporate Edition — COMPLETE 2026-05-09
- [x] `MarketingLayout` + `MarketingHeader` + `MarketingFooter` shipped (M7.A scaffolding retained, footer slimmed to 3-col).
- [x] Home page rebuilt corporate-grade — hero (real product screenshot), problem/solution, capability matrix, security architecture, how-it-works, single-plan pricing summary, 14-day-trial CTA.
- [x] Pricing page rebuilt as single-plan layout ($0.22/device + 100-device floor + 14-day trial), indicative-cost table, 6-card inclusion grid, 8-question FAQ.
- [x] HowWeBuiltIt + DocsComingSoon DELETED per Keith's standing rule (no DocPro/AI-built story on public surface; no placeholder pages).
- [x] Marketing routes lazy-loaded in App.tsx with RootIndex toggle (anon → Home, auth → /dashboard).
- [x] INFO-M7A-003 resolved by removing the lifetime-count line entirely — no fake numbers, no public unauth endpoint added.
- [x] Real product screenshot captured from live composer (smoke-test tenant) at `public/screenshots/composer-hero.png`.
- [x] Build clean, deploy clean, post-deploy curl + Playwright verified.

### M7.C — COMPLETE 2026-05-09
- [x] Docs hub (`/docs`) with two-column layout (240px sticky sidebar at top:80px, mobile collapsible nav).
- [x] Getting Started doc (`/docs/getting-started`) — 4 steps register tenant → first notification.
- [x] Deploy guides — `/docs/deploy/store`, `/docs/deploy/intune`, `/docs/deploy/rmm`.
- [x] API reference (`/docs/api`) — auth, devices, notifications, webhooks, rate limits.
- [x] `DocsLayout` + `CodeBlock` + `Callout` components shipped under `components/marketing/`.
- [x] MarketingHeader Docs link + MarketingFooter Resources column. Slim grid widened 3→4 cols.
- [x] Live at https://toastnotification.com/docs. Code Sweep SHIP WITH NOTES (INFO-M7C-001/002/003/005, FIX-M7C-001 patched). Diana sign-off APPROVED. See `EVIDENCE/2026-05-09-m7c-docs-hub.md`.

### M7.D — SEO + Deploy (after M7.C)
- [ ] `llms.txt` at `https://toastnotification.com/llms.txt` (use M7.A draft as starting point; fill in real numbers).
- [ ] JSON-LD per page (SoftwareApplication on home, Product/AggregateOffer on pricing, TechArticle/BreadcrumbList on docs, Article on how-we-built-it).
- [ ] `/sitemap.xml` and `/robots.txt`.
- [ ] OG/Twitter card image (1200×630) — Keith provides product screenshot or Diana shoots one.
- [ ] Favicon set (32/64/192/512 SVG + ICO).
- [ ] Resolve INFO-M7A-002: fix `DEPLOY.md:451` SSH key path (`Docs/ToastRevival/Assets/...` → `Docs/Assets/...`).
- [ ] Deploy to toastnotification.com (same Lightsail box, same nginx, no DNS change).
- [ ] Lighthouse SEO 100, Accessibility ≥ 95, Performance ≥ 90 mobile.

## M8 — Integration Testing & Beta (sliced 2026-05-09)

### M8.A — COMPLETE 2026-05-09
- [x] `tests/ToastRevival.Api.Tests` xUnit project + Microsoft.AspNetCore.Mvc.Testing 8.0.15 + Testcontainers.PostgreSql 4.0.0 + SignalR.Client 8.0.15.
- [x] `PostgresFixture` (Testcontainers + `TOAST_TEST_CONNECTION_STRING` env-var fallback for CI).
- [x] `ApiTestFactory` (`WebApplicationFactory<Program>` override, layered `appsettings.Test.json`, runtime connection-string rewrite).
- [x] `PayloadVerifier` agent-side HMAC reproduction.
- [x] First E2E test `HubFanout_DeliversSignedPayload_ReportsDelivery_ReportsInteraction` — 8 assertions covering M2.A/M2.B critical path.
- [x] `Program.cs:212` adds `public partial class Program;` for `WebApplicationFactory<Program>`.
- [x] `ToastRevival.sln` updated with `tests` folder + Api.Tests project + nesting.
- [x] `.github/workflows/api-tests.yml` Ubuntu runner with Postgres 16 service container, paths-filtered, uploads `.trx` artifact.
- [x] Closes `INFO-M1-004` (zero automated tests since M1).
- [x] Code Sweep SHIP WITH NOTES (5 INFO items: M8A-001 Docker pre-flight, M8A-002 factory share, M8A-003 WebSocket variant, M8A-004 sln BOM, M8A-005 PayloadVerifier mirroring).
- [x] Build clean (0 warnings, 0 errors solution-wide).
- [x] EVIDENCE captured at `EVIDENCE/2026-05-09-m8a-test-foundation.md`.

### M8.B — COMPLETE 2026-05-09
- [x] D4: load harness with concurrent SignalR clients, single-tenant fanout latency percentiles (p50/p95/p99), HMAC verify, queue saturation drain. Default 100-device CI-fast scenario; opt-in 1,000-device variant via `TOAST_TEST_RUN_LOAD_1K=1`; sustained-burst scenario over 5×30 deliveries.
- [x] `LoadFixture` (collection-scoped shared `ApiTestFactory` + `Respawner` snapshot) — closes INFO-M8A-002.
- [x] `PostgresFixture` Docker pre-flight (Linux socket / Windows pipe / DOCKER_HOST honored) — closes INFO-M8A-001.
- [x] `Respawn 6.2.1` integration for per-test cleanup; `_load.ResetAsync()` no-op-safe when Respawner can't init.
- [x] `EndToEndNotificationTests` refactored to consume the shared `LoadFixture` — both test classes amortize startup.
- [x] Latency capture from POST `/api/notifications` dispatch to per-device `ReceiveNotification` arrival; ReportDelivery loop at scale deferred to M8.C.

### M8.C — Security pen-test + transport variant — **COMPLETE 2026-05-09**
- [x] D5: tenant-isolation probes — 6 [Fact]s: device list/byId, notification send target resolution, pending catch-up scope, audit list (FIX-M8C-001 regression test), hub group event isolation.
- [x] D5: auth bypass attempts — 5 [Fact]s: expired JWT, wrong-key-signed forged platformAdmin claim, missing-deviceId device JWT, user JWT on device-only endpoint, missing-MFA broadcast.
- [x] D5: content injection — 4 [Fact]s: XSS-in-body persists raw, oversized title rejected by `[MaxLength(64)]`, unicode boundary round-trips clean, `</script>` body delivers intact via hub fanout.
- [x] D5: privilege escalation — 5 [Fact]s: technician invite returns 403, admin own-role returns 400, admin cross-tenant user returns 404, technician >100-device broadcast returns 403, admin without platformAdmin claim returns 403 on SystemController.
- [x] WebSocket-transport hub variant test — closes INFO-M8A-003 via `factory.Server.CreateWebSocketClient()` + `SkipNegotiation=true` exercising the `Program.cs:65-75 OnMessageReceived` query-string JWT path.
- [x] Env-gated registration-path load scenario — closes INFO-M8B-002 via `TOAST_TEST_RUN_REGISTRATION_LOAD=1`, 8 concurrent registrations on fresh factory with ConsumedCount + DeviceId-uniqueness assertions.
- [x] FIX-M8C-001 caught in-milestone — AuditController.List + Export now scope by caller's tenantId claim, closing cross-tenant audit log leak.

### M8.D — Beta program (Keith-driven)
- [ ] D6: invite 3–5 MSP partners — Keith identifies and contacts.
- [ ] D7: bug fix cycle from beta feedback — triage queue, prioritize, ship via paths-filtered CI.

### M8 Windows-side E2E (waiting on Keith's lab)
- [ ] D1: Store install → register → receive → interact → DB delivery row verified.
- [ ] D2: MSI/RMM install → same flow.
- [ ] D3: Intune LOB deploy → same flow.

## Deferred (later milestones)

- Curated per-template hero/logo imagery (Diana M4 deliverable, not blocking).
- Tray icon production SVG assets (M9 GA — five states per Diana's spec).
- End-to-end testing across Store/RMM/Intune (M8).
- Beta program with 3-5 MSP partners (M8).
- Public launch + Store expansion (M9).
