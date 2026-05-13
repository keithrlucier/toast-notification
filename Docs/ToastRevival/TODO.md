# Toast Notification — Open Items

**Last updated: 2026-05-12 (session 4)**

## Production status

Live at https://toastnotification.com — TOASTWEB1 (54.82.103.160) + TOASTDATA1 (172.26.3.164 private).

All milestones M0A–M8.C complete. M9.A (Mailjet + ClickSend registration flow) complete.
M9.B (pending endpoint pagination — INFO-M2B-002) complete 2026-05-10.
M9.C (enrollment-key auto-gen + agent drain loop + production tray/Store assets) complete 2026-05-10.
Stripe live: `price_1TVXJYIbddaRrnMlw8K4LrKr`, webhook `we_1TVXJwIbddaRrnMllPMIXamd`.
Public trial access is now review-gated. Direct tenant creation from public registration is disabled by default outside test config.
Free tier: 1–25 devices free, no Stripe required. 26+ requires active subscription.

---

## Engineering backlog

### Medium

- [x] **Tenant owner MSI install surface** - Resolved 2026-05-12. Added `/devices/install` for tenant admins with tenant ID, server URL, MSI download URL, and the prefilled `msiexec` command using the tenant enrollment key. Devices and onboarding now link directly to the install surface.

- [x] **Reviewed trial requests** - Resolved 2026-05-12. Public registration now stores a `TrialRequest` with company/contact/use-case details and Turnstile verification instead of immediately creating a tenant. Platform admins review at `/system/trial-requests`; approval provisions the tenant owner and sends the password setup email.

- [x] **Marketing, SEO, and LLM content refresh** - Resolved 2026-05-12. Home, pricing, public docs, SEO metadata, JSON-LD, `llms.txt`, and prerendered SEO copy now describe realistic MSP/software use cases, deployment paths, reviewed access, and current block pricing.

- [x] **Onboarding billing step** — Fixed 2026-05-10. Free tier language: "1–25 devices always free, billing starts at device 26+." Continue is primary CTA. Deployed.

- [x] **Velopack update feed** — Live since 2026-05-10. Feed at `https://releases.toastnotification.com/agent/win-x64/` serves `releases.win.json`, full+delta nupkg packages, and a signed `ToastNotification.Agent-win-Setup.exe`. **Important architectural note** (added 2026-05-12): MSI-installed agents do NOT auto-update via this feed — UpdateService.cs returns early when `mgr.IsInstalled == false`, by design for MSP/RMM scenarios. The feed is for the Velopack-managed install channel (download Setup.exe, runs from `%LOCALAPPDATA%\ToastNotification.Agent\current\`). For MSI deployments, update = redistribute new MSI via RMM. **Standing rule** (added 2026-05-12 after gap discovered): every signed-MSI ship MUST also run `.\scripts\build-release.ps1 -Version X.Y.Z`, sign the resulting `Setup.exe` via `.\scripts\sign-msix.ps1`, and scp the artifacts to `/opt/toast/releases/agent/win-x64/` in the same session. **Gap closed 2026-05-12**: 0.4.1 through 0.4.5 MSIs shipped without being published to the feed, leaving Velopack-channel agents stuck at 0.4.0. Backfilled 0.4.6 to feed (commit 0cb9659, signed Setup.exe SHA256 `7fa6401a823d6534bb9b06884fd5c08916fa187bf2aa1e24cfc5cf0d2bbd06e1`).

### Low / polish

- [x] **DiagLog rotation + --diag gate** (INFO-MSIX-004) — **RESOLVED 2026-05-10.** `DiagLog.Write()` now calls `RotateIfNeeded()` — rolls `agent.log` → `agent.log.1` at 512 KB, keeps two generations. New `--diag` flag handler in `AgentEntryPoint.RunAsync` (dispatched before the elevation check) prints log path, file size, and last 200 lines to stdout. `DiagMode` class in `Program.cs`. Ships next signed agent build.

- [x] **INFO-M8C-001** — **RESOLVED 2026-05-10.** `TenantIsolation_HubDeviceConnectedEvent_DoesNotLeakAcrossTenantGroups` predicate-poll: 20ms tick, 300ms timeout. Exits early if the leaked event arrives (fail fast), otherwise drains the full window before asserting `DoesNotContain`.

- [x] **INFO-M7C-003** — **RESOLVED 2026-05-10.** Docs route paths extracted to `src/routes/docsRoutes.ts` (`DOCS_PATHS` constant). Both `App.tsx` (router) and `DocsLayout.tsx` (nav sidebar) import from it — single source of truth, nav and routes can no longer drift.

- [x] **INFO-M2B-002** — Resolved 2026-05-10 (M9.B). `GET /api/notifications/pending` now accepts `?limit=<int>` query param, default 100 (backwards compat for v0.3.x agents), server-clamped to [1, 500]. Wire shape preserved (still array). Agent-side adoption of `limit=500` shipped as source change in M9.C (INFO-M9B-001) — ships next signed agent build.

- [x] **INFO-M2B-003** — Resolved (already shipped, doc fix M9.C). Composite DB index `(DeviceId, Status, CreatedAt)` was added in migration `20260509024211_M3SecurityHardening`.

- [x] **INFO-M1-003** — Resolved 2026-05-10 (M9.C, forward-only). New tenants auto-receive a 24-byte base64 `EnrollmentKey`; existing 3 prod tenants backfilled via psql + pgcrypto. Surfaced to admins on `/api/tenant/settings`; `POST /api/tenant/enrollment-key/regenerate` rotates. `DeployCommand.tsx` includes `ENROLLMENTKEY=<key>` in the msiexec command. Agent-side ENROLLMENTKEY plumbing was already in place (BootstrapConfig + WiX); ships next signed agent build.

- [x] **INFO-M9B-001** — Resolved 2026-05-10 (M9.C, source-only). `AgentClient.RunCatchupAsync` passes `&limit=500` and loops until partial page (MaxLoops=64 guard, +1 tick `since` advance). Ships next signed agent build.

- [x] **Azure Content Safety env config** — Resolved 2026-05-10 (M9.C). `ContentSafety__Endpoint` + `ContentSafety__Key` confirmed present in `/opt/toast/.env` on TOASTWEB1.

- [x] **Tray icon SVGs** (Diana) — Resolved 2026-05-10 (M9.C). Production bell glyph in `TrayIconService.CreateBellIcon` replaces M0A's circles. 5 state colors preserved; Disconnected + Error get diagonal slash. Same path data as Store tiles. SVG canon: `Docs/ToastRevival/Design/sources/tray-icons-and-tiles.svg`.

- [x] **Microsoft Store tile assets** — Resolved 2026-05-10 (M9.C). Production brand: near-black `#0A0F1A` panel, brand-amber `#F59E0B` bell, two-line wordmark on Wide310. PNGs at `src/ToastRevival.Agent/Images/{Square44,Square150,Wide310x150,StoreLogo}.png`. Renderer at `scripts/generate-msix-tile-assets.ps1`.

---

## Engineering backlog (session 4 — 2026-05-11)

- [x] **SMS MFA at login** — SHIPPED 2026-05-11 (commit ad8604c). Login issues ClickSend OTP when PhoneNumberConfirmed==true. POST /api/auth/login/verify-sms completes flow. Rate limiting: login-per-ip (10/5min) + login-sms-per-ip (5/15min). Login.tsx 2-step UI.

- [x] **Mailjet + ClickSend config in platform admin** — SHIPPED 2026-05-11 (commit ad8604c). GET/POST /api/system/messaging/config (PlatformAdmin). Billing page Messaging Configuration section. Masked display + live-reload same as Stripe.

- [ ] **INFO-RATELIMIT-001** — UseForwardedHeaders not configured. Rate limit collapses under Cloudflare/proxy. Add `builder.Services.Configure<ForwardedHeadersOptions>` + `app.UseForwardedHeaders()` before adding Cloudflare to the stack.

---

---

## Engineering backlog (M11 — Self-Hosted Distribution, opened 2026-05-12)

- [x] **Docker Compose infrastructure** — SHIPPED 2026-05-12 (commit `eff6c6c`). API + Dashboard + Postgres containers. Billing disabled (empty Stripe keys). Turnstile/ContentSafety gracefully degrade. Named volumes for DB data and uploaded assets. `.env.example` documents all config keys. `README-SELF-HOST.md` is the self-hoster entry point.

- [x] **LicenseService billing bypass** (INFO-M11-001) — RESOLVED 2026-05-12. `TOAST_REQUIRE_BILLING=false` short-circuits device cap. Commit `76ae2ef`.
- [x] **2-device trial cap** (INFO-M11-003) — RESOLVED 2026-05-12. `BillingPlanRules.TrialDeviceLimit = 2`. Commit `76ae2ef`.
- [x] **`DISABLEAUTOUPDATE` MSI property** (INFO-M11-002) — RESOLVED 2026-05-12. WiX + registry key. Commit `76ae2ef`.
- [x] **Git history sanitization** (INFO-M11-004) — RESOLVED 2026-05-12. CLEAN.
- [x] **Public GitHub repo** — LIVE 2026-05-12. https://github.com/keithrlucier/toast-notification
- [x] **Tenant sidebar branding + dynamic favicon** — SHIPPED 2026-05-12. Commit `a5b9a8a`.
- [x] **Logo auto-inject removed** — SHIPPED 2026-05-12. Blank Logo URL respected as no-logo. Commit `eb388b0`.

- [ ] **Marketing site pricing page rewrite** (M11.D7) — Three-tier honest copy. Diana leads. New DocPro session.

- [ ] **TOCTOU concurrent trial registration** (INFO-M11-SW-001) — Concurrent register calls can exceed 2-device cap. `pg_try_advisory_xact_lock` fix needed before SaaS scale. Not blocking self-host launch.

- [ ] **Platform admin nav gap** — Platform admins have only "Trial Requests" in PLATFORM_ITEMS. No cross-tenant Moderation, Users, Audit Log equivalents yet. Future session.

---

## Keith actions

- [x] **Configure production Turnstile keys** - SHIPPED 2026-05-12. `Turnstile__SiteKey` / `Turnstile__SecretKey` / `Turnstile__Required=true` plus `Registration__ReviewEmail` / `Registration__TrialDays=14` / `Registration__AllowLegacyDirectRegister=false` set on TOASTWEB1 `/opt/toast/.env`. Pre-change backup at `/opt/toast/.env.bak.2026-05-12-pre-m10`. Cloudflare siteverify reachable from prod (fake-token smoke returns 400 "Human verification failed").

- [x] **Run production EF migration** - SHIPPED 2026-05-12. `M10TrialApprovalGate` applied automatically on service restart (Migrate() runs on startup, idempotent). `TrialRequests` table + indices present in toastrevival DB.

- [x] **Re-sign MSI** — Signed (Keith confirmed 2026-05-11). Current binary is `ToastNotification.Agent-0.4.0.0.msi`.

- [ ] **Windows E2E verification** — Store install → register (SMS MFA now active) → receive notification → interact → verify DB delivery row. Repeat for MSI path with new ENROLLMENTKEY in msiexec command. Requires Keith's lab machine + signed package.

---

## Closed this session (2026-05-10)

- [x] **INFO-MSIX-004** (session 3) — DiagLog 512KB rotation + `--diag` stdout dump. `DiagMode` class, `RotateIfNeeded()` in `DiagLog`. Ships next signed agent build.
- [x] **INFO-M8C-001** (session 3) — SecurityTests hub isolation test: `Task.Delay(500ms)` → 20ms predicate-poll, 300ms timeout.
- [x] **INFO-M7C-003** (session 3) — Docs route paths centralized to `src/routes/docsRoutes.ts`; App.tsx + DocsLayout.tsx both import `DOCS_PATHS`.
- [x] **INFO-M9C-002** (session 3) — `DeployCommand.tsx` enrollment-key fetch module-level cached; `/api/tenant/settings` fires at most once per page load.
- [x] **Home page redesign** (session 3) — technical hero (copy-left / code-block-right), terminal msg.exe comparison, bento grid platform architecture, accurate pricing cards (free 1–25 / standard $22/mo flat 26–100 / growth $44/mo flat 101–200 / enterprise 200+). No fake stats, no trial claim. `bento.css` new file. Deployed 2026-05-10.
- [x] M9.C — enrollment-key auto-gen on new tenants + regenerate endpoint + DeployCommand surface (closes INFO-M1-003 forward-only); agent multi-page drain loop with `limit=500` (closes INFO-M9B-001 source-only); production bell tray icons + Store tile assets (closes Diana M9 GA blockers); INFO-M2B-003 already-shipped doc fix; Azure Content Safety env confirmation
- [x] M9.B pending endpoint pagination — `?limit=<int>` query param, default 100, clamp [1, 500], wire shape preserved, integration test (510-row Postgres seed) shipped with the change
- [x] M9.A registration flow — ClickSend SMS verify → Mailjet magic token email → set password → logged in
- [x] Marketing site full redesign — dark theme, honest copy, no fake stats, free tier messaging
- [x] Security posture page `/security` — real architecture, pen-test disclosure, logging policy
- [x] Free tier billing enforcement — devices 1–25 always allowed, 26+ requires Stripe subscription
- [x] Forgot password + reset password pages — self-service flow end to end
- [x] DeployCommand — msiexec with pre-filled CLIENTID on Dashboard and Devices pages
- [x] MSI hosted at `/downloads/ToastNotification.msi`
- [x] Stripe fully configured — secret key, `price_1TVXJYIbddaRrnMlw8K4LrKr`, webhook, success/cancel URLs
- [x] Stripe config panel in superadmin Billing page — full self-service, no SSH required
- [x] Duplicate Stripe prices archived (Keith)
- [x] AWS Windows VM (52.21.249.120) terminated (Keith)
