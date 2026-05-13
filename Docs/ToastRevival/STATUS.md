# Toast Notification - Current Status

> Repo / project codename: **ToastRevival** (internal). Product / user-facing brand: **Toast Notification** (toastnotification.com).

Last updated: 2026-05-13

## Project State

**M11 D7 pricing page rewrite shipped (2026-05-13).** Public `/pricing` is now on the locked M11 three-tier model — Free Trial (2 devices, 14 days, reviewed), Managed SaaS ($22/month flat up to 100 devices), Roll Your Own (Docker Compose self-host, $0, no device cap). Origin story (2020 MSP COVID build, 986k notifications, 17 tenants) leads. Three equal-weight tier cards — no "Most Popular" badge, no comparison table, no urgency CTAs. RYO card links to https://github.com/keithrlucier/toast-notification. Home.tsx pricing teaser converted to matching `m-tier-grid` 3-card layout; legacy ✓ checkmark Unicode glyphs removed from UI bullets. Crawler/LLM surfaces aligned: Llms.tsx + llms.txt pricing facts rewritten; prerender-seo.mjs route bodies and AggregateOffer JSON-LD shapes updated; seo.ts `softwareApplicationLd` and `pricingProductLd` rewritten to declare 3 Offers (Free Trial $0, Managed SaaS $22, RYO $0) with corrected eligibleQuantity 0–100 on the paid tier. Build clean. Grep verification: zero remaining "26-100" / "101-200" / "$44/month" / "fleet block" patterns in dist. Code Sweep #41 (Abish) SHIP verdict.

**Trial approval gate + tenant install surface added (2026-05-12).** Public `/register` is now a reviewed trial request instead of direct tenant creation. The request collects company, website, contact, phone, job title, intended use case, notes, IP/user-agent, and Cloudflare Turnstile verification metadata before storing a pending `TrialRequest`. Platform admins can review pending/approved/rejected requests at `/system/trial-requests` and approve into a tenant-owner account with a password setup email. Legacy direct registration is disabled by default outside test config. Tenant admins now have a first-class `/devices/install` page with tenant ID, server URL, MSI download URL, and a prefilled `msiexec` deployment command using the tenant enrollment key.

**Marketing / SEO / LLM discovery refreshed (2026-05-12).** Public content now describes concrete MSP and software use cases: maintenance windows, security incident response, help desk follow-through, MSP client operations, branded Windows notifications, delivery evidence, action buttons, and RMM/Intune/MSI deployment. Pricing and docs copy now reflect reviewed access and current block pricing instead of direct free signup language. `llms.txt`, route metadata, JSON-LD, prerender output, sitemap dates, and public docs were updated with the same facts.

**Brand color updated (2026-05-11).** Accent color flipped from teal `#00C9A7` to amber `#F59E0B` across all design tokens, chart specs, and marketing site references.

**Pricing model updated (2026-05-11).** Per-device billing replaced with block pricing: Free 1–25 devices ($0), Standard 26–100 ($22/mo flat), Growth 101–200 ($44/mo flat), Enterprise 200+ (contact us).

**M0A: COMPLETE (2026-05-07).** Signed MSI installs cleanly on a Win11 lab machine, agent runs in user context, toast survives login and reboot. Brand on every user-facing surface flipped from project codename to product name.

**M0 D2: COMPLETE (2026-05-08).** Signed MSIX (`ToastNotification.Agent-0.2.0.3.msix`, commit `6e3495c`) installs cleanly via Add-AppxPackage on Win11 lab; single visible toast fires from packaged Start menu launch; "Acknowledge" button click routes through `NotificationInvoked` handler with expected argument payload (`action=acknowledge;source=m0a;template=Plain`). FIX-MSIX-004 resolved: missing `Arguments="----AppNotificationActivated:"` on `<com:ExeServer>` was causing `AppNotificationManager.Default.Register()` to throw `COMException 0x80070490` (ERROR_NOT_FOUND). DiagLog file-based diagnostic logging added in 0.2.0.2 (commit `eca31dc`) was what made the failure point isolatable in a single install cycle. Toast Activator CLSID locked at `7FA7762F-41EC-4D72-9F06-58964AB36FEA` for the lifetime of this product.

See `EVIDENCE/2026-05-07-m0-d2-msix-build.md`, `-publisher-fix.md`, `-signed.md`, `2026-05-08-m0-d2-fix-msix-004-patch-build.md`, `2026-05-08-m0-d2-fix-msix-004-register-not-found.md`, `2026-05-08-m0-d2-toast-fires-packaged.md`.

**M0 D3: COMPLETE (2026-05-08).** Signed `ToastNotification.Agent-0.3.0.0.msi` installed cleanly on Win11 lab. `Get-ScheduledTask -TaskPath '\Toast2IT\' -TaskName 'ToastNotificationAgentLogon'` returned `State=Ready` with `MSFT_TaskLogonTrigger` and `MSFT_TaskExecAction`. Alert toast fired at next user logon — Critical-scenario banner with hero/logo/Acknowledge/Report buttons all rendered correctly. Per-machine MSI now uses a logon-triggered Scheduled Task in the BUILTIN\Users group context (`S-1-5-32-545` at `LeastPrivilege`) instead of M0A's all-users Startup-folder shortcut. Better GPO/Intune story for the MSI/RMM channel. Standing rules in CONTEXT.md "Scheduled Task primitive (M0 D3 standing rules)" production-validated. See `EVIDENCE/2026-05-08-m0-d3-msi-build-with-scheduled-task.md` and `EVIDENCE/2026-05-08-m0-d3-task-fires-at-logon.md`.

**CI: GitHub Actions on github-hosted runner (2026-05-09, commit `1c41d3e`).** Workflow `.github/workflows/agent-build.yml` switched from self-hosted `toast-build` runner (on the now-decommissioned Windows VM 52.21.249.120) to `windows-latest` GitHub-hosted runner. Installs WiX via `dotnet tool install -g wix`. Builds unsigned MSI on every push to main and uploads as artifact. No self-hosted runner dependency remains.

**M0 D4: COMPLETE (2026-05-08).** MSI 0.3.1.0 signed and installed on Win11 lab; task State=Ready; toast fires; second local user account also received toast (BUILTIN\Users confirmed); uninstall removed task cleanly. FIX-MSIX-002 applied (manifest MinVersion 17763→19041). GPO/Intune testing deferred: behavior documented in CONTEXT.md, carry to M8 beta. See `EVIDENCE/2026-05-08-m0-d4-matrix-results.md`.

**M0 D5: CLOSED 2026-05-09 — Store listing live.** MSIX `ToastNotification.Agent-0.2.1.0.msix` was signed by Keith (Thales + Sectigo OV cert) and flighted to Partner Center listing **9PFD6004DVTN**; package passed certification and the listing is live at https://apps.microsoft.com/detail/9PFD6004DVTN. Three code changes shipped during the build: (1) `uap5:StartupTask` extension added to manifest (MSIX/Store logon-launch parity with MSI Scheduled Task channel); (2) `TargetPlatformVersion=10.0.22621.0` baked into `build-msix.ps1` via command-line flag (FIX-MSIX-001 resolved — csproj PropertyGroup approach silently failed due to TFM override in late .targets import); (3) CONTEXT.md standing rules updated with discovery. Produced manifest verified: `MinVersion=10.0.19041.0`, `MaxVersionTested=10.0.22621.0`, all three extensions present. Diana's curated tile assets remain a deferred polish item before broader market expansion.

**M1: COMPLETE 2026-05-08.** `src/ToastRevival.Api` — ASP.NET Core 8 / EF Core 8 / Npgsql / ASP.NET Identity / SignalR. All 8 deliverables shipped: scaffolding, multi-tenant data model with global query filters, JWT auth with user+device tokens, device registration API, notification send API with queue background service, SignalR hub with tenant/device groups, rate limiting, audit logging. `InitialCreate` migration generated. Build: 0 warnings, 0 errors. INFO-M1-001 through INFO-M1-006 logged (see FIX-LIST.md).

**M2.A: COMPLETE 2026-05-09.** Agent ↔ Backend pipeline + HMAC. Carl sliced M2 at orientation. M2.A delivered D1 SignalR client + auto-reconnect `[0,2,5,10,30]`s, D2 toast rendering from backend payload (`ToastTemplateBuilder.BuildFromPayload` extends the M0A argv builder — no fork), D3 first-run device registration + atomic-write `DeviceConfig` storage + 30-min REST heartbeat ping, D4 HMAC-SHA256 payload verification (`Tenant.SigningKey` + `CryptographicOperations.FixedTimeEquals` constant-time compare), D5 `ReportDelivery` + `ReportInteraction` over the hub, plus INFO-D5-001 (session-local named mutex `Local\Toast2IT.ToastNotification.PrimaryWorker`) and INFO-MSIX-004-D (activation-handler short-circuit before SignalR + REST `/api/notifications/{id}/interactions` fallback endpoint). Agent grew 3 new files (`AgentClient.cs`, `DeviceConfig.cs`, `ToastPayload.cs`) and refactored `Program.cs` to a 3-mode entry (Activation / Diagnostic / Primary). Backend gained `Tenant.SigningKey` column + `AddTenantSigningKey` migration with backfill, payload signing in `NotificationQueueService.BuildSignedPayload`, and the new REST interaction endpoint. **Code Sweep returned SHIP WITH NOTES; FIX-M2A-001 patched pre-commit** (mutex prefix `Global\` → `Local\` to prevent multi-user-session collision regressing M0 D4 verification). 4 INFO items deferred (INFO-M2A-002 → M3, INFO-M2A-003/004 → M2.B, INFO-M2A-005 → M9). Build clean: 0 warnings + 0 errors solution-wide; MSIX smoke check intact (all M0 D5 manifest standing checks pass). See `EVIDENCE/2026-05-09-m2-a-agent-backend-pipeline.md`.

**M2.B: COMPLETE 2026-05-09.** Missed catch-up + orphan recovery + agent dedup. **M2.C: COMPLETE 2026-05-08.** System tray icon (WinForms STA thread, 5 states, Diana spec colors) + MSI CLIENTID/SERVERURL properties → bootstrap.json via SetupMode. **M2.D: COMPLETE 2026-05-08.** Velopack 0.0.1298 auto-update integration — TrySelfRedirect pattern, 24h background check loop, registry enterprise toggle, tray Update Available menu item, build-release.ps1 pipeline. **All of M2 is complete.**

**M3: COMPLETE 2026-05-08 (commit 362f9d3).** Security hardening — Azure Content Safety text+image moderation pipeline, admin approval queue, broadcast gates (TargetType.All requires MFA-elevated JWT), TOTP MFA (OtpNet), tenant blocklists, DPAPI config encryption, WinVerifyTrust Authenticode check in TrySelfRedirect, EF migration M3SecurityHardening.

**M4: COMPLETE 2026-05-08 (commit 016c4c9).** Admin Dashboard — Vite 6 + React 18 + TypeScript, full auth flow, device inventory, template gallery, notification composer, live Segoe UI preview panel, send/schedule UI with broadcast confirm modal, notification history.

**M5.A: COMPLETE 2026-05-09 (commit 437dce4).** Template API, DeviceGroups API, history pagination fix, User Management page, API Keys page, analytics chart spec from Diana.

**M5.B: COMPLETE 2026-05-09 (commit aabe739).** Delivery analytics (AnalyticsController + 3 aggregate endpoints + Recharts charts), Tenant Settings page (branding + notification defaults).

**M5.C: COMPLETE 2026-05-09 (commit 6bd5cc7).** Asset Library (AssetsController, multipart upload, Azure Content Safety byte scan, UseStaticFiles), Notification Scheduling (PeriodicTimer 60s loop, startup backfill, ProcessAsync non-Queued guard).

**M5.D: COMPLETE 2026-05-09.** Export (D7) — AuditController (list + CSV/PDF export, admin-only), NotificationsController delivery report per-notification (CSV/PDF), PdfExportService (QuestPDF Community, A4 landscape audit + A4 portrait delivery), AuditLog.tsx admin page, History.tsx export button.

**M6: COMPLETE 2026-05-09 (commit 918c723).** Licensing & Subscription System — Stripe.net 47.3.0 integration (BillingController with checkout/portal/webhooks/invoices), per-tier device limits (Free=10, Pro=250, Enterprise=unlimited), license enforcement in DevicesController.Register, Stripe webhook event handling (checkout/subscription/invoice events), Onboarding.tsx 3-step wizard, Billing.tsx admin page. Migration M6Billing adds StripeCustomerId/StripeSubscriptionId/PastDueAt and IX indexes on AuditLogs.Timestamp + Notifications(Status, ScheduledAt). Stripe price IDs are placeholders in appsettings.json — production must override via env vars (INFO-M6-001).

**M7.A: COMPLETE 2026-05-09 (commit ca0972a).** Marketing site DESIGN-SPEC delivered (Diana, ~480 lines locked) + onboarding emoji→SVG swap closed INFO-M6-004. Four hand-coded React SVG components at `src/ToastRevival.Dashboard/src/icons/onboarding/index.tsx` (Bell, Template, Package, Launch). Pre-commit security fix: `*.pem` and `*.key` patterns added to `.gitignore` after Abish caught two Lightsail SSH keys at `Docs/Assets/` that were untracked but not ignored.

**M7.B Corporate Edition: COMPLETE 2026-05-09 (commit bb95779).** Marketing site rebuilt corporate-grade — single-plan pricing ($0.22/device + 100-device floor + 14-day trial), real product screenshot in hero, problem/solution comparison, capability matrix, security architecture, indicative-cost-by-fleet-size table, 6-card inclusion grid, 8-question FAQ. `HowWeBuiltIt.tsx` and `DocsComingSoon.tsx` deleted per Keith's standing rules. Cross-AI handoff: 25-file Codex Pricing v2 / PlatformAdmin / admin-UI WIP preserved unstaged via selective-commit pattern; `Docs/ToastRevival/CODEX-HANDOFF.md` shipped as the comms surface.

**M7.C: COMPLETE 2026-05-09.** Public docs hub at https://toastnotification.com/docs. Six lazy-loaded routes — `/docs` overview, `/docs/getting-started`, `/docs/deploy/{store,intune,rmm}`, `/docs/api`. `DocsLayout` (240px sticky sidebar at top:80px, mobile collapsible nav with 44px touch targets), `CodeBlock` (copy-to-clipboard with content-type label), `Callout` (note green / warning amber). Endpoint table on `/docs/api` with method pills (POST / GET) in JetBrains Mono. MarketingHeader `Docs` link, MarketingFooter `Resources` column (slim grid widened 3→4 cols). Cross-AI handoff pattern reused for the second consecutive milestone — Codex's full WIP set (39 modified + 5 untracked, including `index.css` +405 light-mode tokens, `SystemController.cs`, `BillingPlanRules.cs`, `PlatformAdminBillingV2` migration) preserved unstaged. Code Sweep SHIP WITH NOTES (INFO-M7C-001/002/003/005, FIX-M7C-001 mobile-touch patched pre-commit). Diana sign-off APPROVED. See `EVIDENCE/2026-05-09-m7c-docs-hub.md`.

**M7.B (legacy paused entry): RESOLVED above by Corporate Edition.** Build Mode session for marketing Home / Pricing / How We Built It pages was started, then paused per Keith. Visual rendering came out flat against the locked spec (hero placeholder frame, generic line icons, internal milestone codes leaking into customer-facing copy). Standing rule established: DocPro/AI-built case study NEVER appears on public marketing surfaces — llms.txt + internal .md files only. Standing rule established: internal milestone codes (M0A–M9, "roadmap", "publishes shortly") MUST NOT appear in any user-facing string. Standing rule established: placeholder pages (coming-soon stubs) do not ship — if a feature isn't ready, it's not in nav, not routed, not referenced. Stash: `M7.B WIP — paused 2026-05-09 per Keith; revisit with real screenshots + tighter visual brief`.

**FIX-PROD-001: RESOLVED 2026-05-09.** Production `/register` (and every other dashboard route) was rendering as a blank white page. Root cause: nginx `location /assets/ { proxy_pass http://localhost:5216 }` block (added for the M5.C asset library to serve user-uploaded hero/logo files from ASP.NET) collided with Vite's default build output directory (`dist/assets/index-*.js`). Every SPA bundle request was proxied to ASP.NET, which had no route → 404, so the SPA could never bootstrap. Fix: Vite `build.assetsDir = 'static'` in `vite.config.ts`. SPA bundles now output to `/static/*` and are served by the SPA fallback `try_files`. Asset library `/assets/{tenantId}/*` proxy unchanged. Deployed 2026-05-09. Verified externally: `/register` and `/login` render with zero console errors. See `EVIDENCE/2026-05-09-fix-prod-001-static-assets-dir.md`.

**FIX-PROD-002: RESOLVED 2026-05-09.** Register flow had four independent bugs stacked, all hidden behind the boilerplate "One or more validation errors occurred." banner: (1) frontend payload field names didn't match backend DTO (`adminEmail`/`adminPassword` vs `Email`/`Password`); (2) backend required a `Subdomain` field the UI never collected; (3) backend `AuthResponse` was missing `Email` even though frontend reads it; (4) frontend `client.ts` extracted only the ProblemDetails title, not the per-field `errors` map. Latent since M1 shipped 2026-05-08 — the register endpoint had **never** worked end-to-end. Fix: backend `Subdomain` now optional and auto-derived from `TenantName` via slugify; `AuthResponse` includes `Email`; frontend payload uses correct field names; `client.ts` walks the ProblemDetails `errors` map before falling back to the title. Verified: curl + Playwright UI walkthrough on production complete the register → dashboard flow with zero console errors. Two smoke-test tenants from verification were transactionally cleaned from prod DB. See `EVIDENCE/2026-05-09-fix-prod-002-register-flow.md`. **New standing rule (Carl):** every milestone that ships a frontend → backend interaction MUST include curl-with-real-payload + Playwright UI smoke against the deployed environment before close.

**PRODUCTION LIVE 2026-05-09.** https://toastnotification.com deployed to AWS Lightsail 2-box setup. HTTPS via Let's Encrypt (auto-renews). React dashboard + ASP.NET Core 8 API on TOASTWEB1 (54.82.103.160). PostgreSQL 16 on TOASTDATA1 (172.26.3.164 private). EF migrations ran clean on first startup. SSH keys at `Docs/Assets/`. **First-time admin signup at https://toastnotification.com/register** — register endpoint creates the tenant, makes the registrant admin, seeds the 6 default templates.

**Microsoft Store Partner Agreement complete (2026-05-10). Listing 9PFD6004DVTN is 100% live and passing all Store certification checks. Two deferred polish items remain before broader marketing push: (1) branding — Diana curated tile assets; (2) Store copy — current listing description needs a rewrite against the locked marketing voice. End-to-end testing not yet completed (deferred to M8 beta).**

**INFO-MSIX-004 closed (2026-05-10).** DiagLog rotation (512 KB cap, rolls to `agent.log.1`) added to `DiagLog.Write()`. `--diag` flag gate added: `ToastNotification.exe --diag` dumps the log path and last 200 lines to stdout. Works before the elevation check so support staff can run it as admin. Dispatch entry added between `--setup-bootstrap` and the elevation gate in `AgentEntryPoint.RunAsync`. `DiagMode` class lives alongside `DiagLog` in `Program.cs`.

**Bucket 3 cleanup complete (2026-05-10).** INFO-M8C-001: hub tenant-isolation test replaced `Task.Delay(500ms)` with a 20ms predicate-poll (300ms timeout) — fails fast when isolation is broken, removes the fixed 500ms wall on the fast path. INFO-M7C-003: docs route paths extracted to `src/routes/docsRoutes.ts` (`DOCS_PATHS` const); both `App.tsx` and `DocsLayout.tsx` now import from the shared file — nav paths and router definitions can no longer drift. INFO-M9C-002: `DeployCommand` enrollment-key fetch is now module-level cached; `/api/tenant/settings` is called at most once per page load regardless of mount count.

**Home page redesigned and deployed (2026-05-10, commit a6b658c).** New layout: technical hero with copy-left / RMM code-block-right split, terminal msg.exe vs. Toast Notification comparison, bento grid for platform architecture (4 cards, 8/4 column split), accurate two-card pricing (free ≤25 devices / standard $0.22/device starting at 26). No fake stats, no trial claim. `bento.css` new stylesheet imported via `marketing.css @import`.

**Codex: no open tasks as of 2026-05-10.**

**Next: M11 close (D1–D7 all shipped) → deploy aligned marketing surfaces to production (toast-api unchanged; `cd dashboard && npm run build` + nginx static swap) → M8 beta (integration testing + closed beta), M9 (launch). Store copy + Diana tile assets deferred polish before M9 marketing push.**

## M0A Deliverables - All Closed

- D1: Dev machine has .NET SDK 8.0.420 + Windows App SDK 1.7.250310001 + WiX 5.0.2 + Thales hardware token signing.
- D2: `src/ToastRevival.Agent` agent project on `net8.0-windows10.0.19041.0`.
- D3: Toast displayed - 7 templates (plain + 6 rich) wired through `AppNotificationBuilder`, hero/logo/scenario/audio/multi-button.
- D4: MSI built locally via WiX 5 (`installer/ToastRevival.Agent.Setup.wxs`, `scripts/build-msi.ps1`).
- D5: Signed with Sectigo OV cert via Thales hardware token. Signature valid, DigiCert-timestamped. Cert NotAfter 2027-04-15.
- D6: Installed on clean Win11 lab machine - no issues.
- D7: Agent runs in logged-in user context via Startup-folder shortcut (per-machine MSI, all-users startup).
- D8: Toast still fires after reboot (lab machine confirmed).
- D9: EVIDENCE entries: `2026-05-07-m0a-local-agent-spike.md`, `2026-05-07-m0a-rich-notification-spike.md`, `2026-05-07-m0a-close-signed-msi-install.md`, `2026-05-07-build-server-bootstrap.md`.

## Current Build Outputs

- `artifacts/installer/ToastRevival.Agent-0.1.0.0.msi` - signed, installed on lab machine. Pre-rebrand.
- `artifacts/installer/ToastNotification.Agent-0.2.0.0.msi` - rebranded user-facing surfaces. Same UpgradeCode -> MajorUpgrade replaces 0.1.0.0 cleanly. Awaiting re-sign before redeploy if needed.
- `artifacts/installer/msix/ToastNotification.Agent-0.2.0.1.msix` - signed; install validation revealed FIX-MSIX-004 (Register() ERROR_NOT_FOUND). Superseded.
- `artifacts/installer/msix/ToastNotification.Agent-0.2.0.2.msix` - DiagLog scaffolding + initial COM activator declarations. Superseded by 0.2.0.3 (Register still threw without Arguments token).
- **`artifacts/installer/msix/ToastNotification.Agent-0.2.0.3.msix` - 63.53 MB. Signed by Keith 2026-05-08, installed cleanly on Win11 lab; visible toast fires; NotificationInvoked routes cleanly. Current canonical M0 D2 build.**
- **`artifacts/installer/ToastNotification.Agent-0.3.0.0.msi` - 50.61 MB. Signed by Keith 2026-05-08, installed cleanly on Win11 lab; scheduled task created (State=Ready); toast fires at logon. Canonical M0 D3 build.**
- **`artifacts/installer/msix/ToastNotification.Agent-0.2.1.0.msix` - 63.82 MB. M0 D5 build. Three extensions: `windows.comServer`, `windows.toastNotificationActivation`, `windows.startupTask`. Manifest: MinVersion=10.0.19041.0, MaxVersionTested=10.0.22621.0. Signed + flighted to Microsoft Store 2026-05-09; listing 9PFD6004DVTN live.**

## Production Infrastructure (live 2026-05-09)

### TOASTWEB1 — Web / App Server
- **Public IP (static):** 54.82.103.160
- **Private IP:** 172.26.0.161
- **OS:** Ubuntu 22.04 LTS · AWS Lightsail 2 GB / 2 vCPU / 60 GB
- **Stack:** nginx 1.24 + ASP.NET Core 8 Kestrel (:5216) + React static files
- **Service:** `toast-api.service` (systemd, auto-restart, `toast` user)
- **App root:** `/opt/toast/api/` · `/opt/toast/dashboard/` · `/opt/toast/.env` (chmod 600)
- **TLS:** Let's Encrypt via certbot, auto-renews, expires 2026-08-07
- **SSH key:** `Docs/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem`
- **Connect:** `ssh -i "Docs/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem" ubuntu@54.82.103.160`
- **Logs:** `sudo journalctl -u toast-api -f`

### TOASTDATA1 — Database Server
- **Private IP:** 172.26.3.164 (no public access — DB port closed at Lightsail firewall)
- **Public IP (SSH only):** 100.52.96.67
- **OS:** Ubuntu 22.04 LTS · AWS Lightsail 1 GB / 2 vCPU / 40 GB
- **Stack:** PostgreSQL 16, database `toastrevival`, user `toast`
- **Accepts connections from:** 172.26.0.161/32 (TOASTWEB1 private IP) only
- **SSH key:** `Docs/Assets/Toast_Data_1_LightsailDefaultKey-us-east-1.pem`
- **Connect:** `ssh -i "Docs/Assets/Toast_Data_1_LightsailDefaultKey-us-east-1.pem" ubuntu@100.52.96.67`

### Decommissioned
- **AWS Windows VM 52.21.249.120** — was Codex's self-hosted CI runner. CI moved to GitHub-hosted `windows-latest` (commit `1c41d3e`). Terminate this instance.

## Local Environment Notes

- Git, .NET SDK `8.0.420` (pinned via `global.json`), Windows App SDK 1.7.250310001 via NuGet, WiX 5.0.2 (`dotnet tool install --global wix --version 5.*`).
- Repo-local `NuGet.config` for nuget.org.
- Windows App SDK CLI builds require `<EnableMsixTooling>true</EnableMsixTooling>`.
- MSIX build wrapped in `scripts/build-msix.ps1`. Smoke check command (must include TargetPlatformVersion flag): `dotnet build src\ToastRevival.Agent\ToastRevival.Agent.csproj -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:TargetPlatformVersion=10.0.22621.0`. Without the flag, the .NET SDK TFM late-import silently caps MaxVersionTested at 10.0.19041.0. See CONTEXT.md standing rule #4.
- Code-signing flow: Thales hardware token + Sectigo OV cert. Keith handles signing for both MSI and MSIX. **The OV cert subject is `CN="Toast2IT, LLC", O="Toast2IT, LLC", S=Florida, C=US`** (CN, O, S, C - four RDNs, both CN and O contain a comma so both need quotes). `Package.Identity.Publisher` MUST match this string exactly across all four RDNs or signing fails (0x80091005 in DigiCert Utility / 0x800B0109 in signtool) and any installed signed MSIX rejects with 0x800B0109. The authoritative reference is the cert utility's Details -> Subject field (NOT `Get-AuthenticodeSignature` on a previously-signed MSI - that display can truncate the subject).

## Open Items (carried into M0 or later)

- M0 D2 (Win10 1809 install validation): no Win10 1809 lab machine on hand; deferred to M0 D4 GPO matrix.
- M0 D4: GPO / domain / Intune / multi-user matrix. Apply `FIX-MSIX-002` first. Roll uninstall idempotency + 0.3.x → 0.3.y major-upgrade race validation into this matrix.
- M0 D5: **CLOSED 2026-05-09** — listing 9PFD6004DVTN live in the Microsoft Store. Partner Agreement complete 2026-05-10. **Deferred polish (pre-M9 marketing push):** (1) Diana curated tile assets; (2) Store listing description rewrite.
- M0 D6: Document deployment findings + fallback mechanisms. Diana tile assets delivery.
- M2 follow-up: detect `----AppNotificationActivated:` arg in `AgentOptions.Parse` and route to one-shot activation handler instead of falling through to a default Plain template re-send (INFO-MSIX-004-D).
- M2 follow-up: HMAC payload verification, SignalR reconnect, missed notification catch-up.
- **CLOSED 2026-05-10:** `--diag` flag gate + DiagLog 512KB rotation (INFO-MSIX-004-A/B/C). `ToastNotification.exe --diag` dumps log path + last 200 lines.
- **CLOSED 2026-05-10:** INFO-M8C-001 hub isolation test predicate-poll (was Task.Delay 500ms).
- **CLOSED 2026-05-10:** INFO-M7C-003 docs nav paths extracted to `src/routes/docsRoutes.ts` — single source of truth.
- **CLOSED 2026-05-10:** INFO-M9C-002 DeployCommand enrollment-key fetch now module-cached.
- M4 design: curated per-template hero / logo imagery + curated MSIX tile imagery. Open Diana question - `AppNotificationButtonStyle.Critical` vs `Success` for security-framed actions.
- Future templates: inline image, text input, selection input controls not yet exercised.
- End-to-end testing not yet completed — deferred to M8 beta.
