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
