# M7.D — SEO + LLM-Discovery Layer

**Date:** 2026-05-09 (evening)
**Slice:** M7.D — D5 (SEO + JSON-LD + sitemap + llms.txt + favicon set + OG card + INFO-M7C-005 contrast tweak)
**Owner:** DocPro team (Carl / Anthony / Diana / Abish)
**Status:** SHIP WITH NOTES — FIX-M7D-001 patched pre-commit; 3 INFO items deferred to FIX-LIST.md

---

## What shipped

### Static public/ files

| Path | Bytes | Purpose |
|---|---|---|
| `public/llms.txt` | 4,909 | LLM crawler discovery (Anthropic, OpenAI, Perplexity). Pricing-v2-correct ($0.22/device, 100-device floor, 14-day trial). No `/how-we-built-it` reference. Banned-term clean (`personas` → `team members`, patched pre-commit). |
| `public/robots.txt` | 960 | Allowlist marketing surfaces; disallow `/login`, `/register`, dashboard routes, `/api/`, `/hubs/`. References sitemap.xml. |
| `public/sitemap.xml` | 1,500 | 8 marketing URLs (`/`, `/pricing`, `/docs`, 5 docs sub-pages). lastmod 2026-05-09. INFO-M7D-001: manual maintenance acceptable at MVP scale. |
| `public/favicon.svg` | 527 | Bell-with-checkmark on `#0F172A` rounded-square, brand teal `#00C9A7` strokes. Primary favicon for modern browsers. |
| `public/favicon-32.png` | 680 | 32×32 PNG fallback for legacy browsers. |
| `public/favicon-64.png` | 1,297 | 64×64 PNG fallback (Diana spec). |
| `public/favicon-192.png` | 3,781 | 192×192 PNG (Android home-screen). |
| `public/favicon-512.png` | 12,861 | 512×512 PNG (high-DPI tabs, PWA). |
| `public/apple-touch-icon.png` | 3,575 | 180×180 PNG (iOS home-screen). |
| `public/og-card.png` | 266,157 | 1200×630 OG/Twitter card. Brand teal accent bar, wordmark, "Branded Windows notifications, sent from your dashboard." headline, MSP-targeted subhead, $0.22/device pricing line. |

### New TypeScript surface

`src/lib/seo.ts` — self-contained head manager. **Zero new dependencies.**

| Export | Purpose |
|---|---|
| `useSeo(options)` | React hook. Sets document.title, meta description, canonical, OG (title/description/url/type/site_name/image), Twitter card, optional JSON-LD `<script type="application/ld+json" id="page-jsonld">`. Cleanup removes the JSON-LD script on unmount. |
| `softwareApplicationLd()` | Home-page JSON-LD: `SoftwareApplication` + `Offer` (single Standard plan, $0.22/device, 100-device minimum). |
| `pricingProductLd()` | Pricing-page JSON-LD: `Product` + `AggregateOffer`. |
| `techArticleLd({ headline, description, path })` | Docs-page JSON-LD: `TechArticle`. |
| `breadcrumbLd([{ name, path }, ...])` | `BreadcrumbList` JSON-LD. |

**Defensive serialization:** `JSON.stringify(jsonLd).replace(/<\//g, '<\\/')` — guards against future schema fields containing literal `</script>` substrings (FIX-M7D-001).

### Marketing pages migrated to useSeo

All eight marketing pages replaced their inline `useEffect` head pokers with a single `useSeo()` call. Net additive — each page now ships with canonical, OG, Twitter, and JSON-LD that the prior implementation lacked.

| Page | Path | JSON-LD |
|---|---|---|
| Home | `/` | `SoftwareApplication` |
| Pricing | `/pricing` | `Product` + `AggregateOffer` + `BreadcrumbList` |
| Docs hub | `/docs` | `TechArticle` + `BreadcrumbList` |
| Getting started | `/docs/getting-started` | `TechArticle` + `BreadcrumbList` |
| Microsoft Store | `/docs/deploy/store` | `TechArticle` + `BreadcrumbList` |
| Intune | `/docs/deploy/intune` | `TechArticle` + `BreadcrumbList` |
| RMM | `/docs/deploy/rmm` | `TechArticle` + `BreadcrumbList` |
| API reference | `/docs/api` | `TechArticle` + `BreadcrumbList` |

### index.html head defaults

Updated with: `<title>`/description/canonical (default → `/`), favicon link set (svg + 32/192/512 + apple-touch), `<link rel="llms" href="/llms.txt">`, OG defaults (title/description/url/type/site_name/image=og-card.png + width/height), Twitter card defaults (summary_large_image), `<meta name="theme-color" content="#0F172A">`.

### INFO-M7C-005 (closed)

Docs body color tightened from `--text-secondary` to `--text-primary` at `.m-docs-content p` and `.m-docs-content ul/ol`. Chrome surfaces (sidebar, footer, labels) keep `--text-secondary`. Reading-grade prose now lands a notch darker than utility text.

---

## How the OG card and favicon PNGs were rendered

To avoid adding a new dependency (sharp, canvas, puppeteer-screenshot) for a one-shot asset render, we drove the existing Playwright MCP tooling against two helper HTMLs served by `npx http-server` from `Docs/ToastRevival/EVIDENCE/m7d/`:

1. `og-renderer.html` — fixed 1200×630 layout, Inter + JetBrains Mono, brand-correct gradient + accent bar, headline with `<em>` accent on "dashboard.", subhead, URL + price footer.
2. `favicon-renderer.html` — full-bleed SVG bell-with-checkmark mark (the same path data as `BrandMark.tsx`), captured at 32 / 64 / 180 / 192 / 512 px viewports.

Both renderer HTMLs ship in `Docs/ToastRevival/EVIDENCE/m7d/` (NOT in `public/`) so future rebuilds can repeat the capture without re-deriving the layouts. The PNG outputs are committed to `public/`.

---

## Verification (build-side)

```
npx tsc -p tsconfig.app.json --noEmit  → 0 errors
npm run build                          → 730 modules transformed, 0 errors, 0 warnings
                                          dist/index.html              2.79 kB │ gzip:   0.93 kB
                                          dist/static/seo-*.js         3.79 kB │ gzip:   1.45 kB
                                          dist/static/MarketingLayout-*.css 29.04 kB │ gzip: 5.22 kB
                                          dist/static/index-*.js     727.24 kB │ gzip: 213.19 kB
```

dist/ contains all static SEO assets (llms.txt, robots.txt, sitemap.xml, favicons, og-card.png) verified pre-deploy.

---

## Code Sweep (Abish)

**Verdict:** SHIP WITH NOTES.

**Pre-commit fixes:**
- FIX-M7D-001 — `</script>` escape on JSON-LD serialization (security/Step 5).
- llms.txt `personas` → `team members` (banned-term enforcement, Carl's standing rule).

**INFO items filed:**
- INFO-M7D-001: sitemap.xml hardcoded `2026-05-09` lastmod. M9 candidate (build-time generator).
- INFO-M7D-002: dashboard pages inherit marketing-flavored default `<title>` until React mounts. Codex's admin UI redesign track may add per-route `useSeo` calls.
- INFO-M7D-003: `useSeo` runs in `useEffect` — non-JS crawlers see `index.html` defaults only. Modern AI/search crawlers execute JS and see per-page meta + JSON-LD. Acceptable trade-off.

---

## Files committed

```
M  src/ToastRevival.Dashboard/index.html
M  src/ToastRevival.Dashboard/src/components/marketing/marketing.css
M  src/ToastRevival.Dashboard/src/pages/marketing/Home.tsx
M  src/ToastRevival.Dashboard/src/pages/marketing/Pricing.tsx
M  src/ToastRevival.Dashboard/src/pages/marketing/docs/DocsApi.tsx
M  src/ToastRevival.Dashboard/src/pages/marketing/docs/DocsGettingStarted.tsx
M  src/ToastRevival.Dashboard/src/pages/marketing/docs/DocsIndex.tsx
M  src/ToastRevival.Dashboard/src/pages/marketing/docs/DocsIntune.tsx
M  src/ToastRevival.Dashboard/src/pages/marketing/docs/DocsRmm.tsx
M  src/ToastRevival.Dashboard/src/pages/marketing/docs/DocsStore.tsx
A  src/ToastRevival.Dashboard/src/lib/seo.ts
A  src/ToastRevival.Dashboard/public/llms.txt
A  src/ToastRevival.Dashboard/public/robots.txt
A  src/ToastRevival.Dashboard/public/sitemap.xml
A  src/ToastRevival.Dashboard/public/favicon.svg
A  src/ToastRevival.Dashboard/public/favicon-32.png
A  src/ToastRevival.Dashboard/public/favicon-64.png
A  src/ToastRevival.Dashboard/public/favicon-192.png
A  src/ToastRevival.Dashboard/public/favicon-512.png
A  src/ToastRevival.Dashboard/public/apple-touch-icon.png
A  src/ToastRevival.Dashboard/public/og-card.png
A  Docs/ToastRevival/EVIDENCE/m7d/og-renderer.html
A  Docs/ToastRevival/EVIDENCE/m7d/favicon-renderer.html
A  Docs/ToastRevival/EVIDENCE/2026-05-09-m7d-seo-llm-discovery.md  (this file)
M  Docs/ToastRevival/MILESTONES.md
M  Docs/ToastRevival/FIX-LIST.md
M  Docs/ToastRevival/TODO.md
M  Docs/ToastRevival/STATUS.md
M  Docs/ToastRevival/CODEX-HANDOFF.md
```

---

## Production verification

To be appended after deploy.
