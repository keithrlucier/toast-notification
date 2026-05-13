# ToastRevival - Test Log

## 2026-05-12 (M11.D1 — Docker Compose infrastructure)

### Build Checks

- `dotnet build src\ToastRevival.Api\ToastRevival.Api.csproj`: **0 warnings, 0 errors** (no API code changed — infrastructure files only).
- `npm run build` from `src\ToastRevival.Dashboard`: **not re-run** (no dashboard code changed).
- Docker build: **not yet verified locally** — Docker Desktop not available in the dev session context. Local smoke test deferred to D2 verification.

### Scope Verified (Abish code sweep, 2026-05-12)

- `src/ToastRevival.Api/Dockerfile`: multi-stage build, API project only (Agent excluded — Windows-only). `ASPNETCORE_URLS=http://+:8080`. `/app/wwwroot/assets` declared as VOLUME.
- `src/ToastRevival.Dashboard/Dockerfile`: `node:20-alpine` build stage → `nginx:alpine` runtime. `npm run build` includes `prerender-seo.mjs` (file I/O only, no network calls — safe in Docker build context).
- `src/ToastRevival.Dashboard/nginx.conf`: `/api/` proxy with forwarded headers, `/hubs/` WebSocket upgrade, `/` SPA fallback (`try_files $uri $uri/ /index.html`).
- `docker-compose.yml`: postgres:16-alpine with healthcheck; API `depends_on: condition: service_healthy`; Stripe keys empty (billing disabled); `AllowedOrigins__0` wired; named volumes `db-data` + `assets`.
- `.env.example`: no real secrets — all placeholders. `JWT_KEY` guard comment (32+ chars) present. `TURNSTILE_REQUIRED=false` default.
- `README-SELF-HOST.md`: no codename "ToastRevival", no server IPs, no internal docs referenced. Product name "Toast Notification" throughout.

### Boundaries

- End-to-end `docker compose up` smoke test NOT run this session — requires Docker Desktop.
- LicenseService behavior with empty Stripe keys NOT yet verified (INFO-M11-001).
- `DISABLEAUTOUPDATE` registry key NOT yet implemented (INFO-M11-002).
- Git history sanitization NOT yet run (INFO-M11-004).

### Next Verification Gate (M11.D2)

Before D2 closes:
1. `docker compose up -d` on a clean Linux host — API, dashboard, and postgres must all reach healthy state.
2. Navigate to `http://host` — dashboard must load.
3. Register via `/register` — trial request must queue (not provision tenant directly).
4. Verify `LicenseService.CanRegisterDeviceAsync` with no Stripe subscription (INFO-M11-001 resolution).

---

## 2026-05-12 (Trial approval gate, install page, SEO refresh)

### Build Checks

- `dotnet build src\ToastRevival.Api\ToastRevival.Api.csproj`: **0 warnings, 0 errors**.
- `npm run build` from `src\ToastRevival.Dashboard`: **passed** (`tsc -b`, Vite build, and `scripts/prerender-seo.mjs`). The existing Vite chunk-size warning remains.
- `dotnet test tests\ToastRevival.Api.Tests\ToastRevival.Api.Tests.csproj`: **blocked by local Docker/Testcontainers**. The test assembly compiled and 21 tests passed, but 31 fixture-backed tests failed before app code ran because the shared Postgres Testcontainers fixture could not reach Docker (`Docker is either not running or misconfigured`).
- Browser smoke at `http://127.0.0.1:5173`: `/register`, `/`, `/pricing`, `/docs/getting-started`, and unauthenticated `/devices/install` redirect to `/login` verified. Only console warnings observed were existing React Router v7 future-flag warnings.

### Scope Verified

- Trial request backend compiles with EF model/migration, Turnstile verifier, rate-limit registration policy, pending-review registration endpoint, and platform-admin approve/reject endpoints.
- Dashboard production build compiles with the new `/register`, `/devices/install`, and `/system/trial-requests` routes.
- SEO prerender completed for the public marketing/docs routes after the content and JSON-LD refresh.
- Public trial form renders the company, website, contact, phone, job-title, use-case, and submit controls. The use-case select is operable in-browser.

### Boundaries

- End-to-end approval flow was not runtime-tested against a live Postgres instance because Docker is unavailable in this session.
- Production deploy still needs Turnstile site/secret keys and the `M10TrialApprovalGate` EF migration applied.

## 2026-05-09 (FIX-CI-002 — XML comment double-dash in wxs)

### Problem
After FIX-CI-001 pinned WiX 5.0.2 successfully, the next CI run still failed at "Build unsigned MSI" — different error this time: `error WIX0104: Not a valid source file; detail: An XML comment cannot contain '--', and '-' cannot be the last character. Line 78, position 16.`

### Root cause
Two XML comments in `installer/ToastRevival.Agent.Setup.wxs` (lines 78 and 132) documented the agent's `--setup-bootstrap` CLI mode by writing the literal flag inside the comment body. XML 1.0 forbids the literal sequence `--` inside comments. The flag appearing inside the `ExeCommand` attribute on the `WriteBootstrapJson` custom action (line 145) is fine — XML attribute parsing has no such restriction. Latent since commit `ecf79ce` (M2.C); never surfaced because Keith had not re-run `build-msi.ps1` after M2.C and the previous (now-decommissioned) CI runner pre-dated those comments.

### Fix
Rephrased both comments to use prose like "in setup-bootstrap mode" / "agent's own setup-bootstrap mode" instead of the literal CLI flag. Added an inline note in the second comment explaining the XML constraint to future maintainers. Functional behavior of the MSI is unchanged — the actual `--setup-bootstrap` invocation in the deferred custom action's `ExeCommand` attribute is intact.

### Standing rule
Documentation prose inside XML comments may NOT contain the literal `--` sequence. Rephrase or move the doc to a sibling .md. Code Sweep grep for `<!--[\s\S]*--[^>][\s\S]*-->` (multiline) on any session that touches `.wxs`, `.csproj`, or `.appxmanifest` files.

## 2026-05-09 (FIX-CI-001 — pin WiX to 5.0.2)

### Problem
Every push to main since commit `1c41d3e` (the CI runner-switch) had been failing identically in "Agent MSI Build / Build unsigned MSI" with `error WIX7015` (Open Source Maintenance Fee EULA). Five red runs (f68295b, ed928d2, 918c723, df41216, ca0972a). Keith forwarded the failure email after M7.A landed.

### Root cause
`.github/workflows/agent-build.yml` installs WiX with `dotnet tool install -g wix` — no version pin. The default resolves to WiX 7 (current latest), which introduced the OSMF EULA and aborts `wix build` invocations at the first call until accepted.

### Fix
Pinned to `dotnet tool install -g wix --version 5.0.2` — matches the version locked in M0A's environment spec and the version every shipped MSI was built against. Two-line comment in the workflow file explains the constraint for future maintainers.

### Standing rule
Any tool version locked by the local dev environment must be pinned in CI. "Latest" is not a version.

## 2026-05-09 (M7.A — Marketing Site Design Spec + Onboarding SVGs)

### Build Checks
- `npx tsc -p tsconfig.app.json --noEmit` (strict): **0 errors**.
- `npm run build` (Vite): **clean** (712 modules, pre-existing chunk size warning unchanged — marketing route lazy-loading is M7.B work).
- `dotnet build ToastRevival.sln`: not run (frontend-only milestone, no backend changes).

### Pre-Commit Fix
- **Security**: `Docs/Assets/*.pem` (Lightsail SSH private keys for TOASTWEB1 + TOASTDATA1) were untracked but NOT in `.gitignore` — a future `git add .` or `git add -A` would have committed them. Added `*.pem` and `*.key` patterns to `.gitignore` (with a `letsencrypt/` allow-list reserved for any future certbot artifacts that may legitimately live in-tree). `git check-ignore` confirms both keys now ignored. **Defense-in-depth fix; no key material was ever staged.**

### New Files
- `src/ToastRevival.Dashboard/src/icons/onboarding/index.tsx` — four React SVG components (`OnboardingBell`, `OnboardingTemplate`, `OnboardingPackage`, `OnboardingLaunch`) at the spec geometry. Stroke-only, 1.5 weight, round join/cap, `currentColor` so parent CSS controls the color.

### Modified Files
- `Docs/ToastRevival/DESIGN-SPEC.md` — Marketing Site section expanded from a 20-line stub to a ~480-line M7 specification covering architectural premise (single SPA, two visual contexts), routes, page chrome, page-by-page wireframes (Home / Pricing / Docs / How We Built It / Legal), image strategy, icon system, animation rules, mobile breakpoints, performance targets, copy direction (banned-word list), Onboarding SVG spec with reference SVG paths, `llms.txt` draft, SEO/JSON-LD direction, M7.B/C/D acceptance criteria, and explicit out-of-scope list.
- `src/ToastRevival.Dashboard/src/pages/Onboarding.tsx` — replaced four emoji icons (🔔📋📦🚀) on the welcome step with the four `Onboarding*` SVG components. Bell at 32px, three-up at 24px. Container colors set to `var(--accent)` so SVG `currentColor` resolves correctly. `aria-hidden="true"` on every decorative SVG.
- `.gitignore` — added `*.pem` and `*.key` (see Pre-Commit Fix).

### Code Sweep Results (M7.A — Abish, SHIP WITH NOTES)

**Scope**: SIGNIFICANT — design spec deliverable plus a frontend behavioral change (icon swap on a user-facing onboarding flow). Spec is the authoritative artifact; SVG swap is the implementation evidence that the spec is buildable.

**Blast Radius**:
- Upstream: `Onboarding.tsx` is rendered at `/onboarding` (post-register flow). No other call sites — verified via Grep.
- Downstream: New SVG components consumed only by `Onboarding.tsx`. No global side effects, no context changes, no API calls.
- Test coverage: No automated tests; manually verified via `npm run build` + visual inspection of bundled artifact.

**Five-Perspective Review**:
1. **Regression** — emoji rendering removed; SVG renders inline at the same container slots. fontSize:28/22 removed (was emoji-only); replaced with explicit SVG width/height + `color: var(--accent)` for stroke. Container layouts (64×64 bell halo, 3-column flex grid) unchanged. No layout regression observable in the production build.
2. **Edge cases** — `currentColor` requires a parent `color` style; both SVG mounts now set `color: 'var(--accent)'`. `aria-hidden="true"` on all four icons (decorative; the adjacent label provides the accessible name). `prefers-reduced-motion` not relevant (no animation in the icons themselves).
3. **Security** — pure JSX SVG, no `dangerouslySetInnerHTML`, no external `<image>`/`<use href>` references, no inline `<script>`. No credentials introduced. Pre-commit `.pem` ignore patch (above) closed the only material risk.
4. **Performance** — four tiny inline components (~1.5KB raw added to the bundle). Negligible. The pre-existing 717KB main bundle warning is unchanged and is M7.B (route-level code-splitting) territory.
5. **Architectural Consistency** — line weight 1.5, stroke-only, round caps/joins matches Lucide-derived sidebar icons. New `src/icons/onboarding/` directory cleanly separates bespoke icons from `lucide-react` imports. Component naming (`Onboarding*`) is verbose but unambiguous.

**INFO items filed**:
- INFO-M7A-001 (RESOLVED pre-commit): `*.pem` / `*.key` added to `.gitignore`.
- INFO-M7A-002 (deferred to M7.D): `DEPLOY.md:451` references the SSH key at `Docs/ToastRevival/Assets/...` but the file actually lives at `Docs/Assets/...`. Path mismatch. Fix DEPLOY.md before the M7.D deploy session OR move the key to match the documented path.
- INFO-M7A-003 (deferred to M7.B): the Home hero spec optionally renders a real notification count from a public `GET /api/analytics/global-summary` endpoint that does not yet exist. M7.B build must either add the endpoint OR drop the line from the hero (the spec already says "skip when count < 1000" — which is the current state). Don't ship a fake number.
- INFO-M7A-004 (acceptable / standing): cookie-consent / privacy posture is "we don't track" — verify in M7.B that no third-party scripts (Google Fonts, GTM, ads pixels) are added to `index.html` during the build phase. The fonts are already self-hosted; this is a vigilance item for the build session.

**RECOMMENDATION**: SHIP WITH NOTES. Spec is internally consistent, buildable, and matches Diana's standing rules. Onboarding SVG swap closes INFO-M6-004 cleanly. Critical pre-commit security fix (`.pem` gitignore) absorbed. M7.B/C/D have a complete acceptance bar to build against.

## 2026-05-09 (M5.D — Export)

### Build Checks

- `dotnet build ToastRevival.sln`: **0 warnings, 0 errors** (post M5.D — QuestPDF 2025.1.0 resolves and compiles clean).
- `npx tsc -p tsconfig.app.json --noEmit` (strict): **0 errors** after pre-commit TS fix (removed unused `notificationId` prop from `DeliveryExportMenu`).
- Vite production build: **clean** (708 modules, pre-existing Recharts chunk size warning unchanged).

### Pre-Commit Fix
- `PdfExportService.cs`: `log.UserId?.ToString()[..8] + "…" ?? "—"` — C# range indexer `[..8]` is not null-propagating. Patched to `log.UserId.HasValue ? log.UserId.Value.ToString()[..8] + "…" : "—"` before commit. NullReferenceException would have occurred at PDF generation time for any audit log entry where `UserId` is null (unauthenticated or background-service-generated entries).

### New Backend Files
- `Controllers/AuditController.cs` — `GET /api/audit` (paginated list, admin-only), `GET /api/audit/export?format=csv|pdf&days=N` (admin-only, no cap).
- `Services/PdfExportService.cs` — QuestPDF Community document generation: A4 landscape audit log (teal header stripe, alternating white/#F5F7F9 rows) + A4 portrait delivery report (summary metrics + delivery table).
- `ToastRevival.Api.csproj` — `QuestPDF 2025.1.0` added.
- `Program.cs` — `QuestPDF.Settings.License = LicenseType.Community` (pre-builder), `IPdfExportService` singleton registered.
- `Controllers/NotificationsController.cs` — `GET /api/notifications/{id}/report?format=csv|pdf` delivery report endpoint; `IPdfExportService` injected.

### New Frontend Files
- `api/audit.ts` — `auditApi.list()`, `auditApi.exportFile()`, `auditApi.exportDeliveryReport()`. All file downloads use fetch + `URL.createObjectURL` blob pattern (Authorization header preserved; no token in URL).
- `pages/AuditLog.tsx` — Audit Log admin page. Time range selector (7/30/90d), paginated table (50/page), export dropdown (CSV/PDF), error handling.
- `App.tsx` — `/audit` route added under `ProtectedRoute requireAdmin`.
- `Sidebar.tsx` — `AuditIcon` + `{ to: '/audit', label: 'Audit Log' }` in `ADMIN_ITEMS`.
- `pages/History.tsx` — `DeliveryExportMenu` component added in expanded row (CSV/PDF dropdown, blob download, per-row loading state).

### Code Sweep Results (M5.D — Abish, SHIP WITH NOTES)

**Pre-commit verifications:**
- Tenant isolation on AuditController: global query filter on `AuditLogs` via `ITenantProvider` ✓.
- Admin gate (`IsAdmin()` → Forbid 403) on both `List` and `Export` ✓.
- Delivery report tenant isolation: `Notifications.FindAsync` returns NotFound for cross-tenant IDs (global filter) → deliveries never queried ✓.
- QuestPDF license set before any document generation (program-level, before `builder.Build()`) ✓.
- NullRef patched pre-commit (see above) ✓.

**INFO items filed:** INFO-M5D-001 (CsvCell duplication), INFO-M5D-002 (AuditLog.Timestamp index), INFO-M5D-003 (CSV injection), INFO-M5D-004 (synchronous PDF generation).

## 2026-05-09 (M5.C — Asset Library + Notification Scheduling)

### Build Checks

- `dotnet build ToastRevival.sln`: **0 warnings, 0 errors** (post M5.C, all backend + agent).
- `npx tsc -p tsconfig.app.json --noEmit` (strict): **0 errors, 0 warnings**.
- No EF migration needed — `AssetLibrary` table and `Notification.ScheduledAt` column both exist in `InitialCreate`.

### New Backend Files
- `Controllers/AssetsController.cs` — GET /api/assets, POST /api/assets (multipart), DELETE /api/assets/{id}.
- `Services/IContentModerationService.cs` — additive `ModerateImageBytesAsync(byte[])`.
- `Services/ContentSafetyService.cs` — implements `ModerateImageBytesAsync` via Azure Content Safety `BinaryData` API.
- `Services/NotificationQueueService.cs` — `ProcessQueueAsync` (extracted), `RunSchedulerLoopAsync` (PeriodicTimer 60s), `EnqueueDueScheduledAsync` (startup backfill + timer sweep), `ProcessAsync` non-Queued guard.
- `Program.cs` — `app.UseStaticFiles()` added; `wwwroot/assets/` ensured at startup.

### New Frontend Files
- `src/api/assets.ts` — `assetsApi` (list, upload via raw fetch/FormData, delete), `getModerationStatus`, `AssetRecord` type.
- `src/pages/Assets.tsx` — full asset library page: drop zone, type selector, card grid, moderation status badges, Use as Hero/Logo actions, two-step inline delete.
- `Sidebar.tsx` — Assets nav item added between Templates and History.
- `App.tsx` — `/assets` route added.
- `Compose.tsx` — `scheduledAt` UTC conversion fix (`new Date(scheduledAt).toISOString()`); timezone note added.

### Code Sweep Results (M5.C — Abish, SHIP WITH NOTES)

**Pre-commit verifications:**
- AssetLibrary global query filter: confirmed at `AppDbContext.cs:120` — tenant isolation holds across List + Delete ✓.
- `AssetsController.Delete()` path traversal prevention: `Path.GetFullPath + StartsWith(allowed, OrdinalIgnoreCase)` — confirmed ✓.
- `UseStaticFiles()` ordering: after `UseCors()`, before `UseAuthentication()` — static file serving doesn't require auth (intentional for Windows agent image access) ✓.
- `ProcessAsync` non-Queued guard: prevents double-fanout on duplicate enqueue ✓.
- CSS audit on Assets.tsx: no `btn-danger-ghost`, no `--accent-primary`, no `form-label`/`form-input`, `.field` not needed (drop zone is custom), `btn-ghost` + inline `color: var(--status-error)` for delete ✓.
- Diana sign-off: drop zone border dashed with accent highlight on dragover, card image preview 120px cover, moderation badge colors (Pass=success, Review=warning, Block=error), type chip teal, inline two-step delete confirmed ✓.

**Pre-commit fix:**
- Added `ProcessAsync` non-Queued guard before the `Sending` state write — prevents duplicate-enqueue from startup-backfill + timer-tick overlap producing double-fanout. One-liner. Build clean post-patch.

**INFO items filed:**
- INFO-M5C-001: Uploaded assets publicly accessible by URL (required for Windows agent; document at M9).
- INFO-M5C-002: No `(Status, ScheduledAt)` index for scheduler sweep (acceptable MVP, M6+).
- INFO-M5C-003: Frontend drop zone accepts `image/*` MIME (backend extension check is the gate).

### Boundaries

- Asset upload NOT end-to-end verified against a live backend (requires running API + Azure Content Safety key or degraded Pass). Structural correctness confirmed by code review.
- Scheduler timer NOT verified with live notifications (requires running backend + scheduled notification in DB). Startup backfill logic confirmed by code review.
- Frontend asset grid NOT browser-verified (requires running dev server + backend). CSS and component structure verified by code audit.

---

## 2026-05-09 (M5.B — Analytics + Tenant Settings)

### Build Checks

- `dotnet build ToastRevival.sln`: **0 warnings, 0 errors** (post M5.B, all backend + agent).
- `npx tsc -p tsconfig.app.json --noEmit` (strict): **0 errors, 0 warnings**.
- `npm run build` (Vite 6 production): built in 2.88s ✓. Chunk size warning for recharts bundle (expected — INFO-only).
- New backend files: AnalyticsController.cs, TenantController.cs, TenantDtos.cs, migration M5TenantSettings.
- New frontend files: Analytics.tsx, TenantSettings.tsx. Modified: App.tsx, Sidebar.tsx, package.json.

### EF Migration Verification (M5TenantSettings)

- `dotnet ef migrations add M5TenantSettings`: generated cleanly.
- Migration adds 4 columns to `Tenants`: `DefaultAudioSetting` (text, nullable), `DefaultScenario` (integer, defaultValue=0), `LogoUrl` (text, nullable), `PrimaryColor` (text, nullable).
- `DefaultScenario = 0` = `ToastScenario.Default` — correct enum default.
- Up/Down methods: purely additive AddColumn / reversible DropColumn. No existing column changes.

### Code Sweep Results (M5.B — Abish, SHIP WITH NOTES)

**Pre-commit verifications:**
- CSS audit: `--accent` ✓, `.field` wrapper on all form inputs ✓, no `btn-danger-ghost` ✓, no `form-label`/`form-input`/`form-select` standalone ✓.
- Recharts compliance: `isAnimationActive={false}` on ALL 3 Line + Bar instances ✓, `ResponsiveContainer width="100%"` on all charts ✓, custom `ChartTooltip` on all charts ✓, default Recharts tooltip absent ✓, `<Legend>` only on LineChart (2-series) ✓, `<Cell>` for per-bar status colors ✓.
- Tenant isolation: global query filters on Notifications/NotificationDeliveries/Devices enforce per-tenant scope on all analytics queries — no TenantId filtering needed explicitly ✓.
- Admin gate: TenantController.UpdateSettings uses `IsAdmin()` pattern identical to UsersController:114 ✓.
- Diana sign-off: tooltip dark styling verified (`var(--bg-tertiary)`, 1px rgba border, border-radius 6px). Cell status colors match spec values exactly. TenantSettings color picker + hex input synchronized via shared `primaryColor` state ✓. APPROVED.

**INFO items filed:**
- INFO-M5B-001: AnalyticsController.Summary materializes delivery statuses in memory (acceptable at MVP scale).
- INFO-M5B-002: UpdateSettings silently ignores invalid DefaultScenario enum values.
- INFO-M5B-003: PrimaryColor stored without hex-format validation.

### Boundaries

- Analytics endpoints NOT end-to-end verified against live DB — requires running backend + populated notification/delivery data. Structural correctness confirmed by code review.
- TenantSettings PUT NOT tested with a live backend (requires admin token + running API). GET/PUT contract verified via code review.
- Chart rendering NOT browser-verified (requires running dev server with backend + data). Recharts spec compliance verified by code audit.

---

## 2026-05-09 (M5.A — Pre-work + User Management + API Keys)

### Build Checks

- `dotnet build ToastRevival.sln`: 0 warnings, 0 errors. (Post-M5.A, all backend + agent)
- `npx tsc -p tsconfig.app.json --noEmit` (strict): 0 errors, 0 warnings.
- 22 new/modified backend files; 5 new/modified frontend files.

### Code Sweep Results (M5.A — Abish, SHIP WITH NOTES)

**Pre-commit verification:**
- `NotificationTemplate` interface removal: confirmed zero imports in Dashboard. ✓
- AuthController template seeding: confirmed SaveChangesAsync within active transaction; CommitAsync commits both user + templates atomically. ✓
- DeviceGroupsController DeviceCount: increment on AddMember, decrement with floor guard on RemoveMember. ✓
- UsersController self-deletion guard: HTTP 400 when userId == callerId. ✓
- TemplateDbRecord only imported in notifications.ts + Compose.tsx — no orphaned references. ✓

**INFO items filed:**
- INFO-M5-001 (M6): No explicit RollbackAsync around template seeding in AuthController.Register.
- INFO-M5-002 (acceptable): UsersController.Invite has no role ceiling.
- INFO-M5-003 (M6): TemplatesController.BuildDefaultTemplates coupling (refactor to service).

**INFO items closed:**
- INFO-M4-001 ✓: templateId wired via TemplatesController + Compose.tsx slug→Guid fetch.
- INFO-M4-002 ✓: DeviceGroupsController with full CRUD + member management.
- INFO-M4-003 ✓: History() pagination with Skip/Take, page/pageSize params.

## 2026-05-08 (M4 Admin Dashboard)

### Build Checks

- `npx tsc -p tsconfig.app.json --noEmit` (strict): 0 errors, 0 warnings.
- `npm run build` (Vite 6 production): 51 modules transformed, 272KB / 82KB gzip. Build succeeded.
- `dotnet build src/ToastRevival.Api/ToastRevival.Api.csproj` (post INFO-M3-002/003 fixes): 0 warnings, 0 errors.
- Git commit `016c4c9` pushed to GitHub main.

### Code Sweep Results

- **INFO-M4-001** (pre-commit catch): `templateId` string slug ≠ backend `Guid?`. Fixed by omitting field from send payload.
- **INFO-M4-002**: `/api/devicegroups` returns 404; Dashboard degrades gracefully. Deferred to M5.
- **INFO-M4-003**: `GET /api/notifications` ignores pagination params; hard-caps at 100. Deferred to M5.
- API response shape mismatch caught during review: `NotificationHistoryItem` (from list) uses `targetDeviceCount` + `clickedCount`, not `deviceCount` + `interactionCount`. Fixed before commit.

### Boundaries

- Dashboard TypeScript types verified against actual backend DTO shapes.
- Live preview CSS implementation reviewed against Windows 11 toast anatomy (Diana sign-off).
- MFA broadcast flow logic verified (BroadcastConfirmModal → mfa/verify → elevated token → send).
- No browser UI testing yet (requires running backend + populated DB).

## 2026-05-07

### Environment Checks

- `git --version`: passed.
- `dotnet --info`: initially showed SDK `10.0.203` only.
- Installed .NET SDK `8.0.420` with `winget`.
- `dotnet --list-sdks`: passed with `8.0.420` and `10.0.203`.
- `dotnet nuget list source`: initially showed no sources.
- Added repo-local `NuGet.config` for `https://api.nuget.org/v3/index.json`.

### Build Checks

- `dotnet restore ToastRevival.sln`: passed after adding `NuGet.config`.
- `dotnet build ToastRevival.sln --no-restore`: initially failed because Windows App SDK CLI build could not load `Microsoft.Build.Packaging.Pri.Tasks.ExpandPriContent`.
- Added `<EnableMsixTooling>true</EnableMsixTooling>` to `src/ToastRevival.Agent/ToastRevival.Agent.csproj`.
- `dotnet build ToastRevival.sln --no-restore`: passed with 0 warnings and 0 errors.

### Runtime Checks

- `.\scripts\run-agent-spike.ps1 -WaitSeconds 5`: passed.
- Console output reported `ToastRevival M0A notification sent.`
- `dotnet publish src\ToastRevival.Agent\ToastRevival.Agent.csproj -c Release -r win-x64 --self-contained false -o artifacts\ToastRevival.Agent\win-x64-framework-dependent`: passed.
- `.\artifacts\ToastRevival.Agent\win-x64-framework-dependent\ToastRevival.Agent.exe --wait 5`: passed and captured `Notification activated: action=acknowledge;source=m0a`.
- `dotnet publish src\ToastRevival.Agent\ToastRevival.Agent.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o artifacts\ToastRevival.Agent\win-x64-self-contained`: passed.
- `.\artifacts\ToastRevival.Agent\win-x64-self-contained\ToastRevival.Agent.exe --wait 5`: passed and captured `Notification activated: action=acknowledge;source=m0a`.

### Artifact Checks

- Framework-dependent artifact: 32 files, about 35.83 MB.
- Self-contained artifact: 448 files, about 160.62 MB.

### Signing/Packaging Tool Checks

- `signtool.exe`: not found on PATH.
- `makeappx.exe`: not found on PATH.
- Code-signing cert check: no matching cert with private key visible in `Cert:\CurrentUser\My` or `Cert:\LocalMachine\My`.

### Boundaries

- This confirms local unpackaged notification API execution, published executable execution, and notification activation callback handling from the development machine.
- This does not yet confirm packaging, signing, reboot survival, Store submission, Intune deployment, RMM deployment, or clean-machine behavior.

## 2026-05-07 (rich notification spike)

### Asset Generation

- `.\scripts\generate-toast-assets.ps1`: passed. Produced `src/ToastRevival.Agent/Assets/toast-hero.png` (364x180, 11,549 bytes), `toast-logo.png` (48x48, 296 bytes), `toast-inline.png` (200x120, 3,872 bytes).
- Image dimensions verified via `System.Drawing.Image.FromFile`.

### Build Checks

- `dotnet build C:\SOURCE\toast\ToastRevival.sln`: passed with 0 warnings and 0 errors after the `ToastTemplates.cs` add and the `Program.cs` refactor.

### Runtime Checks (`dotnet run`)

- `--template plain`: passed - `Scenario: Default, Sound: (none), Buttons: 1`.
- `--template announcement`: passed - `Scenario: Default, Sound: Default, Buttons: 1`.
- `--template alert`: passed - `Scenario: Urgent, Sound: Alarm, Buttons: 2`.
- `--template action`: passed - `Scenario: Default, Sound: Reminder, Buttons: 2`. Captured a late activation callback `action=acknowledge;source=m0a;template=Plain` from the prior `plain` run, which is the expected COM activation bridge behaviour.
- `--template reminder`: passed - `Scenario: Reminder, Sound: Reminder, Buttons: 1`.
- `--template celebration`: passed - `Scenario: Default, Sound: Default, Buttons: 1`.
- `--template maintenance`: passed - `Scenario: Default, Sound: Default, Buttons: 2`.

### Publish Checks

- `dotnet publish` framework-dependent: passed. Output `artifacts/ToastRevival.Agent/win-x64-framework-dependent`, 35 files, 35.86 MB. `Assets/` folder with all three PNGs shipped.
- `dotnet publish` self-contained (`-p:WindowsAppSDKSelfContained=true`): passed. Output `artifacts/ToastRevival.Agent/win-x64-self-contained`, 451 files, 160.65 MB. `Assets/` folder shipped.
- Published exe smoke test: `ToastRevival.Agent.exe --template alert --no-wait` reported the expected scenario, sound, and button count.

### Boundaries

- Templates were verified at the API level (build, send, output reporting). Visual rendering in Action Center has not been pixel-checked by Diana - that is a manual review pending Keith pulling open Action Center on the dev workstation.
- No packaged install was run. No signing was attempted. No reboot/login persistence was tested.
- `signtool.exe` and `makeappx.exe` are still not on PATH locally; the renewed token-backed OV cert is still not available to Windows signing tools.

## 2026-05-07 (M0A close - signed MSI install)

### Packaging

- WiX 5.0.2 installed via `dotnet tool install --global wix --version 5.*` (WiX 7 declined - requires paid OSMF EULA).
- `scripts/build-msi.ps1` produces a per-machine self-contained MSI in `artifacts/installer/`.
- 0.1.0.0 build: `ToastRevival.Agent-0.1.0.0.msi`, 50.60 MB. Pre-rebrand naming.
- 0.2.0.0 build: `ToastNotification.Agent-0.2.0.0.msi`, 50.60 MB. Rebranded user-facing surfaces. Same UpgradeCode for clean MajorUpgrade.

### Signing

- Keith signed `ToastRevival.Agent-0.1.0.0.msi` locally with the Thales hardware token.
- `Get-AuthenticodeSignature` verification: Status `Valid`, Signer `CN="Toast2IT, LLC", S=Florida, C=US`, Issuer `CN=Sectigo Public Code Signing CA R36`, NotAfter 2027-04-15, Thumbprint 19B07B46712C2D87FF6AA99842F7EF6B036FEDA7, Timestamp `CN=DigiCert SHA256 RSA4096 Timestamp Responder 2025 1`.
- The 0.2.0.0 rebrand MSI was rebuilt after install and is awaiting re-sign before redeploy.

### MSI Property Verification (0.2.0.0)

Read directly from the MSI Property table via WindowsInstaller COM:
- `ProductName` = `Toast Notification Agent`
- `Manufacturer` = `Toast2IT, LLC`
- `ProductVersion` = `0.2.0.0`
- `UpgradeCode` = `{A6F3D8F1-7B22-4E5A-9E3C-2A4F8B1C9D70}` (matches 0.1.0.0)

### Clean-Machine Install (Win11 lab)

- App installed - no issues reported.
- Shortly after reboot, toasts were seen. No issues reported.
- This closes M0A D6 (clean-machine install), D7 (user context), D8 (login + reboot persistence).

### Boundaries

- Lab machine SmartScreen behavior on the OV-signed MSI was not specifically captured (presumed clean from "no issues" but no screenshot filed).
- Re-test of the rebranded 0.2.0.0 MSI on the lab machine was declined - rename does not change install / login / reboot mechanics, only display strings.
- Domain-joined / GPO / Intune / multi-user scenarios are M0 D4, not M0A.

## 2026-05-07 (M0 D2 MSIX build)

### MSIX Tile Asset Generation

- `.\scripts\generate-msix-tile-assets.ps1`: passed. Produced `src/ToastRevival.Agent/Images/Square44x44Logo.png` (44x44, 240 B), `Square150x150Logo.png` (150x150, 914 B), `Wide310x150Logo.png` (310x150, 9,735 B), `StoreLogo.png` (50x50, 257 B). Image dimensions verified via `[System.Drawing.Image]::FromFile`.

### MSIX Build

- `.\scripts\build-msix.ps1 -SkipAssetGeneration`: initial run produced the .msix at `artifacts/installer/msix/ToastNotification.Agent-0.2.0.0.msix` (63.53 MB) BUT failed afterwards on missing `Properties/launchSettings.json` (required by `Microsoft.WindowsAppSDK.SingleProject.targets`).
- Added `src/ToastRevival.Agent/Properties/launchSettings.json` with `MsixPackage` profile.
- `.\scripts\build-msix.ps1 -SkipAssetGeneration` (re-run): passed. 0 errors, 1 warning (`mspdbcmf.exe` missing - symbols package skipped, benign; FIX-MSIX-003).

### Manifest Verification (read directly from the .msix)

Extracted `AppxManifest.xml` from the produced `.msix` via `System.IO.Compression.ZipFile.ExtractToDirectory`:
- `Identity.Name` = `Toast2IT.ToastNotification.Agent`
- `Identity.Publisher` = `CN="Toast2IT, LLC", S=Florida, C=US` (XML-escaped `&quot;` in source) - matches Sectigo OV cert subject exactly.
- `Identity.Version` = `0.2.0.0`
- `Identity.ProcessorArchitecture` = `x64`
- `Properties.DisplayName` = `Toast Notification`
- `Properties.PublisherDisplayName` = `Toast2IT, LLC`
- `Application.Executable` = `ToastNotification.Agent.exe`
- `Application.EntryPoint` = `Windows.FullTrustApplication`
- `VisualElements.DisplayName` = `Toast Notification`
- `VisualElements.Description` = `Managed Windows toast notifications for MSP-managed endpoints.`
- `VisualElements.BackgroundColor` = `#0F1117`
- All Logo paths point at `Images\*.png`.
- `<rescap:Capability Name="runFullTrust" />` present.
- Zero occurrences of "ToastRevival" in any user-visible field. M0A standing rule held.

### Package Contents Verification

- `ToastNotification.Agent.exe` present, 282,624 bytes.
- 458 files total; WinAppSDK 1.7 self-contained runtime DLLs bundled (`Microsoft.WindowsAppRuntime.dll` 1,890,360 bytes, `Microsoft.WindowsAppRuntime.Bootstrap.dll` 396,344 bytes).
- `Assets/` folder shipped with all three toast PNGs (`toast-hero.png` 11,818 B, `toast-logo.png` 296 B, `toast-inline.png` 3,872 B).
- `Images/` folder shipped with all four tile PNGs (matching the manifest Logo paths).

### Regression Check

- `dotnet build src\ToastRevival.Agent\ToastRevival.Agent.csproj -c Release` (default unpackaged path): passed. 0 warnings, 0 errors, 2.17s elapsed. Output landed in `bin/Release/net8.0-windows10.0.19041.0/ToastNotification.Agent.dll` (separate from MSIX path at `bin/x64/Release/.../win-x64/`).
- `scripts/build-msi.ps1` not re-run this session - file content unchanged, no edits affecting MSI publish path. Existing M0A MSI artifact unaffected.

### Boundaries

- The .msix produced is UNSIGNED. M0 D2 deliverable says "signed with OV cert, installs cleanly on Win10 1809+ / Win11" - signing and install validation are Keith handoff steps:
  - Sign: Thales hardware token + Sectigo OV cert (same flow used for M0A MSI). `signtool.exe sign /tr http://timestamp.digicert.com /td sha256 /fd sha256 /a /n "Toast2IT, LLC" "<path>\ToastNotification.Agent-0.2.0.0.msix"`
  - Verify: `Get-AuthenticodeSignature` should report Status=Valid, Signer="Toast2IT, LLC", Issuer=Sectigo Public Code Signing CA R36.
  - Install: Win11 lab machine confirmed for M0A. Win10 1809 is not on hand; that gap may need to be closed during M0 D4.
- `signtool.exe` and `makeappx.exe` still not on PATH locally - MSIX packaging used `MakeAppx` from the `Microsoft.Windows.SDK.BuildTools` transitive NuGet of WinAppSDK 1.7, which is invoked by the WinAppSDK packaging targets directly. signtool is still not present locally; Keith's signing workstation has it.

## 2026-05-07 (M0 D2 Publisher fix - 0x80091005)

### First sign attempt (0.2.0.0) failed

Keith opened `ToastNotification.Agent-0.2.0.0.msix` in DigiCert Certificate Utility, selected the Toast2IT, LLC cert (Sectigo OV via Thales token, cert chain validated OK), clicked Sign. Sign failed with:

```
The file C:\SOURCE\toast\artifacts\installer\msix\ToastNotification.Agent-0.2.0.0.msix could not be signed (0x80091005).
```

### Root cause - cert subject has more RDNs than the manifest Publisher had

Cert utility Details tab Subject field (the authoritative source for cert subject):

```
CN = Toast2IT, LLC
O  = Toast2IT, LLC
S  = Florida
C  = US
```

Manifest 0.2.0.0 Publisher (incomplete): `CN="Toast2IT, LLC", S=Florida, C=US` - missing `O=Toast2IT, LLC`.

### Fix - rebuild 0.2.0.1 with corrected Publisher

`src/ToastRevival.Agent/Package.appxmanifest` Identity element updated:

```xml
Publisher="CN=&quot;Toast2IT, LLC&quot;, O=&quot;Toast2IT, LLC&quot;, S=Florida, C=US"
Version="0.2.0.1"
```

Rebuilt via `.\scripts\build-msix.ps1 -Version 0.2.0.1 -SkipAssetGeneration`: passed, 0 errors, 1 mspdbcmf warning (cosmetic). Output: `artifacts/installer/msix/ToastNotification.Agent-0.2.0.1.msix` (63.53 MB, UNSIGNED).

Verified by extracting `AppxManifest.xml` from the new .msix:

```
Name      : Toast2IT.ToastNotification.Agent
Publisher : CN="Toast2IT, LLC", O="Toast2IT, LLC", S=Florida, C=US
Version   : 0.2.0.1
```

Old artifacts deleted: `ToastNotification.Agent-0.2.0.0.msix`, `ToastRevival.Agent_0.2.0.0_x64_Test/`. Only 0.2.0.1 outputs remain on disk.

### Lesson captured (also in EVIDENCE/2026-05-07-m0-d2-msix-publisher-fix.md and project context)

- MSI signing does NOT enforce Publisher-vs-cert match; MSIX signing DOES. The M0A MSI signed fine with a manifest-less Publisher; the MSIX rejected because the four-RDN cert subject only had three of those RDNs in the manifest.
- The team's prior memory string `CN="Toast2IT, LLC", S=Florida, C=US` came from a transcription of `Get-AuthenticodeSignature` output on the M0A MSI, which truncated/abbreviated the subject. That string is NOT authoritative for MSIX work.
- Authoritative cert subject sources: cert utility Details tab; `Get-ChildItem Cert:\...\My | Where Subject -like "*<co>*" | Select Subject`; or `(Get-AuthenticodeSignature <signed-msix>).SignerCertificate.Subject` (after a successful MSIX sign).
- Code Sweep Step 4 must enumerate every RDN in the cert subject and verify each appears in the manifest Publisher in the same order with the same quoting before any sign handoff.


## 2026-05-08 (M0 D4 — FIX-MSIX-002 + MSI 0.3.1.0 build)

### FIX-MSIX-002 Code Changes

- `Package.appxmanifest`: `TargetDeviceFamily MinVersion` 10.0.17763.0 → 10.0.19041.0; `Version` 0.2.0.3 → 0.2.1.0.
- `ToastRevival.Agent.csproj`: `<TargetPlatformMinVersion>` 10.0.17763.0 → 10.0.19041.0.
- `Program.cs`: removed "spike" wording from Win-version error message (user-visible cleanup).

### MSI 0.3.1.0 Build

- `.\scripts\build-msi.ps1 -Version 0.3.1.0`: passed with 0 errors, 1 cosmetic mspdbcmf warning (FIX-MSIX-003, pre-existing).
- Artifact: `artifacts/installer/ToastNotification.Agent-0.3.1.0.msi`, 50.61 MB.

### MSI 0.3.1.0 Property Verification

Read via WindowsInstaller COM:

```
ProductName    = Toast Notification Agent
Manufacturer   = Toast2IT, LLC
ProductVersion = 0.3.1.0
UpgradeCode    = {A6F3D8F1-7B22-4E5A-9E3C-2A4F8B1C9D70}
```

UpgradeCode matches 0.3.0.0 — `<MajorUpgrade>` will fire on install of 0.3.1.0 over 0.3.0.0.

### MSIX Smoke Check (Abish QA gate — Code Sweep standing rule)

**MSIX smoke check command (canonical, updated M0 D5):**
```powershell
dotnet build src\ToastRevival.Agent\ToastRevival.Agent.csproj -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:TargetPlatformVersion=10.0.22621.0
```
The `-p:TargetPlatformVersion=10.0.22621.0` flag is required. Without it the .NET SDK TFM (`net8.0-windows10.0.19041.0`) overwrites `TargetPlatformVersion` via a late `.targets` import that runs after PropertyGroup evaluation, silently capping `MaxVersionTested` at `10.0.19041.0`. Command-line flags have higher MSBuild precedence and win. Discovered M0 D5 (2026-05-08). See INFO-D5-003.

- M0 D4 smoke check (before D5 flag discovery): passed, 0 errors, 1 mspdbcmf warning (FIX-MSIX-003, pre-existing).
- AppxManifest.xml extracted and verified from produced .msix:
  - `Identity.Version = 0.2.1.0` ✓
  - `TargetDeviceFamily.MinVersion = 10.0.19041.0` ✓ (FIX-MSIX-002 applied correctly)
  - `TargetDeviceFamily.MaxVersionTested = 10.0.19041.0` — pre-existing at D4; **RESOLVED M0 D5** via `-p:TargetPlatformVersion=10.0.22621.0` in `build-msix.ps1`.

### Regression Check

- `dotnet publish` (unpackaged, default path) compiled cleanly as part of the MSI build: 0 errors.
- `scripts/build-msi.ps1` requires no changes for the version bump; `$Version` is a parameter.

### Boundaries

- MSI 0.3.1.0 is UNSIGNED. Keith signs before the major-upgrade test.
- MSIX 0.2.1.0 not built in this session (D4 focus is MSI/scheduled-task matrix; MSIX path is D5).
- D4 test matrix execution (Tests 1-5) is a Keith handoff.
  See `EVIDENCE/2026-05-08-m0-d4-matrix-results.md` for the full test procedure.
  See `scripts/verify-d4-matrix.ps1` for the pass/fail verification script.

## 2026-05-08 (M0 D3 build — MSI with Scheduled Task)

### Code Sweep — INFO findings (non-blocking)

- **INFO-D3-1** (architectural): Major upgrade from a future 0.3.x to 0.3.y will fire `UninstallScheduledTask` during the old product's `RemoveExistingProducts` (sequence 1401, internal uninstall script with `REMOVE="ALL"`) and `InstallScheduledTask` during the new product's install (sequence 4001). The `/F` flag on `schtasks /Create` is the safety net for the brief overlap window. Validate this end-to-end during M0 D4 GPO matrix when running multi-version upgrade scenarios.
- **INFO-D3-2** (path coupling): `installer/ToastNotificationLogon.xml` hard-codes `%ProgramFiles%\Toast Notification\ToastNotification.Agent.exe` literal. The WiX `INSTALLFOLDER` resolves to the same path under the default `<StandardDirectory Id="ProgramFiles64Folder">` placement. If a future installer change relocates `INSTALLFOLDER` (e.g., to `%LocalAppData%` for a per-user MSI variant), the XML would not auto-update. Captured as a CONTEXT.md standing rule under "Scheduled Task primitive (M0 D3 standing rules)."
- **INFO-D3-3** (artifact noise): WindowsAppSDK self-contained publish ships `RestartAgent.exe` and `createdump.exe` alongside the agent. Microsoft-signed binaries used by certain WindowsAppSDK runtime paths. Not consumed by current product behavior. Acceptable.

### Pre-install structural verification

- **MSI build**: `scripts\build-msi.ps1` (default `$Version=0.3.0.0`) produced `artifacts\installer\ToastNotification.Agent-0.3.0.0.msi` (50.61 MB) with 0 errors and the pre-existing FIX-MSIX-003 mspdbcmf warning (cosmetic, unrelated to D3).
- **XML schema parse** (System.Xml on `installer\ToastNotificationLogon.xml`): root `Task version=1.4`, URI `\Toast2IT\ToastNotificationAgentLogon`, `LogonTrigger` enabled, `GroupId=S-1-5-32-545`, `RunLevel=LeastPrivilege`, action `%ProgramFiles%\Toast Notification\ToastNotification.Agent.exe --template alert --no-wait`.
- **MSI Custom Action table**: `InstallScheduledTask` Type=3106 (deferred + non-impersonate + ExeCommand + Source=Directory, return=check) with target `"[System64Folder]schtasks.exe" /Create /TN "\Toast2IT\ToastNotificationAgentLogon" /XML "[INSTALLFOLDER]ToastNotificationLogon.xml" /F`. `UninstallScheduledTask` Type=3170 (same + return=ignore for idempotency) with target `"[System64Folder]schtasks.exe" /Delete /TN "\Toast2IT\ToastNotificationAgentLogon" /F`.
- **MSI InstallExecuteSequence**: `UninstallScheduledTask` at seq 3499 with `REMOVE="ALL"`; `RemoveFiles` at 3500; `InstallFiles` at 4000; `InstallScheduledTask` at 4001 with `NOT REMOVE`.
- **MSI Shortcut table**: only `StartMenuAgentShortcut` remains. `StartupShortcut` removed cleanly.
- **MSI File table / payload**: `ToastNotificationLogon.xml` mapped to component `LogonTaskXml`. Admin-install extract (`msiexec /a`) confirmed XML lands at `<INSTALLFOLDER>\ToastNotificationLogon.xml`, byte-identical to repo source (3,816 bytes, UTF-16 LE BOM `FF FE`).

### Pre-flight: schtasks /Create from unprivileged shell

- Command: `schtasks.exe /Create /TN \Toast2IT\_PreflightTest_M0D3 /XML installer\ToastNotificationLogon.xml /F`
- Result: `ERROR: Access is denied.` (exit 1)
- Interpretation: **expected and correct**. Creating a task with a group principal (`S-1-5-32-545` BUILTIN\Users) requires admin elevation. The error reached "Access is denied" rather than "task XML is not valid" — confirming Task Scheduler accepted the XML schema and only refused at the authorization step. The MSI deferred custom action runs as SYSTEM (`Impersonate="no"`), which has the privilege.

### Boundaries

- This confirms the MSI builds, the XML parses, the WiX table layout matches the design, and the XML payload survives the cab → admin-install extract round-trip byte-identical.
- This does NOT confirm signed install on a Win11 lab, that the scheduled task is actually created on the endpoint, that the task fires at logon, that the toast renders, or that uninstall removes the task. Those checks are Keith's hand-off step (signed install + `Get-ScheduledTask` + log-out/in verification + uninstall verification + idempotency check). Hand-off detail in `EVIDENCE/2026-05-08-m0-d3-msi-build-with-scheduled-task.md` § Hand-off.

## 2026-05-08 (M2.D — Velopack Auto-Update)

### Scope

M2.D delivers D8 (auto-update for MSI channel). Velopack 0.0.1298 integrated. New `UpdateService` class, `VelopackApp.Build().Run()` startup hook, `TrySelfRedirect` for scheduled-task-pointer problem, tray "Update Available" menu item, `scripts/build-release.ps1` pipeline script.

### Build Checks

- `dotnet build src/ToastRevival.Agent/ToastRevival.Agent.csproj -c Release`: **0 warnings, 0 errors** after Velopack API correction (0.0.1298 uses `OnAfterInstallFastCallback` / `OnAfterUpdateFastCallback`, not `With*` style used in older documentation; `ApplyUpdatesAndRestart(VelopackAsset)` not `ApplyUpdatesAndRestart(UpdateInfo)`).
- `dotnet build ToastRevival.sln -c Release`: **0 warnings, 0 errors** solution-wide.
- MSIX smoke check (`dotnet build ... -p:WindowsPackageType=MSIX ... -p:TargetPlatformVersion=10.0.22621.0`): passed. 1 warning (pre-existing FIX-MSIX-003 mspdbcmf.exe, cosmetic). MSIX produces cleanly; Velopack 0.0.1298 NuGet reference does not affect MSIX packaging path.

### Code Sweep — INFO findings (non-blocking)

- **INFO-M2D-003**: `updateTask` not awaited on shutdown — fire-and-forget, all exceptions caught in loop. Acceptable.
- **INFO-M2D-004**: `_updateItem` Font object not explicitly disposed — process-lifetime, negligible. Acceptable.
- **INFO-M2D-005**: `TrySelfRedirect` launches unsigned binary from user-writable path — inherent Velopack per-user model limitation. Authenticode verification deferred to M3.
- **INFO-M2D-006**: Velopack FastCallback hooks fire before `DiagLog.Init()` — messages silently dropped. Acceptable (lifecycle events only, Velopack has its own log).

### Architecture Verification

- **Registry toggle path**: `HKLM\SOFTWARE\Toast2IT\Toast Notification\DisableAutoUpdate = 1` → `IsAutoUpdateEnabled()` returns false → `RunUpdateLoopAsync` returns immediately. Not runtime-verified (requires a lab machine with the key set) but code path is simple and audited.
- **TrySelfRedirect path**: When running from `%ProgramFiles%` with no `%LocalAppData%\ToastNotification.Agent\current\ToastNotification.Agent.exe` → returns false. Not runtime-verified (requires a full Velopack-channel install) but the path has three independent guards: system-install check, File.Exists, version comparison.
- **Update check path**: `IsInstalled=false` on MSI-deployed binary → `CheckAndDownloadAsync` returns early. Correct behavior — MSI-deployed agents are not Velopack-managed. End-to-end update cycle (Velopack-channel install → check → download → apply) requires a live feed server and is a M9 production setup item.

### Boundaries

- Velopack update cycle (check → download → apply → restart) NOT end-to-end verified — requires live feed server at `https://releases.toastnotification.com/agent/win-x64`. Production feed setup deferred to M9.
- `vpk` CLI tool not installed in this session — `scripts/build-release.ps1` documents the prerequisite (`dotnet tool install -g vpk`). First use is M9.
- Signing of Velopack packages (`.nupkg` + release zip) follows the same Thales/Sectigo OV flow as MSI/MSIX. Not validated this session.

## 2026-05-09 (M2.A — Agent ↔ Backend Pipeline + HMAC)

### Scope

M2 sliced. M2.A delivers D1 SignalR client + auto-reconnect, D2 toast rendering from backend payload, D3 first-run device registration + 30-min heartbeat ping, D4 HMAC payload verification, D5 ReportDelivery + ReportInteraction over the hub, INFO-D5-001 single-instance mutex guard, INFO-MSIX-004-D activation handler, plus a new device-JWT-authenticated REST `POST /api/notifications/{id}/interactions` endpoint for the activation-handler exit path. M2.B (D6 missed catch-up + recovery for orphan Sending), M2.C (D7 system tray + D9 MSI properties — Diana session), M2.D (D8 Velopack auto-update) deferred.

### Build Checks

- `dotnet build ToastRevival.sln`: passed with **0 warnings, 0 errors** after Code Sweep FIX-M2A-001 patch.
- MSIX smoke check (`dotnet build src\ToastRevival.Agent\ToastRevival.Agent.csproj -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:TargetPlatformVersion=10.0.22621.0`): passed with 1 warning (pre-existing FIX-MSIX-003 `mspdbcmf.exe` cosmetic). Produced `bin\Debug\net8.0-windows10.0.19041.0\win-x64\AppPackages\ToastRevival.Agent_0.2.1.0_x64_Debug_Test\ToastRevival.Agent_0.2.1.0_x64_Debug.msix`. All M0 D5 manifest standing checks intact (`windows.comServer` + `windows.toastNotificationActivation` + `windows.startupTask`, four-dash sentinel on `<com:ExeServer>`, CLSID byte-identical, MaxVersionTested=10.0.22621.0).
- `dotnet ef migrations add AddTenantSigningKey --project src/ToastRevival.Api --no-build`: passed (modulo pre-existing INFO-M1-001 DeviceGroupMember warning).

### Runtime Smoke Checks

- **DiagnosticMode regression** (`dotnet run --project src/ToastRevival.Agent --no-build -- --template plain --no-wait`): exit code 0, console output `Toast Notification sent. Template: Plain`. M0A argv-driven path preserved verbatim.
- **PrimaryMode unconfigured-exit** (`dotnet run --project src/ToastRevival.Agent --no-build` with no `TOAST_TENANT_ID`/`TOAST_SERVER_URL` env vars and no `bootstrap.json`): exit code 9, stderr `Toast Notification agent is not configured. Set TOAST_TENANT_ID and TOAST_SERVER_URL, or have the installer drop bootstrap.json next to the exe.` Clean failure mode.

### Code Sweep — Pre-Commit FIX

- **FIX-M2A-001** (BLOCKING → resolved before commit): `Program.cs:14` mutex name was `Global\Toast2IT.ToastNotification.PrimaryWorker`. The `Global\` prefix uses the kernel system-wide BaseNamedObjects namespace — meaning two interactive users on the same Win11 box (Fast User Switching, RDP, Terminal Services) would have user 2's agent collide with user 1's mutex and exit with code 5. **Regression of M0 D4 multi-user verification** (the matrix run on 2026-05-08 confirmed a second local user receives toasts via the BUILTIN\Users-group Scheduled Task). Patched to `Local\` prefix (per-session BaseNamedObjects) so each Windows session gets its own primary. Build re-verified clean post-patch.

### Code Sweep — INFO findings (non-blocking, deferred)

- **INFO-M2A-002** (security, defer to M3): DeviceConfig persisted as plaintext JSON at `%LOCALAPPDATA%\Toast2IT\Toast Notification\config.json` containing the device JWT and per-tenant HMAC signing key. Per-user LocalAppData ACLs gate ordinary access; admin-credential exfiltration is not gated. M3 hardening should wrap with DPAPI CurrentUser scope (`ProtectedData.Protect`).
- **INFO-M2A-003** (M2.B): `NotificationQueueService.ProcessAsync` writes `Status=Sending` then later `Status=Sent/Failed`. A crash between the two writes leaves the row stuck in `Sending`. Not a corruption bug (deliveries remain `Pending`), but a recovery concern. M2.B catch-up should add a startup recovery for orphan `Sending` rows (`UPDATE Notifications SET Status=Failed WHERE Status=Sending AND SentAt < now() - INTERVAL '5 minutes'`).
- **INFO-M2A-004** (M2.B): No agent-side de-dup on `notificationId`. SignalR redelivery on reconnect could double-render. M2.B catch-up should track recently-rendered IDs in a 1-hour rolling window.
- **INFO-M2A-005** (deploy doc, M9): Migration backfill SQL uses `gen_random_uuid()`, which is built-in to Postgres 13+. Document Postgres minimum-version in M9 deployment.

### Boundaries

- This confirms the wire-protocol + HMAC contract is structurally correct (server signs, agent verifies via constant-time compare, both sides agree on the JSON byte sequence to sign over because the server pre-serializes and ships the string + signature as separate SignalR args).
- This does NOT confirm end-to-end runtime: a real Postgres instance + running API + agent install on a signed Win11 lab + button-click → ReportInteraction round-trip. That hand-off is Keith's lab work, gated on MSI/MSIX rebuild + signing.
- Test coverage gap (INFO-M1-004) inherited from M1 — first tests at M8.

## 2026-05-08 (M3 — Content Moderation + MFA + Security Hardening)

### Scope

M3 delivers D1-D7, D9 (D8 was pre-closed in M2.A). New backend surfaces: Azure Content Safety moderation pipeline, TOTP MFA enrollment/verification, tenant blocklists, admin approval queue, broadcast gate. Agent security: DPAPI ConfigStore encryption, Authenticode verify in TrySelfRedirect. INFO backlog items closed: M2A-002, M2D-005, M1-003, M2B-003, M2B-004, M2B-005, M2C-002.

### Build Checks

- `dotnet restore ToastRevival.sln`: passed. New packages: `Azure.AI.ContentSafety 1.0.0` (API), `Otp.NET 1.4.0` (API), `System.Security.Cryptography.ProtectedData 8.0.0` (Agent).
- `dotnet build ToastRevival.sln -c Release`: **0 warnings, 0 errors** solution-wide post-FIX-M3-001 patch.
- `dotnet ef migrations add M3SecurityHardening`: migration generated correctly on second attempt (first run against stale DLL produced empty migration; deleted and regenerated). Migration contains `TenantBlocklistEntries` table, `Tenants.EnrollmentKey` column, composite `NotificationDeliveries (DeviceId, Status, CreatedAt)` index. ✓
- MSIX smoke check: 1 pre-existing FIX-MSIX-003 `mspdbcmf.exe` warning only. 0 errors. ✓

### Code Sweep — Pre-Commit FIX

- **FIX-M3-001** (BLOCKING → resolved before commit): WiX `<Launch Condition="VersionNT64 >= 1904">`. `VersionNT64` is `major*100+minor` — Windows 10/11 = 1000. `1000 >= 1904` evaluates false, which WiX interprets as the condition NOT being met → install blocked. Changed to `VersionNT64 >= 1000` (catches pre-Win10); runtime Program.cs provides the precise build-19041 floor.

### Code Sweep — INFO findings (non-blocking, filed)

- **INFO-M3-001**: TOTP replay within the 30s step accepted (no nonce cache). M8 hardening.
- **INFO-M3-002**: `BlocklistService` is concrete injection in `NotificationsController` — extract interface at M4.
- **INFO-M3-003**: `ContentSafetyService` logs to `Console.Error` — wire `ILogger` at M4.

### Architecture Verification

- **Moderation pipeline**: blocklist-first short-circuit → Azure text scan → Azure image scan → aggregate worst decision → Pass (enqueue) / Review (PendingReview status, not enqueued) / Block (422 Unprocessable Entity, not persisted). Code path audited; `AggregateModerationResults` correctly uses `>=` on `ModerationDecision` enum (Pass=0, Review=1, Block=2).
- **DPAPI ConfigStore**: `ProtectedData.Protect/Unprotect` at `DataProtectionScope.CurrentUser`. Existing plaintext `config.json` on upgrade: `Unprotect` throws `CryptographicException`, caught in `TryLoad`, returns null → re-registration triggered. No crash, correct migration path.
- **TrySelfRedirect Authenticode verify**: `X509Certificate2.CreateFromSignedFile` → subject contains "Toast2IT, LLC" → `WinVerifyTrust` → full chain validation. Any failure → `false` → no redirect. Correct safe default.
- **MFA JWT**: `CreateMfaToken` adds `mfa=true` claim, 15-min lifetime. Standard JWT signed with same key. Broadcast gate checks `User.FindFirstValue("mfa") == "true"` — simple string comparison, correct.
- **EnrollmentKey**: `FixedTimeEquals` on UTF-8 bytes — timing-safe comparison. Length mismatch → false without leakage.

### Boundaries

- Azure Content Safety integration NOT end-to-end verified — requires `ContentSafety:Endpoint` and `ContentSafety:Key` in environment. Graceful degradation (Pass-through) confirmed by code path review; unit test deferred to M8.
- TOTP enrollment/verification NOT runtime-verified — requires a running backend + authenticator app. Code path correctness confirmed by OtpNet documentation alignment. M8 integration test.
- DPAPI wrap NOT verified on a real agent install — requires signed MSI on a lab machine. Upgrade path (plaintext → encrypted on re-registration) is code-correct; lab verification is Keith's next MSI sign+test cycle.
- WiX LaunchCondition NOT verified on a real install (lab machine test is a Keith handoff for next MSI build).

## 2026-05-09 (M2.B — Missed catch-up + Orphan recovery + Agent dedup)

### Scope

M2.B closes D6 from the M2 plan plus the two M2.A INFO items it carried. Three structural pieces:

1. **Backend `GET /api/notifications/pending?since=<DateTime?>`** — device-JWT-authenticated, `device-per-hour` rate-limit, returns `PendingNotificationItem[]` of `(NotificationId, PayloadJson, Signature, CreatedAt)` for the device's `Pending` deliveries; cap 100 per call; ordered `CreatedAt` asc.
2. **Backend `NotificationQueueService.RecoverOrphansAsync`** — runs once at `ExecuteAsync` startup before the channel loop. Sweeps `Notifications WHERE Status=Sending AND SentAt < now()-5min` to `Failed`; pending deliveries left intact for catch-up.
3. **Agent `RunCatchupAsync` + `_renderedCache: MemoryCache<Guid, byte>`** — fires on `_hub.Reconnected` and once after cold `StartAsync`; verifies HMAC + dedups + renders + ReportDeliverys each pending item. Dedup window 1-hour sliding, shared between hub-push and catch-up paths.

Plus a structural improvement: `NotificationPayloadBuilder.BuildSigned` extracted as the single source of truth for the wire shape + signature. The hub fanout and the catch-up endpoint now sign byte-identical UTF-8 sequences via the same code path — M2.A standing rule #14 made into a structural guarantee, not a convention.

### Build Checks

- `dotnet build ToastRevival.sln`: passed with **0 warnings, 0 errors** (post-FIX-M2B-001 patch verify).
- MSIX smoke check (`dotnet build src\ToastRevival.Agent\ToastRevival.Agent.csproj -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=false -p:AppxPackageSigningEnabled=false -p:TargetPlatformVersion=10.0.22621.0`): passed with 0 warnings, 0 errors. (We didn't generate the .msix package on this run since no manifest changes; smoke check confirms the `WindowsPackageType=MSIX` build path still resolves cleanly with the new `Microsoft.Extensions.Caching.Memory` package reference.)
- No EF migration generated — pure code milestone, no schema changes.

### Runtime Smoke Checks

- **DiagnosticMode regression** (`dotnet run --project src/ToastRevival.Agent --no-build -- --template plain --no-wait`): N/A this session (no agent-mode dispatch changes; the catch-up wiring is scoped to PrimaryMode `AgentHubClient`).
- End-to-end runtime confirmation (real Postgres + API + agent + reconnect → catch-up GET → render → ReportDelivery) requires a lab run with a signed agent build talking to a running backend. Same hand-off pattern as M2.A — out of scope for the structural session.

### Code Sweep — Pre-Commit FIX

- **FIX-M2B-001** (BLOCKING → resolved before commit): `AgentClient.cs::AgentHubClient._lastCatchupSince` was initialized to `DateTime.UtcNow` at ctor. First catch-up GET would send `since=<ctor_time>`; server filter `delivery.CreatedAt >= since` would have excluded EVERY pre-existing Pending delivery — exactly the case M2.B was shipping to fix (agent rebooted, Pending from before the reboot, reconnects). **The catch-up endpoint would have returned zero results in its primary scenario.** Patched: `_lastCatchupSince` is now nullable `DateTime?` (default null); first call omits the `since` query param entirely; subsequent calls send the captured `nextSince` from the previous call. Build re-verified clean post-patch. Side benefit: no time-zone Kind=Unspecified hazard against `timestamptz` columns.

### Code Sweep — INFO findings (non-blocking, deferred)

- **INFO-M2B-002**: **RESOLVED 2026-05-10 (M9.B)**. `GET /api/notifications/pending` now accepts `?limit=<int>` query param, default 100 (backwards compat for v0.3.x agents that omit it), server-clamped to `[1, 500]` via `Math.Clamp`. Wire shape preserved (still `PendingNotificationItem[]`) — agents in the field need no rebuild. Integration test `SecurityTests.PendingEndpoint_LimitParamControlsPageSize_ClampsToBounds` verifies default + explicit-in-range + upper-clamp + lower-clamp against a real Postgres container with 510 seeded Pending deliveries. Agent-side adoption (`?limit=500` in `AgentClient.RunCatchupAsync`) deferred to next signed agent build (INFO-M9B-001 carry-forward).
- **INFO-M2B-003** (M3 / M5): No composite DB index on `NotificationDelivery (DeviceId, Status, CreatedAt)`. Catch-up query will scan once delivery volume grows. Add via EF model + migration when needed.
- **INFO-M2B-004** (M3): Agent dedup `MemoryCache` is unbounded (no `SizeLimit`). Acceptable at MVP scale (~100 bytes/entry); set `SizeLimit=50_000` + `Size=1` on entry options at M3.
- **INFO-M2B-005** (M3): Catch-up endpoint shares the `device-per-hour` (10/hr fixed) policy with `ReportInteraction`. Flaky-network reconnect storms could throttle catch-up. Fire-and-forget semantics mean a 429 just delays delivery to the next successful reconnect — acceptable for now. Consider a separate `device-catchup-per-hour` policy at e.g. 60/hr.

### New Standing Rules (Carl, M2.B)

1. **Orphan recovery semantic**: Sweep marks the notification `Failed` but leaves Pending deliveries untouched so the catch-up endpoint can still serve them. The original FIX-LIST plan ("deliveries to Failed accordingly") would have defeated catch-up entirely. State divergence (Failed notification with Delivered devices) is acceptable — the dashboard sees fanout-Failed while delivery counts trickle up.
2. **Single signed-payload source of truth**: any new path that emits a notification payload to an agent uses `NotificationPayloadBuilder.BuildSigned`. Never reimplement the JSON shape or the HMAC step inline — byte-deterministic equivalence between hub fanout and catch-up is structural, not coincidental.
3. **Catch-up `since` initialization rule**: any future catch-up endpoint that takes a `since` parameter must initialize the agent's tracking variable in a way that does NOT exclude pre-existing pending state on first run. Nullable + omit-on-first-call is the canonical pattern (FIX-M2B-001 lesson).

### Boundaries

- This confirms structural correctness: handler shape, auth boundary, dedup wiring, recovery semantic, byte-identical signing path between hub and catch-up.
- This does NOT confirm end-to-end runtime — pending live-server lab run.
- Test coverage gap (INFO-M1-004) inherited; first tests at M8.
