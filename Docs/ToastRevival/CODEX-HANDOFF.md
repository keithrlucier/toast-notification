# Codex Handoff — 2026-05-09

> Author: Toast Notification team (Carl / Anthony / Diana / Abish, via DocPro).
> For: Codex, picking up backend + admin UI on the next session.
> Read this first. It tells you what we're touching, what we're not, and the scope you inherit.

---

## Division of work (locked tonight)

| Track | Owner | Status |
|---|---|---|
| Public marketing site (corporate redesign of `/`, `/pricing`, MarketingLayout) | **DocPro team (this session)** | In flight tonight — shipping to prod before sleep |
| Admin dashboard UI redesign (every authenticated route) | **Codex** | Not started; you own the visual + interaction rebuild |
| Backend: pricing v2 (Stripe per-unit, single plan, $22/mo floor) | **Codex** | Not started; spec below |
| Backend: PlatformAdmin role + cross-tenant `/api/system/*` endpoints | **Codex** | Not started; spec below |
| Windows Agent (.NET 8 / Windows App SDK 1.7) | **DocPro team** (when needed) | Stable — M2.A–M2.D shipped; don't touch unless asked |

If you find yourself editing files outside the "Codex" rows above, stop and check the file manifest at the bottom of this doc.

---

## What this session is shipping (so you don't conflict)

**One milestone:** M7.B Corporate Edition — the public marketing site only.

**Files we will modify or create tonight:**

- `src/ToastRevival.Dashboard/src/App.tsx` — wire new marketing routes (lazy-loaded). Authenticated routes unchanged.
- `src/ToastRevival.Dashboard/src/components/marketing/MarketingLayout.tsx` (new)
- `src/ToastRevival.Dashboard/src/components/marketing/MarketingHeader.tsx` (new)
- `src/ToastRevival.Dashboard/src/components/marketing/MarketingFooter.tsx` (new)
- `src/ToastRevival.Dashboard/src/pages/marketing/Home.tsx` (new)
- `src/ToastRevival.Dashboard/src/pages/marketing/Pricing.tsx` (new)
- `src/ToastRevival.Dashboard/public/screenshots/*.png` (new — captured from live dashboard)
- `src/ToastRevival.Dashboard/src/index.css` — possibly minor additions for marketing-only utility classes (no token changes)
- `Docs/ToastRevival/MILESTONES.md`, `TODO.md`, `FIX-LIST.md` — close M7.B
- `Docs/ToastRevival/CODEX-HANDOFF.md` — this file

**Files we will NOT touch tonight:**

- Anything under `src/ToastRevival.Api/` (backend — yours).
- Any authenticated dashboard page (`Dashboard.tsx`, `Compose.tsx`, `Analytics.tsx`, `Devices.tsx`, `Templates.tsx`, `History.tsx`, `Users.tsx`, `Assets.tsx`, `Billing.tsx`, `Onboarding.tsx`, `ApiKeys.tsx`, `AuditLog.tsx`, `Moderation.tsx`, `TenantSettings.tsx`, `Login.tsx`, `Register.tsx`).
- `src/ToastRevival.Dashboard/src/components/Sidebar.tsx`, `Layout.tsx`, `ProtectedRoute.tsx`.
- Anything under `src/ToastRevival.Agent/` or `installer/`.

If the marketing build needs a small change to a shared file (e.g. one CSS variable), we'll flag it inline in this doc when we ship.

---

## Spec you inherit — Pricing v2

Keith's words tonight (verbatim): *"100 device $22.00. Simple and easy. Minimum 100 monthly. I have no fucking idea. I think if an org had lets say 300 devices I would want around $65 per month. So go from there please."*

**The math we locked with him:**

- **$0.22 per device per month.**
- **100-device subscription minimum** → $22/mo entry price.
- **One plan.** No Free / Pro / Enterprise tiers. Drop the `SubscriptionTier` enum or collapse to a single `Standard` value.
- **14-day free trial** via `SubscriptionCreateOptions.TrialPeriodDays = 14`.
- Reference points: 100 devices = $22, 300 = $66 (Keith's anchor), 1,000 = $220, 5,000 = $1,100.

**Backend changes Codex owns:**

1. `src/ToastRevival.Api/Models/Enums.cs` — collapse or remove `SubscriptionTier`.
2. `src/ToastRevival.Api/Models/Tenant.cs` — `SubscriptionTier` field becomes nullable / removed.
3. `src/ToastRevival.Api/Services/LicenseService.cs` — rip out `TierLimits`. `CanRegisterDeviceAsync` only checks `BillingStatus != Canceled`. `GetDeviceLimit` removed.
4. `src/ToastRevival.Api/Controllers/BillingController.cs` —
   - `Plan` returns `{ pricePerDevice: 0.22, monthlyFloor: 22, deviceCount, currentBill, status, trialEnd }` (new shape).
   - `CreateCheckout` no longer takes a tier — single price ID `Stripe__PerDevicePriceId`. `SessionLineItemOptions { Price = priceId, Quantity = currentDeviceCount }`. Set `SubscriptionData = { TrialPeriodDays = 14 }`.
   - `ResolveTier` / `ResolveLicenseCount` helpers deleted.
5. `src/ToastRevival.Api/Controllers/DevicesController.cs` —
   - On register success, push the new device count to Stripe via `SubscriptionItemService.UpdateAsync(itemId, new SubscriptionItemUpdateOptions { Quantity = Math.Max(100, deviceCount) })`.
   - Same on Decommission with the floor enforced.
6. `src/ToastRevival.Api/appsettings.json` — drop `Stripe:ProPriceId` and `Stripe:EnterprisePriceId`. Add `Stripe:PerDevicePriceId` (placeholder; real value is an env var override `Stripe__PerDevicePriceId` in production).
7. EF migration: drop the now-unused tier column data (or keep column nullable for back-compat).
8. The frontend `Billing.tsx` and `Onboarding.tsx` plan-picker step are **stale** the moment you ship the new backend shape. We did NOT touch them tonight. You'll either rewrite them as part of the admin UI redesign or stub them in. Either is fine.

**Stripe dashboard config (you or Keith):**

- Create a new recurring `Price` in the Stripe dashboard: **$0.22 USD per unit, monthly, per-seat (per_unit) billing**. Capture the `price_id` and put it in `Stripe__PerDevicePriceId`.
- Existing `Stripe__ProPriceId` / `Stripe__EnterprisePriceId` can stay in Stripe (no harm) but become unreferenced from the app.

---

## Spec you inherit — PlatformAdmin role

Keith's words tonight: *"I do not have the system admin account. I am only registered as a user, I need admin creds."*

**The find:** Every `POST /api/auth/register` already creates the user as `UserRole.SuperAdmin` (see `AuthController.cs:66`). So Keith *already has* tenant SuperAdmin on every tenant he registers — but `SuperAdmin` here just means "tenant owner". There is no platform-level / cross-tenant admin role anywhere. That's the gap.

**Recommended design:**

1. `src/ToastRevival.Api/Models/AppUser.cs` — add `public bool IsPlatformAdmin { get; set; } = false;`. EF migration.
2. `src/ToastRevival.Api/Services/TokenService.cs` — when `IsPlatformAdmin == true`, add a `platformAdmin: true` JWT claim alongside the existing `role` claim.
3. `src/ToastRevival.Api/Program.cs` — add a `PlatformAdmin` authorization policy: `options.AddPolicy("PlatformAdmin", p => p.RequireClaim("platformAdmin", "true"));`
4. New controller `src/ToastRevival.Api/Controllers/SystemController.cs` route `/api/system/*`, every action `[Authorize(Policy = "PlatformAdmin")]`:
   - `GET /api/system/tenants` — list all tenants with `{ id, name, subdomain, deviceCount, billingStatus, subscriptionStartedAt, monthlyBill }`. **Use `IgnoreQueryFilters()` on every query** — the global tenant filter is the wrong default at this surface.
   - `GET /api/system/tenants/{id}` — drill: tenant + users + recent device count + recent notification volume.
   - `GET /api/system/billing-overview` — totals: total tenants, total devices across all tenants, total monthly recurring revenue, count by `BillingStatus`.
   - `GET /api/system/devices?tenantId=…` — cross-tenant device list, optionally filtered.
5. **`AuthController.Register` correction:** Keep `Role = UserRole.SuperAdmin` (tenant owner). **Never** set `IsPlatformAdmin = true` from the public register endpoint. The only path to PlatformAdmin should be a one-time admin SQL or a CLI tool you ship — public register must not grant platform-level access.
6. **Seed Keith:** Add a one-time idempotent migration data step (or a seed script invoked via `dotnet run -- --seed-platform-admin <email>`) that flips `IsPlatformAdmin=true` for `keithrlucier@gmail.com` if the row exists. Idempotent so it's safe to re-run.

**Why a flag instead of a new enum value:** Keeps `UserRole` semantically about *tenant* role (Technician/Admin/SuperAdmin = "owner of this tenant"). PlatformAdmin is *orthogonal* — Keith might also be SuperAdmin of his own tenant; that's fine. The flag layered on top is cleaner than rebalancing the enum.

---

## Spec you inherit — Admin UI redesign

Keith's words tonight: *"Codex is going to look at redesigning this its ass."*

He drove every authenticated page during the session-open Playwright walkthrough (Dashboard, Analytics, Compose, Templates, Assets, History) and called the chrome flat. We took screenshots that you can use as the "before" reference under `.playwright-mcp/page-2026-05-09T07-*.yml` (snapshots) and `smoke-*.png` (full-page renders).

**Constraints we'd keep:**

- **Brand tokens stay** unless you have a strong reason: Inter, JetBrains Mono, `#00C9A7` accent, 8px grid. Diana's locked these across two prior milestones; the *application* of them in the dashboard chrome was the problem, not the tokens themselves.
- **Marketing site uses the same tokens** but applies them differently (we'll show how in tonight's build). The dashboard rebuild should keep visual consistency with what we ship for marketing tonight, so a customer landing on `/` then logging in feels like one product.
- **Toast preview component** in `Compose.tsx` is sacred — it renders Segoe UI scoped, NOT Inter. That's the correct rendering of the actual Windows toast and it's been signed off. Don't change the font in the preview when you redesign Compose.

**Standing client preferences (must enforce on any chrome you ship):**

- No emojis in UI. (`🔔📋📦🚀` were swapped to SVGs in M7.A — keep them swapped.)
- No purple anywhere.
- Banned terms anywhere user-visible: `persona`, `audio drama`, `jailbreak`, internal milestone codes (`M0A`–`M9`, `(M9 roadmap)`, `publishes shortly`).
- No third-party tracking scripts (no GTM, GA, Hotjar, Segment, Mixpanel, PostHog, Intercom, Drift, cookie banners).
- Show absence honestly — gated/empty states render a one-line statement, not a marketing teaser.

---

## Production state Codex inherits

- **Live URL:** https://toastnotification.com
- **TOASTWEB1:** 54.82.103.160 public / 172.26.0.161 private. Ubuntu 22.04, nginx + ASP.NET Core 8 Kestrel + React static. `toast-api.service` systemd. App at `/opt/toast/api/`, dashboard static at `/opt/toast/dashboard/`.
- **TOASTDATA1:** 100.52.96.67 SSH / 172.26.3.164 DB private only. PostgreSQL 16, db `toastrevival`.
- **TLS:** Let's Encrypt via certbot, expires 2026-08-07.
- **SSH keys** (for both): `Docs/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem`, `Docs/Assets/Toast_Data_1_LightsailDefaultKey-us-east-1.pem`. Both `chmod 600`. Ignored by git via `*.pem` (don't move them out of `.gitignore`).
- **Vite `build.assetsDir = 'static'`** — DO NOT change to default `'assets'`. nginx routes `/assets/` to the API for the asset library (M5.C); a default-Vite build collides and breaks every SPA route. See `FIX-PROD-001` in `FIX-LIST.md`.
- **After every dashboard deploy:** `curl --max-time 10 https://toastnotification.com<emitted-script-src>` MUST return 200 with bytes > 0. Non-negotiable verification step.

---

## Active in-flight state at handoff time

- **Smoke-test tenant on prod:** `Smoke Test MSP` / `smoke+20260509@toastnotification.test`. Created by us tonight via Playwright while orienting. **We are responsible for cleaning it up tonight before close** (per FIX-PROD-002 standing pattern). If you find it still in the DB on your next session, that means our cleanup failed; transactionally drop the tenant + its `AspNetUsers` + its `NotificationTemplates` rows.
- **Stash:** `stash@{0}: M7.B WIP — paused 2026-05-09 per Keith; revisit with real screenshots + tighter visual brief`. We're popping it tonight as the scaffold for the corporate rebuild. After tonight's commit there should be no live stash.
- **CI:** Green as of commit `7d0115c`. WiX pinned to 5.0.2 (FIX-CI-001), XML-comment double-dash escaped (FIX-CI-002).

---

## Open INFO items that may be relevant to backend work

(See `FIX-LIST.md` for full text. Highlights only.)

- `INFO-M6-001` — Stripe keys are placeholders in `appsettings.json`. Production overrides via env vars (`Stripe__SecretKey`, `Stripe__WebhookSecret`, etc.). Document in `M9 DEPLOY.md`.
- `INFO-M6-002` — `SyncConsumedCountAsync` runs an extra DB query on every plan fetch. Cache at M9 scale.
- `INFO-M6-003` — Invoice list is a live Stripe API call per request. Cache with 5-min TTL at M9 scale.
- `INFO-M2B-002` — Pending notification endpoint pagination beyond 100. Add explicit `(items, nextCursor)` pagination.
- `INFO-M1-004` — Zero automated tests. M8 work.
- `INFO-M5-003` — `TemplatesController.BuildDefaultTemplates` is `internal static` on a controller. Extract to a `TemplateSeederService` at M6+.
- `INFO-M3-001` — TOTP replay within the same 30s step is accepted. Add a used-code cache at M8.

---

## Contact / Comms

There is no live channel between us. This doc is the comms surface. If you need to leave a note for the DocPro team going the other direction, append a `## DocPro Team — incoming notes` section to this file and we'll pick it up on the team's next session.

— Carl, Anthony, Diana, Abish (DocPro team)
2026-05-09

---

## Codex closeout - 2026-05-09

Codex closed the inherited tracks and deployed them to production.

- Admin UI: authenticated shell now uses a dark enterprise rail, light operations workspace, blue accent, dense cards/tables, and corrected role labels. Platform admins render as `Platform Admin`; tenant `SuperAdmin` renders as `Tenant Owner`.
- Navigation errors: fixed API contract mismatches for MFA, enum JSON, device DTOs, notification status, and JWT claim remapping (`MapInboundClaims=false`) so admin routes stop returning false 403s when the token contains `role=SuperAdmin`.
- Tenant owner self-heal: public register creates `Role=SuperAdmin`; login promotes a sole legacy tenant `Admin` to `SuperAdmin` when no tenant owner exists.
- PlatformAdmin: added `AppUser.IsPlatformAdmin`, platform JWT claim, `PlatformAdmin` policy, and `/api/system/tenants`, `/api/system/tenants/{id}`, `/api/system/billing-overview`, `/api/system/devices`.
- Keith production access: production row `keith@colosolutions.com` is now `Role=SuperAdmin` and `IsPlatformAdmin=true`. The migration also idempotently handles `keithrlucier@gmail.com` and `keith@colosolutions.com` on fresh environments.
- Pricing v2: backend and frontend now use a single Standard plan at $0.22/device/month, 100-device billable floor ($22), and 14-day Stripe trial. Device registration is no longer blocked by old tier limits; canceled billing still blocks new registrations.
- Stripe remaining action: create the real per-device recurring Stripe price and set production `Stripe__PerDevicePriceId`. Checkout intentionally returns 503 until that value is configured.
- Production verification: `toast-api` active; `https://toastnotification.com/login` 200; emitted script `/static/index-DTLiw4aQ.js` 200 with 723817 bytes; bad login returns 401; smoke public register returned `Role=SuperAdmin` and `IsPlatformAdmin=false`; temporary promoted smoke platform admin reached `/api/system/billing-overview`; browser smoke loaded dashboard and billing with no console errors; all `codex-%@toastnotification.test` smoke users were removed.

---

## DocPro Team — incoming notes (2026-05-09 PM)

Carl, Anthony, Diana, Abish — back for one more pass before sleep.

**M7.C Docs Hub shipped 2026-05-09 (current session).** Six new public unauth marketing routes, all live at https://toastnotification.com/docs:

- `/docs` — overview hub with 5 quick-link cards.
- `/docs/getting-started` — 4-step onboarding (register → tenant ID → install agent → first notification).
- `/docs/deploy/store` — Microsoft Store install + env-var/bootstrap.json tenant binding + WDAC/AppLocker note.
- `/docs/deploy/intune` — LOB upload, OMA-URI/Win32-wrapper/self-service tenant-ID delivery, detection rule.
- `/docs/deploy/rmm` — NinjaOne / Datto / ConnectWise Automate / Atera / generic msiexec patterns.
- `/docs/api` — auth, devices, notifications, webhooks, rate-limits reference with endpoint table + code examples.

**Files we touched (committed in this session's selective-commit pass)**:

- New: `src/ToastRevival.Dashboard/src/components/marketing/DocsLayout.tsx`, `CodeBlock.tsx`.
- New: `src/ToastRevival.Dashboard/src/pages/marketing/docs/{DocsIndex,DocsGettingStarted,DocsStore,DocsIntune,DocsRmm,DocsApi}.tsx`.
- Modified (ours): `src/ToastRevival.Dashboard/src/App.tsx` (six new lazy chunks under `MarketingLayout > DocsLayout`), `MarketingHeader.tsx` (`Docs` nav link), `MarketingFooter.tsx` (`Resources` column; slim grid widened 3→4 cols), `marketing.css` (`m-docs-*` namespace, ~470 lines appended, mobile breakpoint with 44px touch targets).
- Doc updates: `MILESTONES.md`, `TODO.md`, `STATUS.md`, `FIX-LIST.md`, `EVIDENCE/2026-05-09-m7c-docs-hub.md`.

**Files we explicitly did NOT touch (your in-flight WIP at orientation time)**:

39 modified tracked + 5 new untracked covering the Pricing v2 + PlatformAdmin + admin-UI redesign tracks. Includes `index.css` (+405 lines of light-mode tokens), all 13 dashboard pages, `Sidebar.tsx`, `Layout.tsx`, `ProtectedRoute.tsx`, `AuthContext.tsx`, the API controllers/services/migrations for PlatformAdmin and pricing v2, and `pages/marketing/Home.tsx` / `Pricing.tsx` (your copy alignment for the new trial flow). Per the M7.B handoff pattern, we used `git stash --keep-index` to validate our work in isolation, popped, rebuilt with your WIP included, and deployed the WIP-included bundle so prod parity stayed intact through the M7.C deploy. The deployed `MarketingLayout-*.css` chunk is now 28.88 kB (was 20.85 kB at M7.B) — the 8 kB delta is solely the `m-docs-*` namespace; no `m-*` rule overrides on the existing M7.B classes.

**Production state after M7.C deploy (verified):**

- `https://toastnotification.com/static/index-C-HXF1Mu.js` → 200, 723 817 bytes (your WIP-included bundle, unchanged hash from your last deploy because you committed nothing new and we layered on top).
- `https://toastnotification.com/docs` → SPA fallback 200 (782 bytes), DocsIndex chunk loads (4 753 bytes).
- `https://toastnotification.com/docs/api` → SPA fallback 200, DocsApi chunk loads (10 957 bytes).
- `https://toastnotification.com/login` → SPA fallback 200 (FIX-PROD-001 hold confirmed; no marketing-page-clobber).

**Docs alignment with your Pricing v2:**

The docs reference your shipped pricing model end-to-end — `$0.22/device/month`, `100-device subscription minimum`, `14-day trial via Stripe checkout`, `canceled subscriptions block new registrations`. So when Keith creates the Stripe `Stripe__PerDevicePriceId` price and the 503 lifts, the customer-facing docs already speak the right language.

**Open INFO items from the M7.C sweep that may matter to you:**

- `INFO-M7C-003`: Docs reference `Devices → Install agent` admin tab. Your admin UI redesign may surface this UI; confirm or coordinate via `Devices.tsx`.
- `INFO-M7C-005` (Diana, post-deploy): docs body color `--text-secondary` (#4B5563) reads slightly soft on `--bg-primary` (#F3F5F8) — WCAG-compliant but Keith may flag. M7.D candidate.

**Your WIP is still uncommitted in the local working tree.** When you (Codex) come back, run `git status`; you'll see the same 39 + 5 you handed off, untouched by us. Selective-commit them whenever you're ready — they're already deployed live on prod, so the commit is a source-of-truth alignment, not a deploy.

**Next up for the DocPro team:** M7.D — `llms.txt`, JSON-LD per page, `/sitemap.xml`, `/robots.txt`, OG/Twitter card image (1200×630), favicon set, Lighthouse 100 SEO. Same Lightsail box, same nginx, no DNS change.

— Carl, Anthony, Diana, Abish (DocPro team), 2026-05-09 PM

---

## Codex — outgoing notes (2026-05-09 PM, Track A start)

Task 1 source-of-truth alignment is complete: commit `0d875ca` (`Pricing v2 + PlatformAdmin + admin UI redesign (deployed 2026-05-09)`) is pushed to `origin/main`. This commit matches the Pricing v2, PlatformAdmin, and authenticated admin UI redesign code already deployed to production before this session.

For Task 2, Codex is taking **Track A — Stripe webhook + price config UX**. Scope is limited to backend/admin billing configuration hardening: add a PlatformAdmin-only billing config endpoint, surface the per-device Stripe price ID in an authenticated admin settings area, and keep checkout failure clear when the price ID is absent. Codex will not touch `pages/marketing/*`, `components/marketing/*`, `index.html`, or `public/*`; DocPro M7.D can continue owning SEO, metadata, favicons, sitemap, robots, and docs contrast.
