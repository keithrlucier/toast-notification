# ToastRevival — Design Specification
**Author**: Diana Reyes
**Status**: Initial spec — will expand before M4 implementation

---

## Design System Foundation

One design system. Website and dashboard. No exceptions.

### Typography
- **Primary**: Inter — UI text, headings, body copy. Variable font, all weights.
- **Mono**: JetBrains Mono — code snippets, API keys, device IDs, technical content.
- **Scale**: 12 / 14 / 16 / 20 / 24 / 32 / 40 / 48px. Body is 14px. Nothing smaller than 12px. Ever.
- **Line height**: 1.5 for body, 1.2 for headings. No exceptions.
- **Weight**: 400 (body), 500 (labels/emphasis), 600 (subheadings), 700 (headings).

### Spacing
- **Base grid**: 8px.
- **Values**: 4 (micro-adjustments only), 8, 16, 24, 32, 48, 64, 96.
- **Component padding**: 16px minimum internal padding on all interactive elements.
- **Section spacing**: 48px between major sections, 24px between related groups.

### Color System
```
Foundation:
  --bg-primary:       #0F1117    (app background, dark)
  --bg-secondary:     #1A1D27    (card/panel background)
  --bg-tertiary:      #242836    (elevated surfaces, hover states)
  --bg-surface:       #FFFFFF    (light mode content areas)

  --text-primary:     #F0F0F5    (high contrast, dark bg)
  --text-secondary:   #B0B0C4    (muted text, dark bg)
  --text-dim:         #7A7A92    (disabled, hints, dark bg)
  --text-dark:        #1A1D27    (text on light backgrounds)

Brand:
  --accent-primary:   #F59E0B    (amber — brand color)
  --accent-hover:     #FBBF24    (amber lighter)
  --accent-pressed:   #D97706    (amber darker)

Status:
  --status-success:   #4ADE80
  --status-warning:   #FBBF24
  --status-error:     #F87171
  --status-info:      #60A5FA

Notification Preview:
  --preview-bg:       #202020    (Windows 11 dark theme desktop color)
  --preview-card:     #2D2D2D    (Windows 11 notification card bg)
  --preview-text:     #FFFFFF    (Windows notification text)
  --preview-subtext:  #C0C0C0    (Windows notification secondary text)
```

### Elevation / Shadows
- **Level 0**: Flat (background surfaces)
- **Level 1**: `0 1px 3px rgba(0,0,0,0.3)` (cards, dropdowns)
- **Level 2**: `0 4px 16px rgba(0,0,0,0.4)` (modals, popovers)
- **Level 3**: `0 8px 32px rgba(0,0,0,0.5)` (notification preview panel)

### Border Radius
- **Small**: 4px (buttons, inputs, badges)
- **Medium**: 8px (cards, panels)
- **Large**: 12px (modals, notification preview)
- **Never**: Full rounded (pill shapes). This isn't a toy.

---

## Notification Template Gallery

Six curated templates. Each has locked visual hierarchy. The MSP fills in content — they do NOT design.

### 1. Announcement
- **Use case**: Company news, policy updates, general information
- **Tone**: Neutral, informative
- **Layout**: Title + 2-line body + optional hero image + optional single action button
- **Default audio**: ms-winsoundevent:Notification.Default

### 2. Alert
- **Use case**: Security warnings, system issues, urgent IT notices
- **Tone**: Urgent, attention-grabbing
- **Layout**: Title (bold) + 2-line body + optional hero image + 2 action buttons (Acknowledge / Dismiss)
- **Default audio**: ms-winsoundevent:Notification.Looping.Alarm
- **Scenario**: urgent (breaks through Do Not Disturb on supported builds)

### 3. Action Required
- **Use case**: Password resets, software approvals, compliance tasks
- **Tone**: Direct, clear CTA
- **Layout**: Title + 1-line body + 2 action buttons (primary action + Remind Later)
- **Default audio**: ms-winsoundevent:Notification.Reminder

### 4. Reminder
- **Use case**: Meetings, deadlines, maintenance windows
- **Tone**: Helpful, non-intrusive
- **Layout**: Title + 2-line body (includes date/time) + optional action button
- **Scenario**: reminder
- **Default audio**: ms-winsoundevent:Notification.Reminder

### 5. Celebration
- **Use case**: Birthdays, milestones, team wins, welcome messages
- **Tone**: Warm, positive
- **Layout**: Title + 1-line body + hero image (branded celebration graphic)
- **Default audio**: ms-winsoundevent:Notification.Default

### 6. Maintenance
- **Use case**: Scheduled downtime, update windows, system reboots
- **Tone**: Matter-of-fact, includes timeframe
- **Layout**: Title + 2-line body (what, when, impact) + 2 action buttons (Details / Acknowledge)
- **Default audio**: ms-winsoundevent:Notification.Default

---

## Live Preview Panel

The notification composer includes a preview panel on the right side that renders a pixel-accurate representation of the Windows toast notification.

### Requirements
- Dark background (#202020) simulating Windows 11 desktop
- Notification card matches Windows 11 notification styling (#2D2D2D, rounded corners, Segoe UI font in preview)
- **Preview uses Segoe UI** (Windows system font) — NOT Inter. The preview must match what Windows renders.
- Updates in real-time as user types
- Shows character count warnings when approaching truncation limits
- Shows hero image at correct 364x180 ratio
- Shows app logo at correct 48x48
- Shows action buttons in Windows style
- Responsive — collapses below the composer on narrow viewports

---

## Admin Dashboard Layout

### Navigation
- Left sidebar, collapsible to icon-only on narrow viewports
- Sections: Dashboard, Notifications (compose/history), Devices, Templates, Assets, Settings, Audit Log
- Tenant name + logo at top of sidebar
- User avatar + role badge at bottom

### Dashboard (Home)
- Key metrics: Active devices, notifications sent (7d/30d), delivery rate, interaction rate
- Recent notifications with status indicators
- Device health summary (connected/disconnected/stale)
- Quick-send button (opens composer with most-used template)

### Notification Composer (Full Page)
- Left: Template selector + content fields + target picker + schedule options
- Right: Live preview panel (fixed position, scrolls with content)
- Bottom: Send button with broadcast confirmation dialog when targeting > 100 devices

---

## M5 Analytics Dashboard (D1 — for M5.B implementation)

**Chart library: Recharts 2.x.** No other chart library. Configure `isAnimationActive={false}` on all charts — the only acceptable animation is the axis transition on first render, and even that should be killed on slow machines. If Recharts adds animation by default, turn it off.

### Metric Summary Row
Four metric cards across the top of the Analytics page. Same `.metric-card` CSS class already in the design system.

| Card | Metric | Format |
|---|---|---|
| Sent (7d) | Total notifications sent in the last 7 days | Integer |
| Delivery Rate | (Delivered / Total Deliveries) × 100 | `XX.X%` |
| Interaction Rate | (Clicked / Delivered) × 100 | `XX.X%` |
| Active Devices | Devices with a ping in the last 24h | Integer |

### Notification Volume Chart (Line Chart)
- **Position**: Full width, below metric row
- **Chart type**: `<LineChart>` from Recharts
- **Data**: Notifications sent per day, last 30 days. X-axis: date label (`"May 7"`). Y-axis: count, integer ticks only.
- **Series**:
  - Sent — color `#F59E0B` (accent), `strokeWidth={2}`, `dot={false}`
  - Delivered — color `#60A5FA` (status-info), `strokeWidth={1.5}`, `dot={false}`, `strokeDasharray="4 2"`
- **Grid**: `<CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.06)" vertical={false}`
- **Tooltip**: Dark tooltip — `background: var(--bg-tertiary)`, `border: 1px solid rgba(255,255,255,0.08)`, `border-radius: 6px`, `font-size: 12px`. Do not use the default Recharts tooltip style.
- **Legend**: Bottom, horizontal, `font-size: 12px`, `color: var(--text-secondary)`
- **Height**: 240px

### Delivery Status Breakdown (Bar Chart)
- **Position**: Left column, below volume chart
- **Chart type**: `<BarChart>` from Recharts
- **Data**: Count of deliveries in each status (Pending / Delivered / Clicked / Dismissed / Failed) for the selected time range
- **Bars**: One bar per status
  - Delivered: `#4ADE80` (status-success)
  - Clicked: `#F59E0B` (accent)
  - Dismissed: `#7A7A92` (text-dim)
  - Failed: `#F87171` (status-error)
  - Pending: `#FBBF24` (status-warning)
- **Bar radius**: 2px
- **Grid**: Horizontal only, same style as volume chart
- **Height**: 200px

### Template Usage Breakdown (Horizontal Bar Chart)
- **Position**: Right column, beside delivery status chart
- **Chart type**: `<BarChart layout="vertical">` from Recharts
- **Data**: Notification count per template category for the selected time range
- **Bars**: Single series, color `#60A5FA` (status-info), radius 2px
- **Height**: 200px

### Time Range Selector
- Position: Top-right of the Analytics page, inline with the page header
- Options: 7 days | 30 days | 90 days (segmented button control — three `<button>` elements, amber background on active)
- Default: 30 days
- No date picker. These three presets are enough for MSPs.

### Backend Requirements for D1 (Anthony reads this before building)
Three new endpoints needed:
1. `GET /api/analytics/summary?days=30` → `{ sentCount, deliveryRate, interactionRate, activeDeviceCount }`
2. `GET /api/analytics/volume?days=30` → `[{ date: "2026-05-07", sent: 12, delivered: 10 }]`
3. `GET /api/analytics/breakdown?days=30` → `{ byStatus: { Delivered: N, Clicked: N, ... }, byTemplate: { announcement: N, ... } }`

These are read-only, tenant-scoped, aggregate queries. No new models, no migrations — pure SQL/LINQ over existing tables.

### Rules for M5.B
- Install Recharts: `npm install recharts` — confirm it exists before importing
- `isAnimationActive={false}` on every chart component
- All chart containers: `background: var(--bg-secondary)`, `border-radius: var(--radius-md)`, `padding: 24px` (`.card` class)
- `ResponsiveContainer width="100%"` wraps every chart — never set a pixel width
- Custom tooltip only — no Recharts default tooltip styling
- No `<Legend>` on single-series charts
- The metric row must render before the charts load (skeleton state acceptable — show `—` in metric cards while charts fetch)

---

## Marketing Site (toastnotification.com) — M7

**Status**: M7.A spec — Diana, 2026-05-09. M7.B/C/D implement against this.

### Architectural Premise

One SPA, two visual contexts. The same React app at `src/ToastRevival.Dashboard/` serves both the marketing site and the authenticated product. Marketing routes use a different page chrome (`MarketingLayout`) — no sidebar, no top bar, just `<MarketingHeader>` / `<main>` / `<MarketingFooter>`. Authenticated routes keep the existing `Layout`. Same design tokens, same components where possible, same deploy. No second codebase. No subdomain split.

### Routes (public, unauthenticated)

| Path | Page | Purpose |
|---|---|---|
| `/` | `Home.tsx` | Hero, problem/solution, features, how-it-works, pricing summary, CTA, social proof slot |
| `/pricing` | `Pricing.tsx` | Full tier comparison, FAQ, CTA |
| `/docs` | `DocsIndex.tsx` | Documentation hub — getting started + deploy guides + API reference index |
| `/docs/getting-started` | `DocsGettingStarted.tsx` | Sign up → register tenant → install agent → send first notification |
| `/docs/deploy/intune` | `DocsIntune.tsx` | Intune LOB deployment guide |
| `/docs/deploy/rmm` | `DocsRmm.tsx` | RMM deployment patterns (NinjaOne, Datto, ConnectWise, Atera) |
| `/docs/deploy/store` | `DocsStore.tsx` | Microsoft Store install path |
| `/docs/api` | `DocsApi.tsx` | API reference — auth, devices, notifications, webhooks |
| `/how-we-built-it` | `HowWeBuiltIt.tsx` | The DocPro engineering case study (Keith-approved framing) |
| `/legal/privacy` | `Privacy.tsx` | Privacy policy |
| `/legal/terms` | `Terms.tsx` | Terms of service |

`/login`, `/register`, `/onboarding`, `/dashboard`, `/billing`, etc. — unchanged. The router differentiates `MarketingLayout` from `Layout` by route definition, not by auth state. A logged-in user visiting `/` still sees the marketing home (with a "Go to dashboard" CTA in the header instead of "Sign in").

### Page Chrome

#### `<MarketingHeader>` (sticky, height 64px, `--bg-primary` with bottom border `rgba(255,255,255,0.06)`)
```
[Logo + wordmark]              [Pricing] [Docs] [How we built it]    [Sign in] [Get started →]
```
- Logo: 28px tall toast bell icon + "Toast Notification" wordmark in Inter 600, 16px. Wordmark hidden below 480px viewport — logo only.
- Nav links: Inter 500, 14px, `--text-secondary`, hover → `--text-primary`. Active route gets `--accent-primary` underline (2px, 4px below baseline).
- Right side: "Sign in" is a ghost link (`--text-secondary`, hover `--text-primary`); "Get started →" is the primary teal button (`btn-primary` class, exists).
- When logged in: "Sign in" → "Dashboard", "Get started →" → "Open dashboard".
- Mobile (<768px): hamburger icon (44×44 touch target) opens slide-down menu — full viewport width, list of nav links + auth CTAs, each link 56px tall (touch target).

#### `<MarketingFooter>` (`--bg-secondary`, padding 64px top / 32px bottom, top border `rgba(255,255,255,0.06)`)
```
[Logo + tagline]                    [Product]      [Resources]    [Company]       [Legal]
                                    Pricing        Docs           How we built it Privacy
                                    Sign in        API reference                  Terms
                                    Get started    Status

────────────────────────────────────────────────────────────────────────
© 2026 Toast2IT, LLC. Built in the United States.       [GitHub icon]
```
- Five-column layout on desktop, single column stacked on mobile.
- Tagline: "Managed Windows toast notifications for MSPs." (Inter 400, 14px, `--text-secondary`)
- Column headers: Inter 600, 12px, uppercase, letter-spacing 0.08em, `--text-dim`.
- Links: Inter 400, 14px, `--text-secondary`, hover `--text-primary`.
- 24px gap between header and first link, 12px between links, 48px gap between columns.
- GitHub icon links to public org page (M7.D decision; placeholder for now).
- No social-media icon spam. No newsletter signup form. No cookie consent banner — we don't track.

### Page-by-Page

#### Home (`/`)

**Section 1 — Hero** (above the fold, min-height `100vh - 64px`, `--bg-primary`)

Layout: 12-column grid, content centered horizontally, vertically centered within the section.

```
                    [Eyebrow text — small caps, --accent-primary]
                    
                    Toast notifications,
                    managed.

                    One platform for MSPs to send rich, branded
                    Windows notifications to managed endpoints.
                    No PowerShell. No third-party clutter.

                    [Get started — free for 10 devices →]   [See how it works ↓]

                    ─────────────────────────────────────────
                    
                    [Product screenshot — actual dashboard composer + live preview]
```

- **Eyebrow**: "BUILT FOR MSPs" — Inter 600, 11px, letter-spacing 0.12em, `--accent-primary`. 8px below: 1px wide, 32px long horizontal rule in `--accent-primary` at 40% opacity.
- **Headline**: "Toast notifications,\nmanaged." — Inter 700, 56px desktop / 40px tablet / 32px mobile, line-height 1.1, `--text-primary`. The line break is intentional and load-bearing — "managed." gets its own line because that's the product.
- **Subhead**: Inter 400, 18px desktop / 16px mobile, line-height 1.6, `--text-secondary`, max-width 560px.
- **Primary CTA**: `btn-primary` class, "Get started — free for 10 devices →", links to `/register`.
- **Secondary CTA**: `btn-ghost` class, "See how it works ↓", anchor link to `#how-it-works`.
- **Hero image**: Real screenshot of `Compose.tsx` + live preview panel at the moment of typing. NOT a generated mockup. Captured at 2400×1350 (16:9), exported as WebP + PNG fallback, max display width 1200px, drop shadow `var(--shadow-3)`, 12px border radius. Subtle 1px border `rgba(255,255,255,0.08)`.
- Below the screenshot, 96px below: a single muted line — "Already trusted by MSPs to deliver [N] notifications." — `--text-dim`, 14px. The number is a real count from `AnalyticsController.Summary` aggregated across all tenants, fetched at build time and baked into the static page (`/api/analytics/global-summary` — new public endpoint, M7.B). Skip this line when the count is under 1000; we're not faking traction.

**Section 2 — Problem/Solution** (`--bg-secondary`, 96px vertical padding)

Two-column layout, 50/50 split, 48px gap.

| Left | Right |
|---|---|
| **Heading**: "PowerShell isn't communication."<br><br>**Body**: "Most MSPs notify endpoints with `msg.exe`, custom scripts, or whatever notification API their RMM ships with. None of it scales. None of it is brandable. None of it tracks delivery or interaction. None of it is what your end users deserve."<br><br>"Toast Notification is a single platform: a signed Windows agent, a multi-tenant API, and a dashboard that any tech can drive." | **Visual**: Side-by-side comparison card. Left: a `msg.exe` console output. Right: a real Toast Notification render (Segoe UI, hero image, action buttons). Border around each, label above ("Before" / "After"). Built in HTML/CSS, not a static image — it stays sharp at any zoom level. |

- Heading: Inter 700, 40px, line-height 1.15, `--text-primary`. The period is intentional.
- Body: Inter 400, 16px, line-height 1.65, `--text-secondary`, max-width 480px.

**Section 3 — Features** (`--bg-primary`, 96px vertical padding)

Section heading: "What you get." (Inter 700, 40px, centered, 64px below: subheading "Production-grade infrastructure, no MSP-specific bolt-ons." in `--text-secondary`, 16px.)

Four feature cards in a 2×2 grid (single column on mobile, 24px gap):

| Card | Icon | Heading | Body |
|---|---|---|---|
| 1 | Bell-with-checkmark SVG, 32px, `--accent-primary` | **Branded notifications, every endpoint.** | Six curated templates. Hero images. Logos. Action buttons. Audio. Scenario-aware (Reminder, Alarm, Urgent). The end user sees your brand, not Microsoft's. |
| 2 | Lock-with-key SVG, 32px, `--accent-primary` | **Multi-tenant, signed, audited.** | JWT auth. Per-tenant HMAC payload signing. Azure Content Safety on every notification. Full audit log with CSV/PDF export. SOC-ready. |
| 3 | Cloud-with-arrow SVG, 32px, `--accent-primary` | **Deploy with what you already use.** | Signed MSI. Signed MSIX for the Microsoft Store. Intune LOB compatible. RMM silent install with `CLIENTID` and `SERVERURL` properties. Velopack auto-update built in. |
| 4 | Bar-chart SVG, 32px, `--accent-primary` | **Delivery and interaction tracking.** | Every notification reports back: delivered, clicked, dismissed, failed. Aggregate analytics. Per-notification reports. Export to CSV or PDF for incident reviews. |

- Card: `--bg-secondary`, 24px padding, 8px border radius, no border. Hover: 1px border in `rgba(255,255,255,0.08)`, 200ms transition on border only.
- Card heading: Inter 600, 18px, `--text-primary`, 16px below icon, 8px below: body text.
- Card body: Inter 400, 14px, line-height 1.6, `--text-secondary`.

**Section 4 — How It Works** (`#how-it-works`, `--bg-secondary`, 96px vertical padding)

Section heading: "Three steps." (centered, same type as Features heading.)

Three-step horizontal flow on desktop (single column on mobile):

```
[01]                      [02]                      [03]
Deploy the agent.         Build a notification.     Send it.

Drop the signed MSI       Pick a template.          Pick a target — one device,
into Intune, your RMM,    Add a title, body, hero   a group, or every endpoint
or run msiexec on a       image, action buttons.    in your tenant. Hit send.
single endpoint with      See exactly what your     Track every delivery and
CLIENTID and SERVERURL.   end user will see.        interaction in real time.
```

- Step number: Inter 700, 64px, `--accent-primary` at 30% opacity (decorative, not load-bearing).
- Step heading: Inter 700, 24px, `--text-primary`.
- Step body: Inter 400, 14px, line-height 1.65, `--text-secondary`, max-width 280px.
- 48px gap between columns. 96px below the section heading.
- Below the three columns, 64px gap, then a single CTA centered: "Read the deployment guides →" linking to `/docs`. `btn-ghost` class.

**Section 5 — Pricing summary** (`--bg-primary`, 96px vertical padding)

Section heading: "Pricing that fits how MSPs actually buy." (centered, same type.)

Three-card pricing comparison, 24px gap, each card 320px desktop (single column on mobile, 16px gap):

| Card | Tier | Price | Devices | Bullets |
|---|---|---|---|---|
| Free | "For pilots and small teams." | $0 | 1–25 devices | Six templates, full feature set, audit log, CSV export, Microsoft Store install path, MSI install with auto-update |
| Standard | "For working MSPs." (highlighted, amber border, "MOST POPULAR" eyebrow) | $22 / month flat | 26–100 devices | Everything in Free, plus PDF export, advanced analytics, custom logo + colors, Stripe billing, priority support |
| Growth | "For growing fleets." | $44 / month flat | 101–200 devices | Everything in Standard |
| Enterprise | "For larger fleets and compliance-driven shops." | Contact us | 200+ devices | Everything in Growth, plus single-sign-on (M9 roadmap), dedicated infra, SOC 2 reports, custom SLAs |

- Card: `--bg-secondary`, 32px padding, 8px border radius. The Standard card has a 1px border in `--accent-primary` and a small "MOST POPULAR" eyebrow above the tier name (Inter 600, 11px, `--accent-primary`, letter-spacing 0.1em).
- Tier name: Inter 700, 24px.
- Tagline: Inter 400, 14px, `--text-secondary`, 8px below tier.
- Price: Inter 700, 40px, `--text-primary`, 24px below tagline. "Free" / "$22 / month" / "$44 / month" / "Contact us".
- Bullets: List of 5–6 items, Inter 400, 14px, `--text-secondary`, line-height 1.7, `✓` prefix in `--accent-primary` (use the same SVG checkmark as the dashboard).
- CTA at bottom of each card: Free → "Get started" (primary), Standard → "Get started" (primary), Growth → "Get started" (primary), Enterprise → "Contact sales" (ghost).
- Below the cards, 48px gap, centered text: "Full comparison and FAQ on the [pricing page →](/pricing)." Inter 400, 14px, `--text-secondary`.

**Section 6 — Final CTA** (`--bg-primary`, 96px vertical padding, max-width 720px centered)

```
                Start sending notifications today.

                Ten devices, free, no credit card. Sign up, install
                the agent, send your first notification in under
                ten minutes.

                [Get started →]      [Read the docs]
```

- Heading: Inter 700, 40px, centered, `--text-primary`.
- Body: Inter 400, 16px, centered, line-height 1.65, `--text-secondary`, max-width 520px, 16px below heading.
- CTAs: 32px below body, side by side (stacked on mobile). Primary + ghost.

#### Pricing (`/pricing`)

Reuses the three-card layout from the home page Section 5, but the cards are taller and include full feature lists (every feature, not just the highlights). Below the cards:

**FAQ section** (`--bg-secondary`, 96px vertical padding). Eight questions in an accordion (single-open). Questions:

1. What counts as a "device"?
2. What happens if I exceed my device limit?
3. Can I switch tiers mid-month?
4. Do you offer annual billing?
5. What payment methods do you accept?
6. How do I cancel?
7. Is there a free trial of Pro?
8. Where is my data stored?

Each answer is 2–3 sentences, Inter 400, 14px, line-height 1.7, `--text-secondary`. The accordion uses the same chevron SVG as the dashboard sidebar collapse.

**Comparison table** (`--bg-primary`, between cards and FAQ). Five-column table: Feature | Free | Pro | Enterprise | Notes. Categories: Templates, Branding, Deployment, Auditing, Support. About 25 rows. Same `data-table` class as the dashboard.

#### Docs (`/docs` and children)

**Layout**: Two-column on desktop (240px sidebar nav + content), single-column on mobile (collapsible sidebar from a hamburger).

**Sidebar nav** (sticky, top: 80px to clear the header):
- **Getting Started**
- **Deployment**
  - Microsoft Store
  - Intune
  - RMM
- **API Reference**
  - Authentication
  - Devices
  - Notifications
  - Webhooks
- **Concepts** (M9)
  - Multi-tenancy
  - Content moderation
  - HMAC payload signing

Active link: `--accent-primary` text, 2px left border in `--accent-primary`. Inactive: `--text-secondary`, hover `--text-primary`.

**Content area** (max-width 720px):
- Heading hierarchy: H1 32px, H2 24px (4px bottom border, `rgba(255,255,255,0.06)`, 16px below text), H3 18px.
- Body: Inter 400, 16px, line-height 1.7, `--text-secondary`. (Slightly larger than 14px for readability — docs only.)
- Code blocks: JetBrains Mono, 14px, `--bg-tertiary` background, 16px padding, 8px border radius, copy button top-right (existing component pattern from Onboarding step 3).
- Inline code: JetBrains Mono, 13px, `--bg-tertiary` background, 4px horizontal / 2px vertical padding, 4px border radius.
- Callout boxes (Note / Warning): 16px padding, 8px left border (4ADE80 for note, FBBF24 for warning), `--bg-secondary` background.
- "Edit this page on GitHub" link at bottom of each doc page (M7.D).

#### How We Built It (`/how-we-built-it`)

Long-form essay, single column, max-width 720px, 64px vertical padding.

**Tone**: First-person plural. Engineering narrative. No hero myth about Keith. Concrete milestone data.

**Structure**:
1. **The brief** — What an MSP customer asked for in 2026.
2. **The team** — Who we are. Carl (architect), Anthony (backend), Diana (design), Abish (QA). Keith is the operator who runs the platform; we are the team who builds. *No portrait photos. No bios written like LinkedIn. One paragraph per role describing what we own and how we work — concrete, not aspirational.*
3. **The process** — DocPro coordinates the team across milestones. Every commit goes through Code Sweep before it merges. We don't ship code that hasn't been read by a second pair of eyes. We log every session for continuity. We don't lose context between Tuesday and Thursday.
4. **The numbers** — Built in N days. M0A through M7.A. M migrations, K commits, L deliverables. Backend, agent, dashboard, billing, marketing — full stack. Real production. No demo data.
5. **What's next** — M8 testing, M9 launch. Beta program. Keep building.

**Banned framing**: hero myth about any single person, "AI replaced our developers," "10× faster than human teams," anything that sells the AI angle as the product. The product is the toast platform; the AI angle is *how* we built it, told plainly because it's interesting.

**Image strategy**: One screenshot of the team kanban (or git graph), nothing else. No team avatars. No stock photos.

#### Legal pages (`/legal/privacy`, `/legal/terms`)

Plain-text content in the same `MarketingLayout` as docs but without the sidebar. Single column, max-width 720px, the standard heading hierarchy. **Content is Keith's responsibility to provide before M7.D ships** — Diana writes the layout/typography spec, not the legal text.

### Image Strategy (locked)

Per existing Diana standing rules:
- **No stock photos.** None.
- **No AI-generated hero art, illustrations, or decorative images.** AI is fine for icons (SVGs hand-drawn from prompts and reviewed). It is not fine for anything that fakes provenance.
- **Product screenshots are the only photographs.** Captured at 2× retina, WebP primary + PNG fallback, lazy-loaded below the fold.
- **No gradient blobs, no floating geometric shapes, no abstract decoration.** We're selling to IT professionals.
- **Hero image** = real `Compose.tsx` screenshot. Captured against a representative tenant with the live preview rendering a real Alert template.
- **Feature card icons** = custom SVGs, 32px, line-weight 1.5, single color (`--accent-primary`). Designed to match the dashboard sidebar icons (which are Lucide-derived). See "Icon System" below.
- **Open Graph / Twitter card image** = single 1200×630 PNG of the dashboard with the wordmark overlay, custom-shot. M7.D deliverable.
- **Favicon** = the bell-with-checkmark mark at 32px / 64px / 192px / 512px. SVG primary, ICO fallback.

### Icon System

Marketing uses the same icon vocabulary as the dashboard sidebar (Lucide line icons, 1.5 stroke weight). Custom one-offs (the four feature card icons) follow the same line weight and visual density. **Stroke-only**, not filled. **Round join, round cap.** No drop shadows on icons.

Onboarding emoji replacements (closes INFO-M6-004) — see "Onboarding SVG Icons" section below.

### Animation

Marketing site animations:
- **Allowed**: Hero CTA hover (background color, 150ms), feature card border on hover (200ms), accordion chevron rotation on open (180° / 200ms), sticky-header shadow appearing on scroll past 80px (opacity, 150ms).
- **Allowed once**: Hero subhead and CTAs fade-in on initial page load — `opacity: 0 → 1` over 400ms with a 100ms stagger between subhead and CTAs. **DEFAULT STATE IS opacity:1**, the animation is added by JS only when `prefers-reduced-motion: no-preference` matches. Everything renders fully on first paint without JS. (Standing rule from Abish 2026-05-04 — scroll-reveal CSS must default to opacity:1, never opacity:0.)
- **Banned**: Scroll-triggered reveals on every section, parallax, animated counters, particle backgrounds, anything Webflow-template.
- **`prefers-reduced-motion: reduce`** → all transitions become instant (no 0ms; explicitly `transition: none`).

### Mobile

Breakpoints: 1280 / 1024 / 768 / 480.

| Width | Layout shift |
|---|---|
| ≥ 1280 | Desktop. Full-width hero up to max-width 1200px, centered. |
| 1024–1279 | Slight padding reduction (32px outer instead of 48px). Pricing cards still 3 across. |
| 768–1023 | Pricing cards stack to single column. Features grid becomes 1×4. Header still horizontal. |
| 480–767 | Header collapses to hamburger. Hero headline 32px. All side-by-side sections stack. Footer collapses to single column. |
| < 480 | Same as above; logo wordmark hides, leaves only the icon. |

Touch targets: every interactive element ≥ 44×44px on viewports below 768px. Standing rule.

### Performance

Targets:
- **First Contentful Paint** ≤ 1.0s on 4G mobile.
- **Largest Contentful Paint** ≤ 2.5s on 4G mobile (the hero screenshot is the LCP element — preload it).
- **Cumulative Layout Shift** ≤ 0.05.
- **Total Blocking Time** ≤ 200ms.
- **Lighthouse Performance score** ≥ 90 on both mobile and desktop.

Implementation rules:
- Marketing routes are part of the SPA bundle but lazy-loaded (`React.lazy()`). The initial bundle for `/` should not pay for the dashboard's chart libraries, the toast preview, the asset library, or the auth context's MFA logic.
- Hero image: WebP, `srcset` with 480/960/1440/2400 widths, `<link rel="preload" as="image" imagesrcset>` in `index.html`.
- No web fonts loaded from Google. Inter and JetBrains Mono are self-hosted (already in the project for the dashboard). `font-display: swap`.
- No third-party analytics scripts. No marketing pixels. No GTM. We don't need to know what you click on this page.

### Copy Direction

The voice of the marketing site:
- **First-person plural for the team / our work.** "We built." "We chose." "We don't ship code that hasn't been read."
- **Second-person for the customer.** "You install." "Your endpoints." "Your audit log."
- **Direct. Concrete.** Real numbers, real product names (Intune, Datto, ConnectWise, Microsoft Store), real verbs.
- **No marketing-ese.** Banned words: "robust," "seamless," "powerful," "leverage," "synergy," "best-in-class," "next-generation," "enterprise-grade," "industry-leading," "innovative," "transformative," "AI-powered" (the product isn't AI-powered; it was *built* with AI — different thing), "delight," "journey."
- **No hero myth.** No paragraphs about Keith's vision. No founder story. The product is the story.
- **MSP-targeted vocabulary.** "Managed endpoints," "RMM," "tenant," "MSI," "Intune LOB," "silent install," "agent," "audit log." If a phrase wouldn't appear in an MSP procurement RFP, rewrite it.
- **Banned terms (carry-forward from project standing rules)**: "persona," "audio drama," "jailbreak." None of these are in this project's domain anyway, but they're banned across all public-facing text and Code Sweep checks for them.
- **Cookie consent banner**: none. We don't track. (See SEO/llms.txt for the privacy posture.)

### Onboarding SVG Icons (closes INFO-M6-004)

The current `Onboarding.tsx` welcome step uses four emoji icons: 🔔 📋 📦 🚀. Diana's standing rule: **emojis are not UI**. Replacements below — all 32px, 1.5 stroke weight, `--accent-primary`, single color, no fill. Designed to match the existing dashboard sidebar icons.

Each icon is a small inline SVG component, defined in `src/ToastRevival.Dashboard/src/icons/onboarding/`. Drop the emoji, drop the inline `<span>`, swap in the React SVG component.

| Replaces | Icon Name | Concept | Path Notes |
|---|---|---|---|
| 🔔 | `OnboardingBell` | Bell with notification dot | Standard bell silhouette, 4px circle in upper-right of the bell as the "active" indicator. |
| 📋 | `OnboardingTemplate` | Document with structured content lines | Rectangle with rounded corners, three horizontal lines representing a template's title + body + body, plus one shorter "button row" line at the bottom. |
| 📦 | `OnboardingPackage` | MSI package box | Rectangular box with a tape-line seam down the front and a small Windows-style flag on the side (just the four-square flag, line-weight not filled). Communicates "installer" without being an actual Windows logo. |
| 🚀 | `OnboardingLaunch` | Stylized arrow rising | Diagonal arrow from lower-left to upper-right with two short motion-marks behind it. NOT a rocket. Rockets are toy-store iconography; an arrow says "shipped" or "launched" without the kid stuff. |

**Implementation note for M7.B (Anthony)**:
- Add `import { OnboardingBell, OnboardingTemplate, OnboardingPackage, OnboardingLaunch } from '../icons/onboarding';`
- Replace the four `<span>` emojis in the welcome step with `<OnboardingBell />` etc., 32×32, color via `currentColor` so the existing parent CSS class controls the color.
- The icons render at the same size as the existing emoji slot — no layout shift, no spacing change.
- Stroke `currentColor`, fill `none`, `stroke-width="1.5"`, `stroke-linecap="round"`, `stroke-linejoin="round"`.

**Reference SVG paths (Diana's specs — Anthony hand-codes the React components)**:

```svg
<!-- OnboardingBell — 32x32 viewBox -->
<svg viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
  <path d="M16 4 C 11 4, 8 7, 8 13 L 8 18 L 6 22 L 26 22 L 24 18 L 24 13 C 24 7, 21 4, 16 4 Z" />
  <path d="M14 25 C 14 27, 15 28, 16 28 C 17 28, 18 27, 18 25" />
  <circle cx="22" cy="9" r="2.5" />
</svg>

<!-- OnboardingTemplate — 32x32 viewBox -->
<svg viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
  <rect x="6" y="5" width="20" height="22" rx="2" />
  <line x1="10" y1="11" x2="22" y2="11" />
  <line x1="10" y1="15" x2="22" y2="15" />
  <line x1="10" y1="19" x2="18" y2="19" />
  <line x1="10" y1="23" x2="14" y2="23" />
</svg>

<!-- OnboardingPackage — 32x32 viewBox -->
<svg viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
  <rect x="5" y="9" width="22" height="18" rx="1" />
  <line x1="5" y1="14" x2="27" y2="14" />
  <line x1="16" y1="9" x2="16" y2="27" />
  <path d="M11 4 L 16 9 L 11 9 Z" />
  <path d="M21 4 L 16 9 L 21 9 Z" />
</svg>

<!-- OnboardingLaunch — 32x32 viewBox -->
<svg viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
  <path d="M8 24 L 24 8" />
  <path d="M24 8 L 24 14" />
  <path d="M24 8 L 18 8" />
  <path d="M6 22 L 9 22" />
  <path d="M10 26 L 10 23" />
</svg>
```

These are reference paths. Anthony tunes line tension during M7.B if a stroke renders awkwardly at small sizes. Diana reviews before merge.

### `llms.txt` Draft (M7.D delivers)

The `llms.txt` file lives at `https://toastnotification.com/llms.txt` (and is also linked from `index.html`'s `<head>` as `<link rel="llms" href="/llms.txt">` — convention is still settling but Anthropic, Perplexity, and OpenAI crawlers all read this file when present). It is plain text, no HTML, no markdown rendering. Below is the draft content; M7.D commits the final version.

```
# Toast Notification

Toast Notification is a SaaS platform that lets MSPs (managed service providers)
send rich, branded Windows toast notifications to managed endpoints at scale.

## What it is

A signed Windows agent + multi-tenant API + admin dashboard. The agent installs
on each managed endpoint via signed MSI or Microsoft Store MSIX. The dashboard
lets administrators design notifications using six curated templates (Announcement,
Alert, Action Required, Reminder, Celebration, Maintenance) and send them to any
target — a single device, a device group, or every endpoint in a tenant.
Delivery and interaction are tracked end-to-end. Audit logs export to CSV or PDF.

## Who it is for

Managed service providers (MSPs), internal IT departments, and any organization
that needs to reach Windows endpoints with branded, trackable notifications
instead of ad-hoc PowerShell or RMM-bundled alert APIs.

## How MSPs deploy it

Three documented deployment paths:

1. Microsoft Store — install via Store, register tenant via MSI properties or
   environment variables. Best for individual users and BYOD endpoints.

2. Intune LOB — upload the signed MSIX as a Line-of-Business app. Best for
   MDM-managed corporate endpoints.

3. RMM silent install — `msiexec /i ToastNotification.Agent.msi /qn
   CLIENTID=<tenant-guid> SERVERURL=https://toastnotification.com`. Compatible
   with NinjaOne, Datto, ConnectWise, Atera, and other RMMs that support
   silent MSI installation.

## What it costs

- Free: 1–25 devices, full feature set. No credit card.
- Standard: $22/month flat, 26–100 devices.
- Growth: $44/month flat, 101–200 devices.
- Enterprise: contact sales, 200+ devices, SOC 2, custom SLA.

Full pricing: https://toastnotification.com/pricing

## Documentation

- Getting started: https://toastnotification.com/docs/getting-started
- Deployment guides: https://toastnotification.com/docs/deploy
- API reference: https://toastnotification.com/docs/api

## Technical architecture

- Windows Agent: .NET 8, Windows App SDK 1.7, signed with Sectigo OV cert,
  auto-updates via Velopack.
- Backend API: ASP.NET Core 8, EF Core 8, PostgreSQL, SignalR, Stripe.net.
- Dashboard: React 18, Vite 6, TypeScript.
- Hosting: AWS Lightsail, two-box (web + database) topology.

## Security

- Multi-tenant data isolation enforced at the EF Core query filter level.
- Per-tenant HMAC-SHA256 payload signing on every notification.
- JWT auth for both users and devices.
- TOTP MFA enforced on broadcast (TargetType=All) sends.
- Azure Content Safety scans every notification before fan-out.
- Tenant blocklists for content filtering.

## Who built it

Toast Notification was built by an AI-native development team coordinated through
DocPro, an AI development platform. The team — Carl (architect), Anthony (backend),
Diana (design), Abish (QA) — operates as four specialist personas with persistent
memory across sessions. Every commit passes a five-perspective Code Sweep before
merge. Full engineering case study: https://toastnotification.com/how-we-built-it

## Contact

- Product: https://toastnotification.com
- Status page: not yet published (M9)
- Support: in-product help (logged in users)
- Sales: contact form on /pricing (Enterprise tier inquiries)

## License

Toast Notification is a proprietary commercial product. The Windows agent runtime
is closed-source. Documentation, getting-started examples, and API reference are
public and may be quoted with attribution.
```

This draft is M7.D's starting point. Update with real device-count numbers, Status page URL, and Support details before publishing.

### SEO / JSON-LD (M7.D delivers)

Each marketing page ships with:
- `<title>` ≤ 60 chars, page-specific.
- `<meta name="description">` ≤ 160 chars, page-specific, MSP-vocabulary-dense.
- `<link rel="canonical">` to the absolute URL.
- Open Graph (`og:title`, `og:description`, `og:image`, `og:url`, `og:type=website`, `og:site_name=Toast Notification`).
- Twitter card (`twitter:card=summary_large_image`, same content as OG).
- One JSON-LD `<script type="application/ld+json">` block per page:
  - Home: `SoftwareApplication` schema with `applicationCategory=BusinessApplication`, `operatingSystem=Windows`, `offers` array (Free/Pro/Enterprise).
  - Pricing: `Product` + `AggregateOffer`.
  - Docs: `TechArticle` + `BreadcrumbList`.
  - How We Built It: `Article`.

Sitemap: `/sitemap.xml`, hand-rolled, lists all marketing routes + docs. Update on every M7.D deploy.

`robots.txt`: allow everything except `/api/`, `/hubs/`, `/login`, `/dashboard`, `/billing`, `/onboarding`, `/admin`, `/audit`, `/devices`, `/templates`, `/users`, `/api-keys`, `/assets-management`. The dashboard is auth-gated and shouldn't be indexed.

### Acceptance Criteria (M7.B/C/D close against this spec)

For Build Mode to call M7.B done:
1. Home, Pricing, How We Built It pages render in production with all content from this spec.
2. Hero LCP image preloaded; Lighthouse Performance ≥ 90 mobile.
3. Onboarding SVGs replace emojis on `/onboarding` welcome step (closes INFO-M6-004).
4. Marketing routes lazy-loaded (initial bundle excludes Recharts, Compose, AssetLibrary).
5. `prefers-reduced-motion` honored.
6. Mobile breakpoints render correctly at 1280 / 1024 / 768 / 480.
7. Code Sweep returns SHIP.
8. Diana sign-off on visual fidelity (every section, both desktop and mobile).

For M7.C done:
1. Docs hub + getting-started + 3 deploy guides + API reference render.
2. Sidebar nav active states correct.
3. Code blocks have working copy buttons.
4. All internal anchor links work.
5. Code Sweep + Diana sign-off.

For M7.D done:
1. `llms.txt` live at root, content matches the draft above with real numbers.
2. JSON-LD on every page, validates clean against Google Rich Results Test.
3. `sitemap.xml`, `robots.txt`, OG image, favicon set live.
4. Lighthouse SEO score = 100, Accessibility ≥ 95.
5. Code Sweep + production verification.

### Out of Scope for M7

These are explicitly NOT M7 work:
- Status page (M9).
- Customer testimonials section (no real testimonials yet — adding a placeholder is forbidden by the "no faked traction" rule).
- Cookie consent banner (we don't track).
- Newsletter signup form.
- Live chat widget.
- A/B testing harness.
- Multi-language support.
- Blog (would need ongoing content; not the right time).

---

## Things I Will Not Compromise On

1. The live preview must match Windows rendering. If it doesn't match, we don't ship.
2. Templates are curated, not customizable beyond content. No freeform canvas. No font picker. No color wheel for notification text.
3. The 8px grid applies to everything. If a developer eyeballs spacing, I will know.
4. Character count validation is not optional. If Windows truncates text, our composer prevents it.
5. The notification preview panel uses Segoe UI. The rest of the app uses Inter. These are different contexts and I will fight anyone who tries to unify them.

---

## M12 — Device Appearance (Desktop Overlay + Lock Screen Branding)
**Author**: Diana Reyes
**Status**: Spec locked 2026-05-27. Governs M12 D6 (dashboard) and D7 (marketing).

Two separate concerns, two separate cards. Do not merge them into one "Device Appearance" mega-card. Some tenants want the overlay and not the lock screen, or the reverse. Coupling them in the UI implies they're one decision. They aren't.

Both cards live on `TenantSettings.tsx`, below the existing Branding / Notification Defaults / Rate Limits stack and the Content Moderation pointer. Order: **Desktop Overlay**, then **Lock Screen Branding**.

### Shared card rules (both cards)
- **Toggle is the card header control.** The enable/disable switch sits top-right in the card header row, aligned with the `<h2>`. Not a checkbox buried in the body.
- **Disabled ≠ hidden.** When the toggle is off, the body config stays *visible* but goes to a disabled visual state (`opacity: 0.5`, `pointer-events: none` on the config block, inputs `disabled`). The admin must be able to see what they configured without flipping it on. This is the inverse of the annoying "turn it on to see the settings" pattern — banned here.
- **Isolated Save state per card.** Each card owns its own Save button, spinner, success line, and error banner — independent of the main TenantSettings "Save Changes" button and of each other. Rationale: the overlay config and lock screen image are separate endpoints (`PUT /api/tenant/overlay`, `PUT /api/tenant/lockscreen` + upload). The shared TenantSettings save contract stays clean.
- 8px grid. 16px internal card padding minimum. Card spacing 24px (matches existing TenantSettings grid gap).
- No emojis. No purple. White-text-on-dark in the preview elements only — the dashboard chrome stays in the established dark theme tokens.

### Card 1 — Desktop Overlay

**Header**: `<h2>` "Desktop Overlay" + toggle (right-aligned). Sub-line under header in `--text-dim`, 12px: "A read-only info panel shown on the desktop. Does not change the user's wallpaper."  ← that sentence is load-bearing. It is the answer to the #1 question an admin will have. Keep it.

**Body, when enabled:**

1. **Fields** — section label "Show these fields" (`--text-secondary`, 13px, 500 weight). Six checkboxes, single column, 8px vertical rhythm. Labels exactly:
   - Hostname
   - Logged-in User
   - OS Version
   - IP Address
   - Tenant Name
   - Custom Text
   Checkbox control: square, 16px, accent-checked. No toggle switches for the field list — toggles are for the card-level on/off, checkboxes are for multi-select within. Consistency: switch = one binary state; checkbox = membership in a set.

2. **Custom Text inline reveal** — when "Custom Text" is checked, a single-line text input appears directly below the checkbox group (inline, not a separate section). Placeholder: "e.g. Property of Acme Corp — IT Support x4500". Max 80 chars with a live counter (consistent with the composer's character-count discipline). When unchecked, the input is removed from the DOM, not just hidden.

3. **Position** — section label "Position". A **four-button segmented control** (not a `<select>`): `Bottom Right` · `Bottom Left` · `Top Right` · `Top Left`. Default selected: Bottom Right. Active segment uses the accent fill; inactive segments are `--bg-tertiary` with `--text-secondary`.
   - Next to the segmented control, a **quadrant preview diagram**: a 16:9 rounded rectangle representing the screen (`--bg-tertiary` fill, 1px `--text-dim` border), with a small filled marker (`--accent-primary`, ~22% width) positioned in the selected corner with the spec corner inset (24px-equivalent). The marker moves as the segment changes. This is the only "preview" — we are NOT live-previewing the actual rendered text. The diagram communicates corner placement, nothing more. Subtle position transition (150ms ease) is allowed and serves a purpose; don't animate anything else.

4. **Save** — card-local "Save Overlay" button, right-aligned, primary style. Spinner + "Saved." success line + error banner scoped to this card.

**Rendered overlay appearance (governs the agent's GDI+ output, D4):**
- White text (`#FFFFFF`), drop shadow (1px offset, `rgba(0,0,0,0.8)`), over a semi-transparent dark rounded box (`rgba(0,0,0,0.6)`, ~6px corner radius, ~12px internal padding).
- Font: Segoe UI (this is the OS surface, same logic as the notification preview using Segoe UI — it's a Windows context, not an Inter context). Size DPI-aware, ~14–16px equivalent at 100% scale.
- Each field renders as `Label: Value` on its own line — e.g. `Hostname: AFNB-DESKTOP22`. Label in slightly dimmer white (`rgba(255,255,255,0.7)`), value in full white. Tenant Name and Custom Text render as a single value line with no "Label:" prefix.
- Corner inset from screen edge: 24px equivalent (taskbar-aware on the bottom corners — sit above the taskbar, do not let it cover the panel).

### Card 2 — Lock Screen Branding

**Header**: `<h2>` "Lock Screen Branding" + toggle (right-aligned). Sub-line in `--text-dim`, 12px: "A branded image shown when a device is locked (Win+L, screensaver, lid close)."

**Body, when enabled:**

1. **Upload zone + preview** — mirrors the existing logo upload pattern (`TenantSettings.tsx` logo block), but at **16:9** instead of square:
   - When an image is set: a 16:9 preview thumbnail (~240×135px), `object-fit: cover`, 1px `rgba(15,23,42,0.14)` border, `--radius-sm`, `--bg-tertiary` backing. `onError` → "Preview unavailable" fallback box (same treatment as the logo block).
   - Upload control: `<input type="file" accept=".jpg,.jpeg,.png">` hidden behind a `btn btn-secondary` "Upload image" / "Replace" label-button (matches logo upload exactly). Uploading state: "Uploading...", 0.6 opacity.
   - "Remove" `btn btn-ghost` when an image is set.

2. **Constraint helper text** — `--text-dim`, 11px, below the upload zone:
   - "Recommended: 1920 × 1080 (16:9). JPG or PNG. Max 5 MB."
   - Second line: "Applied to each device's lock screen at agent startup. On Group-Policy-managed endpoints, a policy-set lock screen may take precedence."

3. **Save** — card-local "Save Lock Screen" button. Note: the image upload commits immediately via `POST /api/tenant/lockscreen-image` (consistent with the logo upload, which persists on upload); the Save button commits the enabled toggle via `PUT /api/tenant/lockscreen`. Same dual-action shape as the existing logo + settings split — keep it consistent.

### D7 — Marketing surface (BgInfo-replacement positioning)
- **Lead message**: "Branded device info and lock screens, deployed from your dashboard — no login scripts, no GPO, no registry edits." The competitor is the *workflow* (BgInfo + scripting), not a named product. Do NOT name or disparage Sysinternals/BgInfo in customer-facing copy — sell the centrally-managed, no-scripting advantage. (Internal docs and `llms.txt` answering "BgInfo alternative?" queries are fine — that's discovery, not the hero pitch.)
- **No fake stats.** No "trusted by 10,000 endpoints" invention. Honest capability framing only (standing rule).
- **No AI-generated screenshots or slop.** If we show the overlay, it's a real screenshot from a real machine, or a clean CSS/SVG mock built to this spec — never a generative render. The quadrant diagram from Card 1 can be reused as a clean explanatory graphic.
- Capability grid entry on `Home.tsx` + inclusion-grid row on `Pricing.tsx` (reads as included, not an upsell). Docs reference under `/docs/*`. Inter for marketing chrome, Segoe UI only inside any literal overlay mockup.
- Banned-terms gate before commit: `persona`, `audio drama`, `jailbreak`, internal `M[0-9]+` codes, `ToastRevival` codename. Product name is **Toast Notification**.

### Diana's non-negotiables for M12
1. The overlay card's "Does not change the user's wallpaper" line ships. It is the whole reason this design exists instead of BgInfo's. Cutting it for space is not an option.
2. Two cards, never one. Overlay and lock screen are separate decisions.
3. Disabled state shows config, never hides it.
4. Position is a segmented control with a quadrant diagram — never a bare dropdown. The admin should *see* where it lands.
5. Overlay text is white + drop shadow over a translucent dark box. No color picker. If a tenant wants brand-colored overlay text, that's a future request with a real design conversation — not a hex input bolted on now.
6. The lock screen upload reuses the logo upload pattern exactly, at 16:9. Don't invent a second upload UX.
