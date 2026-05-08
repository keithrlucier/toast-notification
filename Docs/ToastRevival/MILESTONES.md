# ToastRevival — Milestones

## M0A: Signed Toast Agent Spike  **[COMPLETE 2026-05-07]**
**Goal**: Prove the smallest possible Windows endpoint path before platform work begins.

### Deliverables (all closed)
- **D1** [x]: .NET SDK 8.0.420, Windows App SDK 1.7.250310001, WiX 5.0.2, Thales token + Sectigo OV cert installed.
- **D2** [x]: `src/ToastRevival.Agent` on `net8.0-windows10.0.19041.0`.
- **D3** [x]: Agent displays toast notifications - 7 templates (plain + 6 DESIGN-SPEC templates) via `AppNotificationBuilder`.
- **D4** [x]: MSI built locally via WiX 5 (`installer/ToastRevival.Agent.Setup.wxs`).
- **D5** [x]: Signed with Sectigo OV cert via Thales hardware token (timestamp valid past cert expiry).
- **D6** [x]: Installed on clean Win11 lab machine - no issues.
- **D7** [x]: Agent runs in logged-in user context via Startup-folder shortcut.
- **D8** [x]: Toast fires after reboot (lab machine confirmed).
- **D9** [x]: Evidence captured - `EVIDENCE/2026-05-07-m0a-*.md` (4 entries).

### Resolved Research
- Signing flow: Thales hardware token + Sectigo OV cert works cleanly from the dev workstation; Keith handles the PIN/middleware.
- COM activator / Store identity: not needed for the first spike. Unpackaged Windows App SDK 1.7 works fine for unsigned dev runs and for the per-machine MSI install.
- Packaging path: MSI first (this milestone). MSIX comes in M0 D2.
- Per-user run mechanism: Startup-folder shortcut (all-users) for M0A. Scheduled Task in user context is a more robust alternative for MSP-managed endpoints and is M0 D3.

---

---

## M0: Foundation & Deployment Validation
**Goal**: Prove the deployment model works on real enterprise images before writing product code.

### Deliverables
- **D1** [x]: Skeleton WinUI 3 / .NET 8 app that displays a hardcoded toast notification on launch (closed under M0A — same agent project carried forward)
- **D2** [x **COMPLETE 2026-05-08**]: MSIX package built, signed with OV cert, installs cleanly on Windows 11 lab; visible toast fires from packaged context, button-click routes through NotificationInvoked handler.
  - Build complete 2026-05-07 (initial 0.2.0.0). Signing tooling discovery + Publisher fix landed 0.2.0.1 same day.
  - Install validation 2026-05-08: 0.2.0.1 installed cleanly but `AppNotificationManager.Default.Register()` threw `COMException 0x80070490` (ERROR_NOT_FOUND) — DiagLog isolated the failure to the Register() call. Root cause: missing `Arguments="----AppNotificationActivated:"` on `<com:ExeServer>` (FIX-MSIX-004).
  - 0.2.0.3 patch shipped (commit `6e3495c`): Arguments token added, CLSID `7FA7762F-41EC-4D72-9F06-58964AB36FEA` retained. Signed + installed on Win11 lab; single visible toast fired, "Acknowledge" button click routed cleanly to `NotificationInvoked` handler with the expected argument payload (`action=acknowledge;source=m0a;template=Plain`). See `EVIDENCE/2026-05-07-m0-d2-msix-build.md`, `2026-05-08-m0-d2-fix-msix-004-patch-build.md`, `2026-05-08-m0-d2-fix-msix-004-register-not-found.md`, `2026-05-08-m0-d2-toast-fires-packaged.md`.
  - Win10 1809 install validation deferred to M0 D4 GPO matrix (no Win10 1809 lab machine on hand).
- **D3** [x **COMPLETE 2026-05-08**]: MSI wrapper installs the agent + registers `\Toast2IT\ToastNotificationAgentLogon` Scheduled Task at install (replaces M0A's Startup-folder shortcut). Task is logon-triggered with BUILTIN\Users group principal (`S-1-5-32-545`) at `LeastPrivilege`; action runs `%ProgramFiles%\Toast Notification\ToastNotification.Agent.exe --template alert --no-wait`. WiX 5 deferred custom actions invoke `[System64Folder]schtasks.exe` with `/Create /XML /F` (after InstallFiles, condition `NOT REMOVE`) and `/Delete /F` (before RemoveFiles, condition `REMOVE="ALL"`, `Return="ignore"` for idempotency). Pre-install verification clean (XML parses, MSI Custom Action table correct Type 3106/3170, payload byte-identical repo → cab → admin-install extract). Lab install verified 2026-05-08: signed `ToastNotification.Agent-0.3.0.0.msi` installed cleanly on Win11 lab; `Get-ScheduledTask -TaskPath '\Toast2IT\' -TaskName 'ToastNotificationAgentLogon'` returned `State=Ready` with `MSFT_TaskLogonTrigger` and `MSFT_TaskExecAction`; alert toast fired at next user logon (Critical scenario, hero + logo + Acknowledge/Report buttons all rendered correctly). See `EVIDENCE/2026-05-08-m0-d3-msi-build-with-scheduled-task.md` and `EVIDENCE/2026-05-08-m0-d3-task-fires-at-logon.md`.
- **D4** [x **COMPLETE 2026-05-08**]: Verified scheduled task on Win11 lab: MSI 0.3.1.0 installs cleanly, task State=Ready, toast fires, second local user account also receives toast (BUILTIN\Users principal confirmed), uninstall removes task cleanly. FIX-MSIX-002 applied (manifest MinVersion aligned with runtime gate). GPO/Intune testing deferred: GPO standing rules documented in CONTEXT.md, domain/Intune carry to M8 beta. See `EVIDENCE/2026-05-08-m0-d4-matrix-results.md`.
- **D5**: Verify Store submission pipeline — push skeleton app to the existing 9P5L0MRMFRRF listing (private/hidden flight)
- **D6**: Document deployment findings and any fallback mechanisms needed

### Open Research
- Does the scheduled task approach work under restrictive GPOs?
- Can we submit to the existing Store listing after accepting the updated Developer Agreement?
- What's the minimum Windows 10 build version for WinUI 3 + Windows App SDK?

### Agent Deployment
- Anthony: D1-D3 (system-level, requires deep understanding of WinUI 3 packaging)
- Abish: D4 (bounded verification task — run through GPO test matrix, document results)
- Carl: D5-D6 (Store submission requires Partner Center access coordination with Keith)

---

## M1: Backend API — Core Infrastructure  **[COMPLETE 2026-05-08]**
**Goal**: Functioning multi-tenant API with auth, device registration, and notification send.

### Deliverables (all closed)
- **D1** [x]: ASP.NET Core 8 project `src/ToastRevival.Api` scaffolded with EF Core 8 + Npgsql + ASP.NET Identity. `ToastRevival.sln` updated. Build: 0 warnings, 0 errors.
- **D2** [x]: Multi-tenant data model — `Tenant`, `AppUser`, `Device`, `DeviceGroup`, `DeviceGroupMember`, `NotificationTemplate`, `Notification`, `NotificationDelivery`, `AssetLibrary`, `AuditLog`. EF Core global query filters enforce per-tenant isolation on all reads. `InitialCreate` migration generated.
- **D3** [x]: JWT auth — `POST /api/auth/register` (tenant + admin user, transactional), `POST /api/auth/login`. `TokenService` issues user tokens (60 min) and device tokens (365 days). `ITenantProvider`/`HttpContextTenantProvider` resolves tenant from JWT claim.
- **D4** [x]: `POST /api/devices/register` (unauthenticated) — agent registers, receives JWT device token. `GET /api/devices`, `GET /api/devices/{id}`, `DELETE /api/devices/{id}` (decommission), `POST /api/devices/ping` (heartbeat). SHA-256 token hash stored; raw token transmitted once.
- **D5** [x]: `POST /api/notifications` — create notification, expand device/group/all targets, create per-device `NotificationDelivery` records, enqueue. `GET /api/notifications` (history), `GET /api/notifications/{id}`. `NotificationQueueService` background service pushes via SignalR.
- **D6** [x]: `NotificationHub` at `/hubs/notifications` — JWT-authenticated, tenant-scoped groups (`tenant-{id}`, `device-{id}`). `ReportDelivery` / `ReportInteraction` hub methods. `ConnectedDevices` registry for presence tracking.
- **D7** [x]: Built-in ASP.NET Core rate limiting — `tenant-per-minute` sliding window (60 req/min, partition by tenantId), `device-per-hour` fixed window (10 req/hr, partition by deviceId). Applied via `[EnableRateLimiting]` attributes.
- **D8** [x]: `AuditService` writes `AuditLog` entries on all device registration, device decommission, and notification send events. Scope-factory pattern (works from background service context).

### Code Sweep
- Commit (pending): SHIP WITH NOTES — INFO-M1-001 through INFO-M1-006 logged (see FIX-LIST.md).
- INFO-M1-002 (transaction gap in Register) fixed before commit.

### Agent Deployment
- Anthony: D1-D3, D6 ✓
- Abish: D4-D5, D7-D8 (implemented inline with Anthony's track) ✓

---

## M2: Windows Agent — Full Implementation
**Goal**: Production-ready Windows agent that connects to backend and renders notifications.

### Deliverables
- **D1**: SignalR client integration — connect to backend hub, auto-reconnect, exponential backoff
- **D2**: Toast notification rendering — receive payload from SignalR, render via WinRT APIs, all template types
- **D3**: Device registration flow — first-run registration with backend, token storage, heartbeat/ping
- **D4**: Payload verification — HMAC signature check before rendering
- **D5**: Notification interaction tracking — clicks, dismissals, button actions → report back to backend
- **D6**: Missed notification catch-up — on reconnect, fetch and display any notifications sent while offline
- **D7**: System tray icon with status indicator (connected/disconnected/error)
- **D8**: Auto-update mechanism — Velopack integration for MSI-deployed instances, Store-deployed use Store updates
- **D9**: Installer refinement — MSI properties (CLIENTID, SERVERURL), silent install/uninstall, upgrade path

### Agent Deployment
- Anthony: D1-D4, D6 (SignalR + security + reconnection — system-level complexity)
- Abish: D5, D7-D9 (interaction tracking is bounded, tray icon is UI, installer is configuration)

---

## M3: Content Moderation & Security Hardening
**Goal**: Production security posture — content moderation, broadcast controls, payload signing.

### Deliverables
- **D1**: Azure Content Safety integration — text moderation on all notification content
- **D2**: Azure Content Safety integration — image moderation on ad-hoc uploads (skip approved library assets)
- **D3**: Moderation decision engine — PASS/REVIEW/BLOCK based on severity thresholds
- **D4**: Admin approval queue — UI for reviewing flagged notifications (REVIEW status)
- **D5**: Broadcast gate — notifications targeting > N devices require elevated permission + confirmation dialog
- **D6**: MFA enforcement for Super Admin actions (broadcast, user management, tenant settings)
- **D7**: Tenant-configurable blocklists — custom banned terms per tenant
- **D8**: HMAC payload signing — server signs, agent verifies
- **D9**: Security audit of all API endpoints — authentication, authorization, input validation, tenant isolation

### Agent Deployment
- Anthony: D1-D3, D5, D8-D9 (moderation pipeline, security audit — requires system-level judgment)
- Abish: D4, D6-D7 (approval queue UI, MFA integration, blocklist CRUD — bounded tasks)

---

## M4: Admin Dashboard — Core UI
**Goal**: Functional web dashboard for notification management.

### Deliverables
- **D1**: React + TypeScript project scaffolding, auth flow (login/register), tenant-scoped routing
- **D2**: Device inventory page — list, search, filter, status indicators, group management
- **D3**: Notification template gallery — 6 curated templates (Announcement, Alert, Action Required, Reminder, Celebration, Maintenance)
- **D4**: Notification composer — select template, fill fields, character-count validation
- **D5**: Live notification preview — dark panel showing pixel-accurate Windows toast rendering, updates in real-time as user types
- **D6**: Send/schedule interface — target selection (device/group/all), schedule for later, broadcast confirmation
- **D7**: Notification history — sent notifications with delivery status, click-through rates
- **D8**: Diana design review and iteration

### Agent Deployment
- Anthony: D1-D2, D5-D6 (scaffolding, real-time preview engine, send flow — core architecture)
- Abish: D3-D4, D7 (template gallery is bounded component work, history is data display)
- Diana: D3, D5, D8 (template visual design, preview accuracy, full design review)

---

## M5: Admin Dashboard — Advanced Features
**Goal**: Complete dashboard with analytics, user management, tenant settings.

### Deliverables
- **D1**: Delivery analytics dashboard — send volume, delivery rates, interaction rates, trends over time
- **D2**: User management — invite users, assign roles, MFA status, activity log
- **D3**: Tenant settings — branding (logo, colors), notification defaults, rate limit configuration
- **D4**: Asset library — upload/manage hero images and logos, moderation status indicators
- **D5**: API key management — generate/revoke per-tenant API keys for programmatic access
- **D6**: Notification scheduling — calendar view, recurring notifications, timezone handling
- **D7**: Export — audit logs, delivery reports (CSV/PDF)

### Agent Deployment
- Anthony: D1, D6 (analytics requires data aggregation design, scheduling has timezone complexity)
- Abish: D2-D5, D7 (CRUD interfaces, bounded feature work)
- Diana: D1, D3-D4 (analytics visualization, branding UI, asset library UX)

---

## M6: Licensing & Subscription System
**Goal**: Commercial-ready licensing with Stripe integration.

### Deliverables
- **D1**: Subscription tiers — Free (10 devices), Pro (250 devices), Enterprise (unlimited)
- **D2**: Stripe integration — subscription creation, billing portal, webhook handling
- **D3**: License enforcement — device registration blocked when limit reached, grace period handling
- **D4**: Usage metering — device count tracking, overage alerts
- **D5**: Tenant onboarding flow — signup, plan selection, first device registration walkthrough
- **D6**: Admin billing page — current plan, usage, invoices, upgrade/downgrade

### Agent Deployment
- Anthony: D2-D3 (Stripe integration + license enforcement — payment code must be precise)
- Abish: D1, D4-D6 (tier definitions, metering, onboarding flow — bounded tasks)

---

## M7: Marketing Site — toastnotification.com Redesign
**Goal**: Professional marketing site that sells the product.

### Deliverables
- **D1**: Diana DESIGN-SPEC for marketing site (shared design system with dashboard)
- **D2**: Build Mode execution — hero, features, pricing, testimonials, documentation
- **D3**: Pricing page — transparent per-device pricing, tier comparison
- **D4**: Documentation — getting started guide, deployment guides (Store/Intune/RMM), API docs
- **D5**: SEO optimization — meta tags, structured data, sitemap
- **D6**: Deploy to toastnotification.com (existing domain)

### Agent Deployment
- Diana: D1 (design spec — must be done before build)
- Build Mode: D2-D5 (marketing site is a Build Mode project)
- Carl: D6 (deployment coordination)

---

## M8: Integration Testing & Beta
**Goal**: End-to-end testing across all deployment channels, beta program launch.

### Deliverables
- **D1**: End-to-end test: Store install → register → receive notification → interact → verify delivery tracking
- **D2**: End-to-end test: MSI/RMM install → same flow
- **D3**: End-to-end test: Intune LOB deploy → same flow
- **D4**: Load testing — 1,000 concurrent agents, notification blast, measure delivery latency
- **D5**: Security penetration testing — tenant isolation, auth bypass, content injection, privilege escalation
- **D6**: Beta program — invite 3-5 MSP partners for real-world testing
- **D7**: Bug fix cycle based on beta feedback

### Agent Deployment
- Anthony: D1-D3, D5 (end-to-end requires system understanding, security testing requires judgment)
- Abish: D4, D6-D7 (load testing is scripted, beta coordination is process work)

---

## M9: Launch
**Goal**: Public launch with Store submission, production infrastructure, and marketing.

### Deliverables
- **D1**: Production infrastructure — Azure/AWS deployment, monitoring, alerting, backups
- **D2**: Store submission — update 9P5L0MRMFRRF with production build, expand to all markets
- **D3**: RMM deployment packages — documented scripts for NinjaOne, Datto, ConnectWise
- **D4**: Launch marketing — email to beta users, social media, MSP community posts
- **D5**: Support system — ticketing, knowledge base, SLA documentation
- **D6**: SOC 2 preparation — document controls, begin audit process

### Agent Deployment
- Carl: D1-D2 (infrastructure + Store submission — operational ownership)
- Anthony: D3 (RMM packages require testing on actual RMM tools)
- Abish: D4-D6 (marketing coordination, documentation, compliance prep)

---

## Milestone Summary

| Milestone | Focus | Key Risk | Est. Complexity |
|---|---|---|---|
| M0 | Deployment validation | GPO restrictions on scheduled tasks | Medium |
| M1 | Backend API core | Multi-tenant data isolation correctness | High |
| M2 | Windows agent | SignalR reconnection + offline catch-up | High |
| M3 | Security & moderation | Moderation accuracy vs. false positives | Medium |
| M4 | Dashboard core UI | Live preview accuracy matching Windows rendering | High |
| M5 | Dashboard advanced | Timezone handling for scheduling | Medium |
| M6 | Licensing & billing | Stripe webhook reliability, edge cases | Medium |
| M7 | Marketing site | Design consistency with dashboard | Low |
| M8 | Testing & beta | Real-world deployment diversity | High |
| M9 | Launch | Store certification, infrastructure hardening | Medium |
