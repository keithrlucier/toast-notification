# M7.B Corporate Edition — Marketing Site Rebuild

**Date:** 2026-05-09
**Owner:** Anthony (build) + Diana (design pass) + Carl (scope) + Abish (preflight)
**Trigger:** Keith called the original M7.B render flat (`"Codex is going to look at redesigning this its ass"`); asked for a corporate marketing site, not "another VIBE coded saas site." Pricing model corrected ($0.22/device + 100-device floor + 14-day trial). Codex unavailable for ≥14 hours; backend rewrite handed off via `Docs/ToastRevival/CODEX-HANDOFF.md`.

## Scope shipped

**Pages rebuilt corporate-grade:**

- `/` — Home with: hero (real product screenshot of the composer + live preview), problem/solution comparison (msg.exe vs branded toast), capability matrix (4-card), **security architecture** section (8-control compliance grid), how-it-works (3-step), one-plan pricing summary with indicative-cost table, 14-day-trial final CTA.
- `/pricing` — Single Standard plan card ($0.22/device, $22/mo floor, 14-day trial), indicative-cost table by fleet size (100 → 5,000+ devices), 6-section inclusion grid (Notifications / Targeting / Deployment / Tracking & audit / Security / Branding & ops), 8-question FAQ accordion, procurement-friendly final CTA with `mailto:` for >5,000-device fleets.

**Removed from spec:**

- `HowWeBuiltIt.tsx` — DocPro/AI-built case study NEVER on public marketing surface (lives in `llms.txt` + internal `.md` docs only).
- `DocsComingSoon.tsx` — no placeholder pages ship; navigation entries don't reference unbuilt features.
- "M9 roadmap" / "publishes shortly" / "MOST POPULAR" / `(M9)` — every internal-milestone-code leak banished from copy.

**Routing changes:**

- `/` → `RootIndex` toggle: anon visitors render the marketing Home wrapped in `MarketingLayout`; authenticated users `<Navigate to="/dashboard" replace />`.
- `/pricing` → `Pricing` wrapped in `MarketingLayout`. Public route, even authed users can view.
- `/dashboard` → new path for the authenticated dashboard index. Layout/Sidebar untouched.
- All other dashboard routes (`/analytics`, `/devices`, `/templates`, `/compose`, `/assets`, `/history`, `/moderation`, `/users`, `/audit`, `/billing`, `/settings/*`, `/onboarding`) unchanged.
- Sidebar's existing `to="/"` Dashboard link resolves through the toggle without modification — Codex's in-flight Sidebar WIP is untouched and deploys preserve the live chrome.

**Real product screenshot:**

- Captured via Playwright from the live composer (smoke-test tenant: `Smoke Test MSP` / `smoke+20260509@toastnotification.test`) showing a populated "Maintenance window tonight" notification with live preview rendering in Segoe UI.
- Saved at `src/ToastRevival.Dashboard/public/screenshots/composer-hero.png` (1600×1000, 125 198 bytes).
- Hero img element uses the screenshot as LCP candidate (`loading="eager" decoding="async"`).

## Build evidence

```
> npm run build (with full WIP working tree included)
✓ 721 modules transformed.
dist/index.html                           0.78 kB │ gzip:   0.43 kB
dist/static/index-CPBLaXIl.css           10.27 kB │ gzip:   2.79 kB
dist/static/MarketingLayout-RAWf_d6h.css 20.85 kB │ gzip:   4.07 kB
dist/static/FeatureIcons-KwjAdMyO.js      1.53 kB │ gzip:   0.67 kB
dist/static/MarketingLayout-0eK87lfn.js   4.19 kB │ gzip:   1.51 kB
dist/static/Pricing-BMNa-WxG.js           9.79 kB │ gzip:   3.64 kB
dist/static/Home-DGojaGND.js             11.03 kB │ gzip:   3.51 kB
dist/static/index-7zXBsNuA.js           722.54 kB │ gzip: 212.70 kB
✓ built in 4.31s
```

Marketing chunks lazy-load and stay out of the dashboard hot path; the dashboard hot path stays out of the public marketing bundle (anon visitors don't pull Recharts / Compose / asset library code).

## Deploy evidence

- Tarball uploaded via scp to TOASTWEB1 (54.82.103.160).
- Backup at `/opt/toast/dashboard.bak.m7b` for immediate rollback.
- `chown toast:toast` matches the pre-existing live ownership (NOT `www-data:www-data` as in the older standing rule — adjusted live).
- Post-deploy curl checks (all 200, all bytes > 0):

```
GET https://toastnotification.com/                                       200, 782 bytes
GET https://toastnotification.com/static/index-7zXBsNuA.js               200, 722 540 bytes
GET https://toastnotification.com/pricing                                200, 782 bytes (SPA fallback)
GET https://toastnotification.com/login                                  200, 782 bytes (SPA fallback)
GET https://toastnotification.com/register                               200, 782 bytes (SPA fallback)
GET https://toastnotification.com/screenshots/composer-hero.png          200, 125 198 bytes
```

- Post-deploy Playwright: anon `/` renders marketing Home with title `Toast Notification — Managed Windows notifications for MSPs`, zero console errors. Authenticated `/` redirects to `/dashboard` with the existing live corporate chrome rendering. `/pricing` renders cleanly with title `Pricing — Toast Notification`, zero console errors.

## In-flight state preserved

A non-trivial 25-file WIP changeset was already in the working tree at session start (Codex's pre-sleep redesign of the dashboard chrome — `Layout.tsx`, `Sidebar.tsx`, `index.css +405`, full `Users.tsx` rewrite, 13 dashboard page modifications, plus shared `api/*.ts`, `AuthContext.tsx`, `BroadcastConfirmModal.tsx`, `StatusBadge.tsx`, `AuthController.cs`, `Program.cs`). All 25 files were preserved untouched. Tonight's build INCLUDED them so the deployed bundle continues to render the corporate dashboard chrome customers are already seeing live; tonight's git commit EXCLUDED them so Codex picks up exactly where it left off without merge conflict.

## Standing rules carried forward

- **Vite `build.assetsDir = 'static'`** — re-validated against the M5.C `/assets/` API proxy. No regression. FIX-PROD-001 standing check held.
- **Post-deploy curl verification** of emitted script-src — non-negotiable, executed cleanly.
- **No internal milestone codes in user-facing copy** — Code-Sweep grep against the diff would catch `M[0-9]+\.?[A-Z]?` / `roadmap` / `publishes shortly`. Corporate copy passes.
- **No third-party tracking scripts** — marketing site has zero. Verified.
- **No DocPro/AI-built story on public surfaces** — HowWeBuiltIt.tsx deleted; story stays in `llms.txt` + internal docs.
- **No placeholder pages** — DocsComingSoon.tsx deleted.
- **Real product screenshots only** — composer-hero.png captured from live composer, not stock or AI-generated.

## Lessons

- **The vibe-vs-corporate distinction is mostly in the COPY, not the chrome.** Diana's M7.A scaffolding was already corporate-grade (8px grid, restrained accent, dense typography, Bloomberg-style tables). The flatness was in the page bodies — three-tier card layout, "MOST POPULAR" eyebrow, generic feature copy, fake screenshot placeholders, "M9 roadmap" leaks. Tonight's body rewrite produced a corporate site without rewriting the chrome.
- **A single-plan pricing model removes a whole category of vibe-SaaS visual debt.** Three-tier card layouts read as Webflow templates. One plan + indicative-cost-by-fleet-size table reads as Bloomberg.
- **Real product screenshots replace lots of design fluff.** The hero went from empty-frame-with-"screenshot here"-label to actual composer + live preview rendering. That single change moves the page out of template territory.
- **Light-mode is more corporate than dark-mode for MSP audiences.** Datadog, Cloudflare, Atlassian, ServiceNow all default to light. Diana's prior dark-mode default deferred to whatever the deployed chrome was using.
- **Working tree archeology before any write.** Memory rule held: `git status` showed 25 unexpected modified files at session start — reading them before deciding to commit/preserve avoided clobbering Codex's in-flight redesign work.

## Open / handed-off

- **Backend pricing v2 implementation** — Codex track. Specs in `Docs/ToastRevival/CODEX-HANDOFF.md`.
- **PlatformAdmin role + cross-tenant `/api/system/*`** — Codex track. Specs in `Docs/ToastRevival/CODEX-HANDOFF.md`.
- **Admin dashboard UI redesign** — Codex track. Working-tree files preserve current state.
- **Smoke-test tenant cleanup** — `Smoke Test MSP` / `smoke+20260509@toastnotification.test` to be transactionally removed from prod DB after this evidence file lands. (Cleanup attempt logged inline if it falls back to next session.)
- **Docs hub (M7.C) and SEO/llms.txt/sitemap (M7.D)** — deferred. Not on tonight's critical path; Keith's directive was the public marketing site corporate rebuild only.
