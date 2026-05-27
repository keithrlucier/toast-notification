# ToastRevival - Fix List

---

## FIX-UPLOAD-413-001 — nginx 413 Request Entity Too Large on uploads (2026-05-27, RESOLVED)

**Surface:** `/etc/nginx/sites-enabled/toast` on TOASTWEB1.
**Symptom:** First lock-screen image upload via the M12 dashboard returned an `<html>413 Request Entity Too Large</html>` page from nginx (not a JSON error from the API). The image never reached `/api/tenant/lockscreen-image`.

**Root cause:** the nginx vhost had no `client_max_body_size` directive at all, so nginx fell back to the **1 MB default**. The API allows 5 MB on the lock-screen and asset uploads and 2 MB on the logo upload (per the controller `[RequestSizeLimit]` attributes), but nginx rejected anything over 1 MB before it ever reached Kestrel. The gap had been latent — every image uploaded through the dashboard since launch happened to be under 1 MB.

**Fix:** added `client_max_body_size 6m;` at the toast vhost server level (5 MB content + ~1 MB form/multipart overhead), placed right after `server_name`. nginx test + reload clean; lock-screen and asset uploads now go through.

**Standing rule (added to deploy checklist):** when a controller has a `[RequestSizeLimit]` attribute, the nginx vhost must carry a matching-or-larger `client_max_body_size` directive. Verify with `nginx -T | grep client_max_body_size` after any nginx config edit on this box.

---

## FIX-DOWNLOADS-001 — Public MSI download 404 (2026-05-27, RESOLVED)

**Surface:** `/etc/nginx/sites-enabled/toast` on TOASTWEB1.
**Symptom:** `https://toastnotification.com/downloads/ToastNotification.msi` (the URL the dashboard's `DeployCommand.tsx` and `InstallAgent.tsx` hand to admins for the msiexec install) returned **HTTP 404 bytes=0** — meaning every RMM/manual installer pull since FIX-ASSETS-001 shipped has been broken.

**Root cause:** the nginx `location /downloads/` block proxied to `localhost:5216` (the API), but the API has **no controller route** for `/downloads/*`. It used to work because the pre-FIX-ASSETS-001 `Program.cs` had a default `app.UseStaticFiles()` that served `wwwroot/downloads/ToastNotification.msi` at the URL. FIX-ASSETS-001 replaced the default middleware with an *explicit* `PhysicalFileProvider` mounted at `/assets` only. That fix orphaned `/downloads/` because the static handler that was implicitly serving it disappeared. Nobody noticed because there were no new agent builds between then and 2026-05-27.

**Fix:** rewrote the nginx `location /downloads/` block to serve the file statically by `alias /opt/toast/downloads/` (where the signed MSI already lives), with `try_files $uri =404`, `Content-Disposition: attachment`, and a short `Cache-Control: public, max-age=300`. nginx reloaded clean; signed MSI download verified end-to-end (HTTP 200, byte-perfect sha256 match against the local artefact).

**Standing rule (added to Code Sweep Step 4):** when removing a default static middleware in favor of an explicit one, audit every URL path that the default was implicitly serving — at minimum grep the dashboard for hardcoded download URLs and curl them against prod. The dashboard had `${serverUrl}/downloads/ToastNotification.msi` in `DeployCommand.tsx` and `InstallAgent.tsx`; a single targeted curl in the FIX-ASSETS-001 verification would have caught this in the same session.

---

## Assets page — preview + rename (2026-05-22)

### FIX-ASSETS-001 — Asset library previews never load (gray boxes) — **RESOLVED 2026-05-22 (verified on prod)**

**Filed:** 2026-05-22 (troubleshooting session — Carl/Anthony/Diana/Abish).
**Surface:** `src/ToastRevival.Api/Program.cs`, `src/ToastRevival.Api/Controllers/AssetsController.cs`, `src/ToastRevival.Dashboard/src/pages/Assets.tsx`; prod state on TOASTWEB1.
**Symptom:** Asset cards render gray placeholder boxes instead of the uploaded image. `<img onError>` hides the element, so any failed image load looks identical to "unstyled" — but it was a dead URL, not CSS.

**CORRECTION:** the first writeup of this fix blamed a missing `location /assets/` in the dashboard `nginx.conf`. That was wrong for **production** — prod is bare-metal Lightsail (not the Docker compose path), and the live server's nginx already proxied `/assets/ → localhost:5216`. SSH inspection of TOASTWEB1 found **three** compounding real causes:

1. **Static serving was dead in the API (code bug).** `Program.cs` reassigned `app.Environment.WebRootPath` *after* `builder.Build()`. That does NOT rewire `WebRootFileProvider`, so `app.UseStaticFiles()` served from a stale/empty provider and **every** static request to Kestrel 404'd (verified: even `wwwroot/favicon-32.png` 404'd via `:5216`). nginx masks this for the SPA — it serves those from `/opt/toast/dashboard` directly — so `/assets/*` (proxied to Kestrel) was the visible casualty.
2. **Uploads lived inside the deploy directory** (`/opt/toast/api/wwwroot/assets`). The redeploy procedure replaces `/opt/toast/api` wholesale, so **every redeploy orphaned all previously-uploaded assets.** Of 5 DB rows, 3 files survived only in `api.bak.pre-m10-2026-05-12`; 2 (colosolutions logo + icon, uploaded 2026-05-12) were lost from every backup.
3. **Baked `http://` URLs.** No `UseForwardedHeaders`, so behind the TLS-terminating nginx `Request.Scheme`=http and stored `AssetLibrary.Url` carried `http://` → mixed-content blocked on the https dashboard (also wrong for the Windows agent).

**Fix (shipped + verified on prod):**
- `Program.cs`: serve `/assets` via an explicit `PhysicalFileProvider` (not the broken web-root provider) rooted at a configurable `Assets:RootPath`; added `UseForwardedHeaders` (XFwd-For/Proto; loopback proxy trusted by default) so `Request.Scheme`=https.
- `AssetsController.cs`: write/delete uploads under the same `Assets:RootPath` root.
- Prod `/opt/toast/.env`: `Assets__RootPath=/opt/toast/shared/assets` — **persistent, outside the deploy dir** so redeploys no longer orphan uploads. Surviving 3 files migrated there; DB `Url` backfilled `http://`→`https://`.
- `Assets.tsx`: `previewSrc(url)` loads the `<img>` from the URL's same-origin pathname (defense-in-depth against any future baked-scheme drift). "Use as Hero/Logo" still pass absolute `asset.url`.
- Dashboard `nginx.conf` + `vite.config.ts`: added `/assets/` proxy — **only relevant to the Docker/self-host ("Roll Your Own") path**, NOT prod. Kept because self-hosters hit the same routing gap.
**Verified:** the 3 recoverable assets return `200 image/png` over https; SPA `/login` 200; `/api/health` 200; rename route `401` (auth-gated, exists).
**Outstanding:** 2 colosolutions files (`f1be6341` icon, `a79a3a91` logo) are unrecoverable — require re-upload by the tenant. No automated tests on the Assets surface (follow-up).

### FIX-ASSETS-002 — No way to rename an asset — **RESOLVED 2026-05-22**

**Filed:** 2026-05-22 (same session).
**Surface:** `src/ToastRevival.Api/Controllers/AssetsController.cs`, `src/ToastRevival.Dashboard/src/api/client.ts`, `src/api/assets.ts`, `src/pages/Assets.tsx`.
**Issue:** Asset name was set only at upload; no edit endpoint or UI existed.
**Fix:** Added `PATCH /api/assets/{id}` (tenant-scoped lookup by `Id && TenantId`, trim + non-empty + ≤200 char validation, `asset.rename` audit log with old/new name, returns `AssetResponse`). Added `RenameAssetRequest` DTO, `api.patch()` verb, `assetsApi.rename()`, and an inline rename UI in `AssetCard` (Rename button → input with Save/Cancel, Enter/Escape, disabled-during-save, empty/no-op guards). No migration needed — `Name` column already exists.
**QA:** Abish — SHIP WITH NOTES. Note: no automated asset tests exist; recommend a Rename integration test (tenant isolation + validation) in a future test pass.
**Blocking:** No.

---

## M11 Open Items (opened 2026-05-12)

### INFO-M11-D7-001 — Home.tsx terminal-comparison block uses Unicode ✓ glyphs — **OPEN (deferred)**

**Filed:** 2026-05-13 (M11 D7 Code Sweep #41).
**Surface:** `src/ToastRevival.Dashboard/src/pages/marketing/Home.tsx:120-123` — `<div className="m-terminal-body">` simulating console output.
**Issue:** Decorative `[✓]` Unicode glyphs inside the styled terminal-comparison panel. Diana's standing rule bans emojis in UI; these are arguably chrome simulating console output rather than UI affordance. Pre-existing — not introduced by D7.
**Fix:** Diana decision — ASCII `[x]` / `[OK]` replacement, or keep as-is if simulated-terminal aesthetic justifies the glyphs.
**Priority:** Not blocking. Cosmetic. Defer for Diana review when convenient.
**Owner:** Diana.

---

### INFO-M11-D7-002 — AggregateOffer lowPrice = $0.00 on /pricing JSON-LD — **OPEN (informational)**

**Filed:** 2026-05-13 (M11 D7 Code Sweep #41).
**Surface:** `src/ToastRevival.Dashboard/src/lib/seo.ts` — `pricingProductLd()`; `src/ToastRevival.Dashboard/scripts/prerender-seo.mjs` — `productLd()`.
**Issue:** AggregateOffer now declares three Offers — Free Trial ($0), Managed SaaS ($22), Roll Your Own ($0). Schema.org permits `lowPrice: "0.00"`, but some SERP price-range renderers prefer non-zero lowPrice and may suppress the price chip. Reality is what it is.
**Fix:** None required. If SERP price chip suppression becomes a marketing problem, narrow the AggregateOffer to the paid tier only.
**Priority:** Informational.
**Owner:** Diana / Carl (marketing observability review).

---

### INFO-M11-SW-001 — TOCTOU on concurrent trial device registration — **RESOLVED 2026-05-12**

**Resolution:** New `ILicenseService.TryRegisterDeviceAtomicAsync(tenant, device, ct)` owns a transaction that (a) acquires `pg_advisory_xact_lock` keyed on `tenantId.GetHashCode()`, (b) reloads the tenant inside the lock so the cap check sees authoritative `ConsumedCount`, then (c) inserts the device row and bumps `ConsumedCount` in a single commit. Concurrent registrations for the same tenant serialize at the lock; different tenants run in parallel. Stripe sync stays outside the transaction (network I/O, safely retriable after the seat is committed). `DevicesController.Register` calls the new method instead of the previous `CanRegisterDeviceAsync` → `Add` → `IncrementConsumedAsync` sequence. Regression test `TrialDeviceCapConcurrencyTests.Register_ConcurrentTrialBurst_NeverExceedsTrialCap` fires two concurrent registers at cap-minus-one and asserts exactly one wins. See `git log --grep "INFO-M11-SW-001"` for the resolving commit.

**Filed:** 2026-05-12 (M11 D6 Code Sweep).
**Surface:** `src/ToastRevival.Api/Services/LicenseService.cs` — `CanRegisterDeviceAsync` trial branch.
**Issue:** Concurrent `POST /register` calls from the same trial tenant can both pass the 2-device check before either write commits, exceeding the cap. Read-then-write is not atomic.
**Fix:** `pg_try_advisory_xact_lock` pattern (or `SELECT ... FOR UPDATE` on tenant row) to make the check-and-increment atomic.
**Priority:** Not blocking for self-host launch. Fix before SaaS scale / concurrent onboarding load.
**Owner:** Anthony.

---

### INFO-M11-001 — LicenseService self-hosted bypass — **RESOLVED 2026-05-12**
**Resolution:** `TOAST_REQUIRE_BILLING` env var added. When `false`/unset, `CanRegisterDeviceAsync` returns `true` immediately. Commit `76ae2ef`.

**Filed:** 2026-05-12 (M11 dev meeting kickoff).
**Surface:** `src/ToastRevival.Api/Services/LicenseService.cs` — `CanRegisterDeviceAsync`
**Issue:** Self-hosted deployments run with empty `Stripe__SecretKey`. Current `CanRegisterDeviceAsync` logic may block device registration if it checks for `BillingStatus.Active` when no Stripe subscription exists. Self-hosters must not hit a device cap or billing wall.
**Required verification:** Read `LicenseService.CanRegisterDeviceAsync` — confirm what it returns when `Stripe__SecretKey` is empty/null and no `StripeSubscriptionId` exists on the tenant row. If it returns false or throws, add `TOAST_REQUIRE_BILLING` env flag to short-circuit the check.
**Owner:** Anthony (code audit) + Carl (approach sign-off before any change).
**Blocking:** M11.D2.

### INFO-M11-002 — DISABLEAUTOUPDATE MSI property — **RESOLVED 2026-05-12**
**Resolution:** `DISABLEAUTOUPDATE=1` WiX property wired in Setup.wxs. Writes `HKLM\SOFTWARE\Toast2IT\Toast Notification\DisableAutoUpdate=1`. `UpdateService.cs` already consumed that key. Commit `76ae2ef`.

**Filed:** 2026-05-12 (M11 dev meeting kickoff).
**Surface:** `installer/ToastRevival.Agent.Setup.wxs` + `src/ToastRevival.Agent/Services/UpdateService.cs`
**Issue:** Self-hosters point the agent at their own backend. The Velopack auto-update feed (`releases.toastnotification.com`) must not be polled — it would overwrite the self-hoster's configuration with our managed build. Need a `DISABLEAUTOUPDATE` WiX property that writes a registry key; `UpdateService.cs` reads the key and returns early.
**Owner:** Anthony.
**Blocking:** M11.D3.

### INFO-M11-003 — 2-device trial cap not yet enforced — **RESOLVED 2026-05-12**
**Resolution:** `BillingPlanRules.TrialDeviceLimit = 2`. `CanRegisterDeviceAsync` gates Trialing tenants at 2 devices. Commit `76ae2ef`.

**Filed:** 2026-05-12 (M11 dev meeting kickoff).
**Surface:** `src/ToastRevival.Api/Services/LicenseService.cs` — free tier threshold (currently 25 devices). `src/ToastRevival.Dashboard/src/pages/Onboarding.tsx` — copy references old tier model.
**Issue:** New model is 2-device / 14-day trial for approved tenants. Current free tier allows up to 25 devices. Trial tenants need their own tier logic separate from the ongoing free tier for self-hosted operators.
**Note:** This is a pricing model change — needs Carl's architecture sign-off before touching `LicenseService`. Don't change before understanding whether trial tenants and self-hosted operators share the same code path.
**Owner:** Anthony (code) + Carl (arch) + Diana (Onboarding copy).
**Blocking:** M11.D6.

### INFO-M11-004 — Git history sanitization required before public repo — **RESOLVED 2026-05-12**
**Resolution:** Full scan clean. Public repo live at https://github.com/keithrlucier/toast-notification — 218 files, fresh history, no private commits included.

**Filed:** 2026-05-12 (M11 dev meeting kickoff).
**Surface:** Full git history.
**Issue:** Before the repo goes public, the full commit history must be scanned for: server IPs (54.82.103.160, 172.26.3.164, 52.21.249.120), `.pem` file contents (SSH keys), Stripe keys, database passwords, or any other credentials. `Docs/Assets/*.pem` is gitignored but confirm no historical commit ever included the content. Use `git log -p --all` + grep, or `trufflehog`.
**Owner:** Anthony (audit) + Carl (final sign-off).
**Blocking:** M11.D4 (public repo creation cannot proceed until this is clean).

---

## Resolved 2026-05-10 (session 3)

### INFO-MSIX-004 (A/B/C) — **RESOLVED 2026-05-10**

**Filed:** M1/M2 carry-forward (DiagLog unbounded growth, no support dump path).
**Surface:** `src/ToastRevival.Agent/Program.cs` — `DiagLog` class + `AgentEntryPoint.RunAsync`.
**Resolution:**
- **Rotation**: `DiagLog` now has a `MaxFileBytes = 512 * 1024` constant. `Write()` calls `RotateIfNeeded()` inside the lock before each append. `RotateIfNeeded` checks `FileInfo.Length >= MaxFileBytes` and renames `agent.log` → `agent.log.1` (overwriting any existing `.1`), then returns so the next `AppendAllText` creates a fresh `agent.log`. Keeps two generations. Error-swallowed (best-effort, same policy as the rest of `DiagLog`).
- **`--diag` flag gate**: New `DiagMode` class handles `--diag`. Dispatched in `AgentEntryPoint.RunAsync` after `--setup-bootstrap` (SYSTEM mode) but before the elevation check — support staff running as admin can still get the dump. Prints log path, file size in KB, and the last 200 lines of the log to stdout. Ships next signed agent build alongside the agent's next MSI rebuild.

### INFO-M9C-002 — **RESOLVED 2026-05-10**

**Filed:** M9.C Code Sweep carry-forward ("deploy-command fetch caching nice-to-have").
**Surface:** `src/ToastRevival.Dashboard/src/components/DeployCommand.tsx`
**Resolution:** Module-level `_enrollmentKeyCache: Promise<string | null> | null` variable caches the first API call result. `getEnrollmentKey()` helper returns the cached promise on subsequent calls — `/api/tenant/settings` fires at most once per page load regardless of how many times `DeployCommand` mounts. Non-fatal error path preserved (unresolved promise catches to `null`). The `useEffect` now calls `getEnrollmentKey()` instead of `api.get()` directly; the `cancelled` flag guards remain.

---

## INFO-RATELIMIT-001 (pre-Cloudflare required)

**Filed:** 2026-05-11 (session 4, Code Sweep)
**Surface:** `src/ToastRevival.Api/Program.cs` — `login-per-ip` and `login-sms-per-ip` rate limit policies
**Issue:** Both policies partition on `ctx.Connection.RemoteIpAddress`. No `UseForwardedHeaders` configured. Behind a reverse proxy (Cloudflare, nginx upstream, load balancer), all requests share the same IP bucket — one user triggering the limit blocks all users.
**Fix:** Before routing production traffic through Cloudflare or any reverse proxy:
```csharp
builder.Services.Configure<ForwardedHeadersOptions>(opts => {
    opts.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    // Add Cloudflare IP ranges to KnownProxies
});
app.UseForwardedHeaders(); // before app.UseRateLimiter()
```
**Blocking:** No — current deployment is direct Lightsail. Becomes blocking the moment a proxy sits in front.

---

## Open Issues

### INFO-SEC-006 — **RESOLVED 2026-05-09** (nginx static SPA responses now carry defensive headers + HSTS)

**Filed:** 2026-05-09 (production verification of SEC-001).
**Resolved:** 2026-05-09 (immediate follow-up).
**Surface:** `/etc/nginx/sites-available/toast` on TOASTWEB1 → snapshotted to `infrastructure/nginx/toast.conf`.
**Resolution:** Five `add_header ... always` directives added at server scope: `X-Content-Type-Options nosniff`, `X-Frame-Options DENY`, `Referrer-Policy strict-origin-when-cross-origin`, `Permissions-Policy camera=()/microphone=()/geolocation=()`, and `Strict-Transport-Security max-age=31536000; includeSubDomains`. Server-scope inherits to every location (none of `/api/`, `/hubs/`, `/assets/`, `/`-fallback override). `always` applies to non-2xx responses too (4xx/5xx error pages). `nginx -t` clean, `systemctl reload nginx` applied without service interruption.
**Verification:** `curl -D -` against `https://toastnotification.com/login` (was missing all five) and `https://toastnotification.com/` (marketing) both now carry the full defensive set + HSTS. `/api/templates` carries duplicates from nginx + ASP.NET — defense-in-depth: a future nginx config drift (e.g., a Certbot renewal that rewrites the server block) still leaves API protected by its own middleware, and a middleware-pipeline regression leaves the API protected by nginx.
**Repo snapshot:** `infrastructure/nginx/toast.conf` + `infrastructure/nginx/README.md` (sync workflow + Certbot-managed-line caveats). The repo snapshot is documentation-only; authoritative copy lives on TOASTWEB1.

### SEC-005 — **RESOLVED 2026-05-09** (TOTP code replay within ±1 step)

**Filed:** 2026-05-09 (post-M8.C security review). Was carrying as INFO-M3-001 since M3.
**Surface:** `src/ToastRevival.Api/Services/MfaService.cs::Verify`, `src/ToastRevival.Api/Models/AppUser.cs`, `src/ToastRevival.Api/Controllers/AuthController.cs::MfaVerify`.
**Issue:** `MfaService.Verify` called `Totp.VerifyTotp(code, out _, new VerificationWindow(1, 1))` and discarded the matched time-step. With ±1 step skew, a TOTP code stays valid for up to ~90 seconds. An attacker who intercepted a single submission could replay it any number of times within that window before the legitimate user noticed. Standard TOTP weakness; required the standard mitigation (track last-accepted step per user, reject any code whose matched step is `<=` recorded value).
**Fix applied:**
  - New `AppUser.LastTotpStep` (long?, nullable) column. Migration `20260509190000_M3MfaTotpReplay` adds it with a null default so existing users transparently start tracking on their next successful verify.
  - `MfaService.Verify` now takes `(AppUser user, string code)` instead of `(string? secret, string code)`. Captures the matched step via `out var matchedStep`, rejects when `user.LastTotpStep.HasValue && matchedStep <= user.LastTotpStep.Value`, and writes `user.LastTotpStep = matchedStep` on success.
  - `AuthController.MfaVerify` now calls `_mfa.Verify(user, req.Code)` and follows a successful verify with `await _db.SaveChangesAsync()` so the floor persists across requests. Without persistence, replay rejection only held for the lifetime of the in-memory entity.
**Verification:** `tests/ToastRevival.Api.Tests/MfaServiceTests.cs` — 6 [Fact] cases covering fresh-code accept, replay reject within step, older-step reject (skew/rewind), missing-secret reject, invalid-code reject, empty-code reject. The replay-reject test mints one TOTP code via OtpNet, asserts the first verify succeeds and records a step, then asserts the second verify of the same code returns false without bumping the floor.
**Blocking:** No — defensive only.

### SEC-004 — **RESOLVED 2026-05-09** (CSV formula injection in audit / delivery exports)

**Filed:** 2026-05-09 (post-M8.C security review). Was carrying as INFO-M5D-003 "acceptable" since M5.D.
**Surface:** `src/ToastRevival.Api/Utilities/CsvHelper.cs::Cell`.
**Issue:** The CSV cell encoder applied RFC 4180 quoting (commas, quotes, newlines) but did not neutralize spreadsheet formula triggers. A cell value starting with `=`, `+`, `-`, `@`, `\t`, or `\r` is interpreted as a formula by Excel / LibreOffice Calc / Google Sheets when the export is opened. Audit log `Action`, `ResourceType`, and `ResourceId` strings are server-generated today (low blast radius), but any future controlled-string field flowing into `BuildAuditCsv` or `BuildDeliveryCsv` would inherit the vector. Filed "acceptable" at M5.D — promoted to fix because B2B compliance reviewers flag CSV injection on every audit-export surface.
**Fix applied:** `CsvHelper.Cell` now prefixes any value starting with a formula-trigger character with a single apostrophe (`'`) before applying RFC 4180 quoting. The apostrophe is the documented "treat as literal text" sentinel across Excel, LibreOffice, and Google Sheets — it strips on render and the original value displays as plain text. Stacks under quoting (apostrophe goes inside the outer double quotes when both defenses fire).
**Verification:** `tests/ToastRevival.Api.Tests/CsvHelperTests.cs` — 9 [Theory]/[Fact] cases covering each formula trigger, safe values left unchanged, RFC 4180 quoting paths, and the stacking case where both defenses apply.
**Blocking:** No — defensive only.

### INFO-M2A-002 — **RESOLVED 2026-05-08 (M3, commit 362f9d3)** (DeviceConfig at rest plaintext)

**Filed:** 2026-05-09 (M2.A Code Sweep). **Resolution noted:** 2026-05-09 (post-M8.C audit caught FIX-LIST entry was stale).
**Surface:** `src/ToastRevival.Agent/DeviceConfig.cs::ConfigStore`.
**Resolution:** `Save` and `TryLoad` wrap the JSON payload via `ProtectedData.Protect`/`ProtectedData.Unprotect` at `DataProtectionScope.CurrentUser` (lines 95-101). Ciphertext written via temp-then-Move so a half-written file can't survive a crash mid-write. `bootstrap.json` next to the exe stays plaintext intentionally — it's the install-time non-secret values (TenantId, ServerUrl, optional pre-shared EnrollmentKey) that the SYSTEM-context MSI custom action writes and the user-context agent reads on first run; CurrentUser-scope DPAPI wouldn't work across that boundary. After registration, all session credentials (device JWT, tenant signing key) live in the DPAPI-wrapped `config.json`.

### INFO-M2D-005 — **RESOLVED 2026-05-08 (M3)** (TrySelfRedirect launches binary from user-writable path)

**Filed:** 2026-05-08 (M2.D Code Sweep). **Resolution noted:** 2026-05-09 (post-M8.C audit caught FIX-LIST entry was stale).
**Surface:** `src/ToastRevival.Agent/UpdateService.cs::TrySelfRedirect`.
**Resolution:** Authenticode signature verification before launch (lines 112-119, 215-288). Two-step gate: (1) `X509Certificate2.CreateFromSignedFile` reads the embedded cert and confirms the subject contains `Toast2IT, LLC` (fast, no network); (2) full chain + signature validation via `WinVerifyTrust` P/Invoke with `WTD_UI_NONE`/`WTD_REVOKE_NONE` flags. Returns false on any failure so the redirect aborts and the bootstrap binary continues running from `%ProgramFiles%`. A local-user attacker can no longer plant a higher-versioned malicious binary at the Velopack managed path and have the bootstrap binary execute it on next launch.

### SEC-001 — **RESOLVED 2026-05-09** (API response missing defensive security headers)

**Filed:** 2026-05-09 (post-M8.C security review).
**Surface:** `src/ToastRevival.Api/Program.cs` middleware pipeline.
**Issue:** API responses carried only the default Kestrel/ASP.NET headers — no `X-Content-Type-Options`, no `X-Frame-Options`, no `Referrer-Policy`, no `Permissions-Policy`, no HSTS. A misbehaving browser MIME-sniffing a JSON response, a clickjacking iframe of a Swagger UI page, a referrer leak from a toast hero-image fetch, or a downgrade attack on the TLS pipe all sat exposed.
**Fix applied:** Inline middleware at the top of the response pipeline (before auth, before static files, before swagger) sets `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy: camera=(), microphone=(), geolocation=()`. `UseHsts` and `UseHttpsRedirection` registered in non-Development environments only (TestServer has no HTTPS pipe; Development typically runs over plain http://localhost). HSTS skips localhost by default, so it's only useful behind the production TLS terminator on TOASTWEB1.
**Verification:** `tests/ToastRevival.Api.Tests/SecurityTests.cs::SecurityDefaults_ResponseIncludesDefensiveHeaders` exercises an unauthenticated `GET /api/templates` (returns 401) and asserts every defensive header is present. Placement before authentication means even challenge responses and 4xx error pages carry the headers.
**Blocking:** No — defensive only.

### SEC-002 — **RESOLVED 2026-05-09** (No SAST coverage)

**Filed:** 2026-05-09 (post-M8.C security review).
**Surface:** `.github/workflows/`.
**Issue:** No static analysis on push/PR. Compiler default warnings only — no Roslyn analyzer rules, no CodeQL, no SonarQube. C# AND TypeScript surfaces both unscanned for the standard SAST class (SQL injection, command injection, taint flow, hardcoded secrets, deserialization gadgets, etc.).
**Fix applied:** New `.github/workflows/codeql.yml` runs CodeQL on push to main, PR to main, and Mondays 06:13 UTC. Two language matrix entries: `csharp` (manual build of `ToastRevival.Api.csproj`) and `javascript-typescript` (no build needed). `security-extended` query suite. Findings surface in the GitHub Security tab and gate PR merge once branch protection enforces required checks.
**Blocking:** No — additive defensive layer.

### SEC-003 — **RESOLVED 2026-05-09** (No dependency vulnerability scanning)

**Filed:** 2026-05-09 (post-M8.C security review).
**Surface:** `.github/dependabot.yml`.
**Issue:** No automated detection of known CVEs in NuGet, npm, or GitHub Actions dependencies. Vulnerability advisories propagated to the project only on manual review.
**Fix applied:** New `.github/dependabot.yml` covering three ecosystems (`nuget` rooted at `/`, `npm` at `/src/ToastRevival.Dashboard`, `github-actions` at `/`). Weekly cadence (Monday) for version updates; security advisories surface immediately regardless of schedule. PR limit per ecosystem 5 to keep noise bounded. Update groups for ASP.NET Core, EF Core, test stack, React, Vite, and TypeScript so semver bumps land as one PR per group rather than dozens.
**Blocking:** No — defensive only.

### INFO-M1-006 — **RESOLVED 2026-05-09** (JWT key min-length runtime check)

**Filed:** 2026-05-08 (M1 Code Sweep). **Resolved:** 2026-05-09 (post-M8.C security pass).
**Surface:** `src/ToastRevival.Api/Program.cs` JWT key load.
**Resolution:** A misconfigured production deployment that forgets to override `Jwt__Key` would silently fall through to whatever short placeholder sat in `appsettings.json`. HMAC-SHA256 wants 32+ bytes of key material; anything shorter weakens the signature beyond useful guarantee. New runtime check throws `InvalidOperationException` when `!builder.Environment.IsDevelopment() && jwtKey.Length < 32`. Test config key is 70 characters; ApiTestFactory's Production-environment posture passes the check.

### FIX-M8C-001 — **RESOLVED 2026-05-09 (M8.C)** (Cross-tenant audit log leak)

**Filed:** 2026-05-09 during M8.C orientation (Carl spotted the missing tenantId filter while mapping pen-test surfaces).
**Surface:** `src/ToastRevival.Api/Controllers/AuditController.cs` (`List` endpoint at line 33–63, `Export` endpoint at line 70–101).
**Symptom:** Both per-tenant audit endpoints queried `_db.AuditLogs.Where(l => l.Timestamp >= since)` with no tenantId predicate. The `AuditLog` entity has no global query filter — by design, the PlatformAdmin `SystemController` needs cross-tenant visibility for the platform-wide audit view (see `AppDbContext.cs:123-129` comment). Per-tenant controllers were therefore returning every tenant's audit rows to any authenticated tenant admin. Reproducible: seed two tenants, write one audit row per tenant via `_audit.LogAsync`, hit `/api/audit` as Tenant A's admin → Tenant B's row appears in the response.
**Fix applied:** Both endpoints now extract `tenantId = Guid.Parse(User.FindFirstValue("tenantId")!)` from the JWT claim and add `.Where(l => l.TenantId == tenantId && l.Timestamp >= since)` before the timestamp range filter. Same extraction pattern already in use elsewhere in the controller (Export's tenantName lookup). Composite `(TenantId, Timestamp)` index already exists at `AppDbContext.cs:127`, so the added predicate sits cleanly on an indexed column.
**Verification:** `tests/ToastRevival.Api.Tests/SecurityTests.cs::TenantIsolation_AuditList_DoesNotLeakOtherTenantsRows` seeds A + B audit rows and asserts Tenant A's response contains A's action string and not B's. Build clean (0 warnings, 0 errors).
**New standing rule:** Code Sweep Step 5 now includes "Does every entity without a global query filter have an explicit tenantId predicate at every per-tenant controller read site?" `AuditLog` is the canonical "read by both PlatformAdmin and tenant admins" table; future tables of the same shape need this check applied at controller review.

### INFO-M8C-001 — **RESOLVED 2026-05-10 (session 3)**

**Filed:** 2026-05-09 (M8.C Code Sweep).
**Surface:** `tests/ToastRevival.Api.Tests/SecurityTests.cs::TenantIsolation_HubDeviceConnectedEvent_DoesNotLeakAcrossTenantGroups`
**Resolution:** `Task.Delay(TimeSpan.FromMilliseconds(500))` replaced with a 20ms predicate-poll loop (300ms total timeout). Loop exits early if `aReceived.Count > 0` — fail-fast when tenant isolation is broken, which is the test's primary signal. Otherwise drains the full 300ms window before `Assert.DoesNotContain`. Reduces wall-time on the happy path (isolation intact → exits at deadline, not fixed 500ms) and on the failure path (leaked event detected within ~20ms, not after 500ms). Lock scope preserved; same `aReceived` list and lock guard as before.

### INFO-M1-004 — **RESOLVED 2026-05-09 (M8.A)** (Zero automated tests in backend)

**Filed:** 2026-05-08 during M1 close (carry-forward across M2–M7).
**Surface:** entire `src/ToastRevival.Api` — no test project existed.
**Resolution:** New `tests/ToastRevival.Api.Tests` project shipped at M8.A with xUnit + Microsoft.AspNetCore.Mvc.Testing 8.0.15 + Testcontainers PostgreSQL fixture + Microsoft.AspNetCore.SignalR.Client 8.0.15. First end-to-end scenario covers the M2.A/M2.B critical path: tenant register → device register → admin send → SignalR fanout → HMAC verify → ReportDelivery → ReportInteraction → DB invariants. CI runner at `.github/workflows/api-tests.yml` runs the suite against a Postgres 16 service container on every push/PR touching API, sln, or tests. See `EVIDENCE/2026-05-09-m8a-test-foundation.md`.

### INFO-M8A-001 — **RESOLVED 2026-05-09 (M8.B)** (PostgresFixture friendly Docker pre-flight)

**Resolution:** `tests/ToastRevival.Api.Tests/PostgresFixture.cs` `InitializeAsync` now probes the platform-specific Docker endpoint (`/var/run/docker.sock` on Linux/macOS, `\\.\pipe\docker_engine` on Windows; honors `DOCKER_HOST` override) before calling `_container.StartAsync()`. When neither Docker nor `TOAST_TEST_CONNECTION_STRING` is reachable, the fixture throws an `InvalidOperationException` with a single-paragraph instruction pointing the developer at the env-var override and the CI service-container pattern. The Testcontainers stack trace surfaces only after this gate passes.

### INFO-M8A-002 — **RESOLVED 2026-05-09 (M8.B)** (ApiTestFactory class-scoped fixture share)

**Resolution:** New `LoadFixture` (collection-scoped via `LoadCollection`) owns one `ApiTestFactory` instance plus a `Respawner` snapshot of the empty post-migration schema. Both `EndToEndNotificationTests` and the new `LoadTests` consume the shared fixture. Per-test isolation is preserved via `_load.ResetAsync()` calls at the top of every test method (truncates non-Identity tables back to the snapshot in milliseconds). The M8.A pattern of building a fresh factory per test is preserved for connection strings that don't support the Respawner DDL truncation path (Respawner stays null, `ResetAsync` becomes a no-op, tests still pass on fresh-GUID isolation).

### INFO-M8A-003 — **RESOLVED 2026-05-09 (M8.C)**

**Resolution:** New `tests/ToastRevival.Api.Tests/WebSocketTransportTests.cs` shipped at M8.C with `WebSocket_HubAuthenticatesViaQueryStringAccessToken_AndReceivesNotification`. Uses `factory.Server.CreateWebSocketClient()` plus `Transports = HttpTransportType.WebSockets` plus `SkipNegotiation = true` to drive the SignalR client straight at `/hubs/notifications` over a WebSocket upgrade with no Authorization header. The only authentication channel is the `access_token` query string — exactly the path `JwtBearerEvents.OnMessageReceived` (`Program.cs:65-75`) reads. Validates a notification round-trips the WebSocket transport with HMAC verify against the seeded tenant signing key.

### INFO-M8A-004 (open, no action)

`ToastRevival.sln` BOM was dropped during the M8.A rewrite. `dotnet build` 0/0 confirms VS 2022 17.x and the dotnet CLI parse the BOM-less UTF-8 file without warnings. Legacy Visual Studio 2019 or older may emit a warning if the user opens the solution there. Acceptable trade-off for a cross-tool-edited file.

### INFO-M8A-005 (open, no action)

`tests/ToastRevival.Api.Tests/PayloadVerifier.cs` reproduces the production HMAC verification logic (HMAC-SHA256 + `CryptographicOperations.FixedTimeEquals`) because the Windows-only `ToastRevival.Agent` project cannot be referenced from a netstandard test assembly. Drift risk is minimal — both ends use the same vendor primitives, and the tenant signing key encoding is shared via `DeviceTokenResponse`. If `Tenant.SigningKey` ever changes encoding, both surfaces update at once. Flagged for record only.

### INFO-M8B-001 (open, M8.C/M9 candidate)

`tests/ToastRevival.Api.Tests/LoadTests.cs::Fanout_To_DefaultDeviceCount_DeliversWithinLatencyBudget` asserts `result.P95Ms < 5000` as a behavioral smoke. The threshold is a generous initial budget chosen without CI-runner data — it should be tightened (or replaced with a regression-tracking percentile) once the test has accumulated 10+ green runs on the GitHub-hosted Ubuntu runner. M8.C or M9 candidate: capture a rolling p95 baseline as a workflow-published artifact, then assert that new runs land within ±20% of the rolling median.

### INFO-M8B-002 — **RESOLVED 2026-05-09 (M8.C)**

**Resolution:** New `tests/ToastRevival.Api.Tests/RegistrationLoadTests.cs` shipped at M8.C with `Registration_ConcurrentBurst_AllSucceed_NoCollisions_ConsumedCountAccurate`. Stands up its own `ApiTestFactory` against the shared `PostgresFixture` for a fresh rate-limit window (the `device-per-hour` policy partitions on `RemoteIpAddress?.ToString() ?? "anon"` for unauthenticated registrations, so a shared factory would leak budget from prior tests). Issues 8 concurrent `POST /api/devices/register` calls (sized below the 10/hr `"anon"` partition cap with retry headroom), asserts all succeed with unique DeviceIds, and verifies `Tenant.ConsumedCount == 8` to catch any concurrent-write loss in the licensing path. Opt-in via `TOAST_TEST_RUN_REGISTRATION_LOAD=1` — same env-gating pattern M8.B established for the 1,000-device fanout variant.

### INFO-M8B-003 (open, no action — environmental)

`dotnet build` and `dotnet test` are blocked locally on this dev box by Microsoft Defender's load-time block on `Microsoft.AspNetCore.Mvc.Testing.Tasks.dll` and on freshly-compiled `ToastRevival.Api.Tests.dll`. ACL is `FullControl`; bash can read the bytes (MZ header confirmed); the .NET runtime gets `E_ACCESSDENIED (0x80070005)` when `Assembly.LoadFile()`-ing them. Same root cause that constrained M8.A to CI-only test execution — a Defender real-time scan that intercepts code-load on `.nuget` paths but not on relocated copies. Workaround verified locally: `-p:_MvcTestingTasksAssembly=<copy-of-the-DLL-outside-.nuget>` lets the build succeed (0 warnings, 0 errors). The CI runner's Linux Ubuntu environment does not reproduce. M8.A precedent stands: CI is the verification gate.

### FIX-M7D-001 — **RESOLVED 2026-05-09 (pre-commit)** (Defensive `</script>` escape on JSON-LD serialization)

**Filed:** 2026-05-09 during Abish's M7.D Code Sweep (Step 5 — security perspective).
**Surface:** `src/ToastRevival.Dashboard/src/lib/seo.ts` (`useSeo` hook, JSON-LD `<script>` injection).
**Symptom:** `script.text = JSON.stringify(jsonLd)` — current schema payload has no `</script>` substrings, so behavior is correct today. But if any future schema field ever holds user-controllable text containing a literal `</`, it would close the script tag prematurely and execute the rest of the page as inline script context. Latent injection vector.
**Fix applied:** `script.text = JSON.stringify(jsonLd).replace(/<\//g, '<\\/')`. JSON parsers accept the backslash-escaped form unchanged; HTML parsers no longer see a closing tag.
**Verification:** Build clean (730 modules). seo chunk gained ~20 bytes (3.79 kB vs 3.77 kB).
**Blocking:** No — defensive only, no live exploit path with current static schema. Caught and patched before commit.

### INFO-M7C-005 — **RESOLVED 2026-05-09 (M7.D)** (Docs body reads soft on light bg)
**Resolution:** Docs body copy (`.m-docs-content p`, `.m-docs-content ul/ol`) bumped from `--text-secondary` to `--text-primary`. Chrome surfaces (sidebar, footer, labels) keep `--text-secondary`. Standing rule: reading-grade prose lands a notch darker than utility text for sustained readability.

### INFO-M7D-001 (open, M9 candidate)
`src/ToastRevival.Dashboard/public/sitemap.xml` hardcodes `<lastmod>2026-05-09</lastmod>` for every URL. Manual update required on subsequent doc edits. Acceptable at MVP scale; promote to a build-time generator (or static-pull from git mtime) at M9.

### INFO-M7D-002 (open, Codex-track candidate)
After M7.D, `index.html` ships marketing-flavored default `<title>`/description/OG tags. Marketing pages override per-route via `useSeo`. Authenticated dashboard pages currently inherit the marketing defaults until React mounts (no dashboard-track `useSeo` calls yet). Codex's admin UI redesign track may add per-route titles via the same hook if desired; otherwise tab-title parity with the previous "Admin Dashboard" string is lost. Acceptable trade-off for a public marketing launch; Codex owns the dashboard chrome decision.

### INFO-M7D-003 (open, no action)
`useSeo` runs in `useEffect` — modern AI/search crawlers (Googlebot, Anthropic, OpenAI, Perplexity) execute JS and see per-page meta + JSON-LD. Legacy/non-JS crawlers see `index.html` defaults only (canonical → `/`, OG → marketing Home). Acceptable for a 2026 SaaS launch; pre-rendering or SSR is out of scope until product traction warrants the build complexity.

### FIX-M7C-001 — **RESOLVED 2026-05-09 (pre-commit)** (Mobile docs nav links below 44px tap target)

**Filed:** 2026-05-09 during Abish's M7.C Code Sweep (Step 5 — architectural / Diana standing rule).
**Surface:** `src/ToastRevival.Dashboard/src/components/marketing/marketing.css` (`.m-docs-nav-link` mobile breakpoint).
**Symptom:** Desktop `.m-docs-nav-link` was `padding: 8px 12px` font-size 14, line-height ~20px → tap target ~36px on mobile. Diana's standing rule (Mobile touch targets must be explicitly spec'd at 44px minimum, 2026-04-12) was violated.
**Fix applied:** Added mobile-breakpoint override (`@media max-width: 1023px`) bumping `.m-docs-nav-link` to `min-height: 44px`, `padding: 12px 12px`, `font-size: 15px`, removing the desktop 2px left-border treatment for the larger touch surface.
**Verification:** Rebuilt 729 modules clean. Playwright at 480×900 confirms the open mobile nav links exceed 44px.
**Blocking:** No — caught and patched before any commit shipped.

### INFO-M7C-005 (open, M7.D candidate)
Diana, post-deploy review: `--text-secondary` (#4B5563) reads slightly soft on `--bg-primary` (#F3F5F8) for docs body. WCAG AA compliant but Keith's standing comment ("my old eyes hurt, it's too faint") may flag. M7.D candidate: bump body to `--text-primary` and reserve `--text-secondary` for fine print / nav inactive.

### INFO-M7C-003 — **RESOLVED 2026-05-10 (session 3)**

**Filed:** 2026-05-09 (M7.C Code Sweep — nav path consistency).
**Surface:** `src/ToastRevival.Dashboard/src/components/marketing/DocsLayout.tsx` (NAV_GROUPS), `src/ToastRevival.Dashboard/src/App.tsx` (docs route children).
**Resolution:** Docs route paths extracted to a shared constants file `src/ToastRevival.Dashboard/src/routes/docsRoutes.ts` exporting `DOCS_PATHS` (`as const` object: index, gettingStarted, deployStore, deployIntune, deployRmm, api). Both `App.tsx` and `DocsLayout.tsx` import `DOCS_PATHS` and reference the same string values. Nav sidebar links and router route definitions are now structurally coupled — adding a path to one without the other causes a TypeScript reference error that surfaces at build time, not as a silent 404 at runtime. Verified: all six paths present in both consumers, routes and nav match exactly.

### INFO-M7C-002 (open, no action)
`m-footer-grid--slim` widened from 3-col to 4-col to host the new `Resources` column. Codex did NOT modify `MarketingFooter.tsx` (verified via `git diff --cached`), so no merge conflict expected. Mobile breakpoint preserves single-column.

### INFO-M7C-001 (open, M9 candidate)
`CodeBlock` clipboard API (`navigator.clipboard.writeText`) has no fallback for sandboxed/insecure contexts. Fails silently — button stays default. Acceptable v1 (HTTPS site, modern browsers). Add `document.execCommand('copy')` fallback at M9 polish.

### FIX-PROD-002 — **RESOLVED 2026-05-09** (Register flow had never worked end-to-end)

**Filed:** 2026-05-09 (Keith hit "Create account" on production with valid inputs and saw "One or more validation errors occurred." with no detail).
**Surface:** `src/ToastRevival.Dashboard/src/api/auth.ts`, `src/ToastRevival.Dashboard/src/api/client.ts`, `src/ToastRevival.Dashboard/src/contexts/AuthContext.tsx`, `src/ToastRevival.Api/DTOs/AuthDtos.cs`, `src/ToastRevival.Api/Controllers/AuthController.cs`.
**Symptom:** API returned 400 with three `[Required]` validation errors (`Email`, `Password`, `Subdomain`) but the UI surfaced only the boilerplate ProblemDetails title.
**Root cause:** Four independent bugs stacked — (1) frontend sent `adminEmail/adminPassword`, backend expected `Email/Password`; (2) backend required a `Subdomain` field the UI never collected; (3) backend `AuthResponse` didn't include `Email` even though frontend reads it; (4) frontend client.ts ignored the field-level `errors` map in ProblemDetails responses, so users saw no actionable error message. None caught by Code Sweep — bugs only surface when frontend payload meets live backend. Latent since M1 shipped 2026-05-08.
**Fix applied:** Backend `RegisterRequest.Subdomain` made optional; auto-derived from `TenantName` via slugify with random suffix on collision. `AuthResponse` adds `Email` field, returned in both Register and Login. Frontend `RegisterRequest` interface uses `{ tenantName, email, password, subdomain? }`. `AuthResponse` interface adds `refreshToken`, `expiresAt`, `email`. `client.ts` extracts ProblemDetails `errors` (Record<string, string[]> or string[]) before falling back to `detail`/`message`/`title`.
**Verification:** Curl with full payload returned a clean AuthResponse (token + email + refreshToken + role=Admin). Curl with missing fields returned the per-field errors ready for the UI. Playwright UI walkthrough on production: register form → dashboard, zero console errors, sidebar populated with email. HTML5 `minLength=8` on the password input blocks submission of obviously-bad passwords before the API is hit. Two smoke-test tenants created during verification were transactionally deleted from prod (`Tenants`, `AspNetUsers`, `NotificationTemplates` counts all back to 0). See `EVIDENCE/2026-05-09-fix-prod-002-register-flow.md`.
**Standing rule:** Every milestone that ships a frontend → backend interaction MUST include a curl-with-real-payload + Playwright UI smoke against the deployed environment before close. Code Sweep does not catch payload-shape mismatches; only end-to-end smoke does.
**Blocking:** YES — register endpoint was broken from the day it shipped (2026-05-08) until 2026-05-09. Now resolved.

### FIX-PROD-001 — **RESOLVED 2026-05-09** (Production blank-page blocker)

**Filed:** 2026-05-09 (Keith reported `/register` shows a blank white page on production).
**Surface:** `src/ToastRevival.Dashboard/vite.config.ts` (build config) + nginx `/etc/nginx/sites-enabled/toast` on TOASTWEB1 (config not changed).
**Symptom:** Every dashboard route on https://toastnotification.com (including `/register`, `/login`, `/`) rendered a blank white page in the browser. View-source showed the SPA shell HTML with `<div id="root"></div>` and a `<script src="/assets/index-DelPZakl.js">`. The script returned 404, so React never bootstrapped.
**Root cause:** Path collision between nginx's `location /assets/ { proxy_pass http://localhost:5216; }` block (added in M5.C for the asset library API to serve user-uploaded hero/logo files from `wwwroot/assets/{tenantId}/{file}`) and Vite's default build output directory (`dist/assets/index-*.js`). Every SPA bundle request was proxied to ASP.NET, which had no route → 404, so the SPA never bootstrapped.
**Fix applied:** Added `assetsDir: 'static'` to `vite.config.ts` `build` config. Vite now emits `dist/static/index-*.js` and the generated `index.html` references `/static/...`. nginx's `try_files $uri $uri/ /index.html` SPA fallback in the catch-all `location /` block serves `/static/*` from `/opt/toast/dashboard/static/` directly. Asset library URL pattern `/assets/{tenantId}/{file}` is unchanged — still proxies to ASP.NET.
**Verification:** `curl https://toastnotification.com/static/index-B04HT6PW.js` → 200, 718 KB. Playwright loaded `/register` and `/login` cleanly with zero console errors. See `EVIDENCE/2026-05-09-fix-prod-001-static-assets-dir.md`.
**Standing rule:** If nginx has any `location /<prefix>/ { proxy_pass ... }` blocks, the Vite/build static output prefix MUST NOT match any of them. Default `assets` is unsafe in this project — `/assets/` is owned by the asset library API. Code Sweep Step 4 now cross-references `vite.config.ts build.assetsDir` (or analogous build config) against `/etc/nginx/sites-enabled/*` `location` directives before declaring a deploy clean.
**Blocking:** YES — was blocking every customer signup until resolved. Now resolved.

### FIX-CI-002 — **RESOLVED 2026-05-09**

**Filed:** 2026-05-09 (immediately after FIX-CI-001 unblocked the WiX install).
**Surface:** `installer/ToastRevival.Agent.Setup.wxs` (lines 78 and 132 — XML comment bodies).
**Symptom:** After FIX-CI-001 pinned WiX to 5.0.2 and the runner installed cleanly, the next "Build unsigned MSI" run failed with `error WIX0104: Not a valid source file; detail: An XML comment cannot contain '--', and '-' cannot be the last character. Line 78, position 16.`
**Root cause:** Two XML comments in the WiX source documented the agent's `--setup-bootstrap` CLI mode by writing the literal flag inside `<!-- ... -->`. XML 1.0 forbids the sequence `--` inside comment bodies. The flag itself appearing in the `ExeCommand` attribute (line 145) is fine — attribute parsing has no such restriction. Latent since M2.C (commit `ecf79ce`, the M2.C MSI bootstrap property wiring); the local `build-msi.ps1` script had not been re-run after the M2.C edits, so the violation never surfaced until CI built it on `windows-latest`.
**Fix applied (commit pending):** Rephrased both comment bodies to drop the literal double-dash. Functional behavior unchanged — the actual `--setup-bootstrap` invocation in the deferred `WriteBootstrapJson` custom action remains intact at line 145. Added an inline note in the second comment acknowledging the XML constraint so a future maintainer doesn't reintroduce the literal flag. Standing rule: documentation prose inside XML comments may NOT contain the literal `--` sequence; rephrase or move the doc to a sibling .md file.
**Blocking:** No — only the CI MSI artifact pipeline. Local Keith builds work because Keith hadn't run the script post-M2.C; he would have hit the same error on his next manual rebuild had we not caught it first.

### FIX-CI-001 — **RESOLVED 2026-05-09**

**Filed:** 2026-05-09 (Keith forwarded a recurring "Agent MSI Build" failure email after the M7.A push).
**Surface:** `.github/workflows/agent-build.yml` (Install WiX step).
**Symptom:** Every push to `main` since commit `1c41d3e` (CI runner switch) failed identically at the "Build unsigned MSI" step. Five back-to-back red runs across `f68295b` (M5.D), `ed928d2`, `918c723` (M6), `df41216`, `ca0972a` (M7.A). Failure window was always ~3:41 — long enough to clear restore + dotnet build, fast enough to suggest the WiX invocation itself.
**Root cause:** `dotnet tool install -g wix` (no version pin) resolves to **WiX 7**, the current latest. WiX 7 introduces the Open Source Maintenance Fee EULA — `wix build` aborts at the very first invocation with `error WIX7015: You must accept the Open Source Maintenance Fee (OSMF) EULA to use WiX Toolset v7.` Local dev uses WiX 5.0.2 (the locked version since M0A); the CI runner had been silently drifting forward with every workflow run, and the WIX7015 gate landed when WiX 7 became default.
**Fix applied (commit pending):** Pinned the install command to `dotnet tool install -g wix --version 5.0.2` with a header comment explaining why. Standing rule: any tool version that the local dev environment locks must be pinned in CI; "latest" is not a version.
**Blocking:** No — agent MSI builds were broken in CI but Keith's local signed-MSI pipeline was unaffected (he installs WiX manually via `dotnet tool install -g wix --version 5.0.2`).

### INFO-M7A-001 — **RESOLVED 2026-05-09 (pre-commit)**

**Filed:** 2026-05-09 (M7.A Code Sweep)
**Surface:** `.gitignore`, `Docs/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem`, `Docs/Assets/Toast_Data_1_LightsailDefaultKey-us-east-1.pem`
**Issue:** Lightsail SSH private keys for TOASTWEB1 and TOASTDATA1 were sitting untracked in `Docs/Assets/` with no `.gitignore` rule. A future `git add .` or `git add -A` (against the standing project rule, but trivially possible) would have committed key material to a public repo.
**Resolution:** Added `*.pem` and `*.key` patterns to `.gitignore` with `letsencrypt/` allow-list reservation. `git check-ignore -v Docs/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem` confirms both keys are now ignored. No key material was ever staged. Standing rule going forward: SSH keys, signing keys, and TLS private keys belong in `.gitignore` by extension, not by location.

### INFO-M7A-002 (low) — DEPLOY.md path discrepancy

**Filed:** 2026-05-09 (M7.A Code Sweep)
**Surface:** `Docs/ToastRevival/DEPLOY.md:451`
**Issue:** The redeploy procedure references the Web key at `Docs/ToastRevival/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem`, but the actual file lives at `Docs/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem` (workspace-root `Docs/Assets/`, not `Docs/ToastRevival/Assets/`). Anyone copy-pasting the redeploy script gets `scp: No such file or directory`.
**Fix (deferred to M7.D):** Either update `DEPLOY.md` to point at `Docs/Assets/...`, or move the key file under `Docs/ToastRevival/Assets/` to match the doc. Fix in the same session that runs the M7 marketing-site deploy — the redeploy procedure will be exercised then.
**Blocking:** No (current redeploy works because Keith knows where the keys actually live).

### INFO-M7A-003 (medium) — Hero "real notification count" needs a public endpoint or removal

**Filed:** 2026-05-09 (M7.A Code Sweep)
**Surface:** Marketing Home hero ("Already trusted by MSPs to deliver [N] notifications.")
**Issue:** The DESIGN-SPEC says the hero may render a real lifetime notification count fetched from `GET /api/analytics/global-summary` (a public, unauthenticated, cross-tenant aggregate). That endpoint does not exist. The spec also says "skip this line when the count is under 1000; we're not faking traction" — which is the de-facto current state.
**Fix (M7.B):** Either (a) add the public endpoint with a tenant-aggregate count and gate the hero line on `count >= 1000`, or (b) drop the line from the hero entirely until M9 GA. Whichever Carl picks, document the choice in the M7.B notes. Do NOT ship a hardcoded or invented number — Diana's standing rule against faked traction.
**Blocking:** No (the spec already says "skip when low"; M7.B build must just respect that).

### INFO-M7A-004 (acceptable / standing vigilance) — Third-party script discipline

**Filed:** 2026-05-09 (M7.A Code Sweep)
**Surface:** Marketing site build pipeline (M7.B/C/D).
**Issue:** The DESIGN-SPEC states "no third-party analytics scripts. No marketing pixels. No GTM. We don't need to know what you click on this page." This is enforceable but easy to violate in a build session if someone reaches for a quick analytics drop-in.
**Standing check:** Code Sweep on M7.B/C/D must include a grep of the diff for `googletagmanager`, `google-analytics`, `gtag(`, `fbq(`, `hotjar`, `segment`, `mixpanel`, `posthog`, `intercom`, `drift`, `cookieconsent`, `osano`, `onetrust`. If any of those land in the marketing routes, HOLD.
**Blocking:** No (preventative; not a current defect).



### FIX-MSIX-001 — **RESOLVED 2026-05-08 (M0 D5)**

**Filed:** 2026-05-07 (M0 D2 Code Sweep)
**Surface:** `scripts/build-msix.ps1`
**Root cause discovered (M0 D5):** Setting `<TargetPlatformVersion>` in a csproj conditional PropertyGroup does NOT work. The .NET SDK TFM (`net8.0-windows10.0.19041.0`) sets `TargetPlatformVersion=10.0.19041.0` in a late `.targets` import that runs AFTER PropertyGroup evaluation, silently overriding any csproj value.
**Fix applied:** Added `-p:TargetPlatformVersion=10.0.22621.0` to the `dotnet build` invocation in `scripts/build-msix.ps1`. Command-line flags have higher MSBuild precedence than imported `.targets`. Produced manifest verified: `MaxVersionTested="10.0.22621.0"` ✓. See CONTEXT.md standing rule #4.

### INFO-D5-001 — **RESOLVED 2026-05-09 (M2.A)**

**Filed:** 2026-05-08 (M0 D5 Code Sweep)
**Resolved:** 2026-05-09 (M2.A, FIX-M2A-001 patch + named mutex implementation)
**Surface:** `src/ToastRevival.Agent/Program.cs`
**Resolution:** `AgentEntryPoint.RunAsync` now takes a session-local named mutex (`Local\Toast2IT.ToastNotification.PrimaryWorker`) before entering primary worker mode. Activation mode + diagnostic mode both short-circuit BEFORE the mutex acquisition (their flows are short-lived and must not block the long-running primary). `WaitOne(TimeSpan.Zero)` non-blocking try; if held, exit code 5 with a clear stderr message. `AbandonedMutexException` catch path takes ownership when the previous holder crashed without releasing. `Local\` prefix (NOT `Global\`) — verified during Code Sweep that `Global\` would regress M0 D4 multi-user verification by colliding across Windows sessions; FIX-M2A-001 patched the prefix pre-commit.

### INFO-D5-002 (low) - MSI + MSIX simultaneous install fires two toasts per logon

**Filed:** 2026-05-08 (M0 D5 Code Sweep)
**Surface:** Deployment documentation (M0 D6, M7)
**Issue:** If both the MSI (Scheduled Task channel) and MSIX (startupTask channel) are installed simultaneously on the same machine, both launch mechanisms fire independently at logon, producing two toasts per session.
**Fix:** Document in M0 D6 deployment findings: "Do not install MSI and MSIX on the same endpoint. Choose one channel — MSI for RMM-managed deployment, MSIX/Store for user-managed." INFO-D5-001 mutex guard would also limit blast radius.
**Blocking:** No.

### FIX-MSIX-002 (low) - Manifest MinVersion vs. runtime gate divergence — **RESOLVED 2026-05-08**

**Filed:** 2026-05-07 (M0 D2 Code Sweep)
**Resolved:** 2026-05-08 (M0 D4 pre-work, commit pending)
**Surface:** `src/ToastRevival.Agent/Package.appxmanifest`, `src/ToastRevival.Agent/ToastRevival.Agent.csproj`

**Fix applied (Option b):** bumped `TargetDeviceFamily MinVersion` and `<TargetPlatformMinVersion>` from
`10.0.17763.0` (Win10 1809) to `10.0.19041.0` (Win10 2004 / build 19041), matching the `Program.cs`
runtime check. Manifest version bumped to `0.2.1.0`. Win10 1809 installs now fail at `Add-AppxPackage`
with a clear "requires Windows 10.0.19041.0" error rather than installing successfully and failing
silently at runtime. See `EVIDENCE/2026-05-08-m0-d4-fix-msix-002.md`.

**Win10 1809 lab verification:** not performed (no 1809 lab machine). Acceptable — the fix is
preventative for a platform below the product's stated floor, and the lab machine is Win11.

### FIX-MSIX-004 (medium) - Packaged MSIX install does not fire toasts - **RESOLVED 2026-05-08 (commit `6e3495c`)**

**Update 2026-05-08 (post-0.2.0.2 install attempt):** DiagLog from 0.2.0.2 install captured `AppNotificationManager.Default.Register()` throwing `COMException 0x80070490` (`HRESULT_FROM_WIN32(ERROR_NOT_FOUND)`) before `Show()` was reached. Original FIX-MSIX-004 hypothesis (Show silently no-ops) was wrong; Register() itself was the failure point. **Root cause: missing `Arguments="----AppNotificationActivated:"` on `<com:ExeServer>`.** Microsoft's packaged-WinAppSDK quickstart sample includes the four-dash sentinel; the framework uses it as the activator surface marker, and Register()'s COM class registration lookup fails ERROR_NOT_FOUND without it.

**Resolution:** 0.2.0.3 patch (commit `6e3495c`) added the Arguments token. Keith signed + installed via Add-AppxPackage on Win11 lab. DiagLog confirmed `Register() returned without throwing`, `Show() returned without throwing`, and `NotificationInvoked` fired with the expected argument payload after Keith clicked the toast's Acknowledge button. Single visible toast appeared, no duplicates, button-click routed cleanly. CONTEXT.md "Toast Activator Class ID" section captures the standing rule for the Arguments token going forward. See `EVIDENCE/2026-05-08-m0-d2-fix-msix-004-register-not-found.md` and `EVIDENCE/2026-05-08-m0-d2-toast-fires-packaged.md`.



**Filed:** 2026-05-07 (M0 D2 install validation)
**Patch built:** 2026-05-08 (`ToastNotification.Agent-0.2.0.2.msix`, unsigned)
**Surface:** Win11 lab machine, signed `ToastNotification.Agent-0.2.0.1.msix` installed via Add-AppxPackage.

**Symptom (0.2.0.1):** Console window flashes when the package launches via Start menu tile or `shell:appsfolder\<AUMID>`, but no toast banner appears, no entry lands in Action Center (Win+N), and Settings -> System -> Notifications -> Toast Notification shows no Notification history. The same agent code shipped via the M0A MSI fires toasts reliably (Startup-folder shortcut, unpackaged path).

**Hypothesis:** `Package.appxmanifest` was missing the COM activator declarations that the WinAppSDK packaged toast path requires. For UNPACKAGED apps, `AppNotificationManager.Default.Register()` auto-injects a CLSID into `HKCU\SOFTWARE\Classes\CLSID\...` so the toast pipe is wired implicitly (that's why M0A MSI works). For PACKAGED apps, the framework looks up the activator CLSID from the manifest; without those declarations, `Register()` returns success at the API surface but the activation channel never wires.

**Patch shipped in 0.2.0.2 (commit pending):**

1. **CLSID locked**: `7FA7762F-41EC-4D72-9F06-58964AB36FEA` (generated 2026-05-08 via `[guid]::NewGuid()`; documented in `CONTEXT.md` -> Toast Activator Class ID).
2. **Manifest patch in `src/ToastRevival.Agent/Package.appxmanifest`**:
   - Added `xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10"` and `xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"` to `<Package>`.
   - Added `com desktop` to `IgnorableNamespaces`.
   - Added `<Extensions>` **inside `<Application>`** (NOT at Package level — first build attempt with Extensions at Package level failed schema validation `C00CE014` "Element ... unexpected according to content model of parent element"). Both `<com:Extension Category="windows.comServer">` and `<desktop:Extension Category="windows.toastNotificationActivation">` go inside `<Application>` per Microsoft's quickstart.
   - Both CLSIDs (`com:Class Id` and `ToastActivatorCLSID`) byte-for-byte identical.
3. **Diagnostic logging in `src/ToastRevival.Agent/Program.cs`**: `DiagLog` static class writes to `Windows.Storage.ApplicationData.Current.LocalFolder.Path\agent.log` when packaged, falls back to `%LOCALAPPDATA%\Toast2IT\Toast Notification\agent.log` when unpackaged. Logs at app start (with pid/args/baseDir/IsPackaged), pre/post `Register()`, pre/post `Show()`, exception path, every exit code.
4. **Version bumped to 0.2.0.2** in manifest + `scripts/build-msix.ps1` default.

**Hand-off (Keith):**
  1. Sign: `.\scripts\sign-msix.ps1 -Path artifacts\installer\msix\ToastNotification.Agent-0.2.0.2.msix`.
  2. Install on Win11 lab: `Add-AppxPackage -Path <signed-msix>` (or `-ForceUpdateFromAnyVersion` if 0.2.0.1 is still installed).
  3. Launch from Start menu tile (NON-elevated; the IsElevated guard at Program.cs:13 exits 3 in elevated context).
  4. Look for: visible toast banner (bottom-right), Action Center entry (Win+N), Settings -> System -> Notifications -> Toast Notification -> Notification history.
  5. Pull `agent.log` from `%LOCALAPPDATA%\Packages\Toast2IT.ToastNotification.Agent_8gxm9tzcy3sby\LocalState\agent.log` and ship it back.

**If toast fires:** mark M0 D2 complete in MILESTONES.md, move FIX-MSIX-004 to Resolved, capture EVIDENCE/2026-05-08-m0-d2-toast-fires-packaged.md.

**If toast still doesn't fire (fallback diagnostic tree):**
  - Read agent.log: did Register throw? Did Show return? What AUMID was used at runtime?
  - Check `TargetDeviceFamily MaxVersionTested="10.0.19041.0"` vs lab Win11 build (`[Environment]::OSVersion.Version.Build`). If lab build > 22000 there could be a notifications-suppressed-when-tested-version-too-low side effect (FIX-MSIX-001 already tracks bumping MaxVersionTested for Store flight; consider pulling forward).
  - Verify `BackgroundColor="#0F1117"` is a valid 6-char hex (it is; ruled out).
  - Check packaged AUMID via `Get-StartApps | Where-Object { $_.Name -like '*Toast*' }` and compare to the Identity-derived AUMID (`Toast2IT.ToastNotification.Agent_8gxm9tzcy3sby!App`).

**Reference:** Microsoft docs on packaged WinAppSDK toast activation: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/notifications/app-notifications/app-notifications-quickstart (Packaged section).

**Blocking:** YES for M0 D2 close. Cannot mark D2 complete until visible toast verified on signed 0.2.0.2.

### FIX-MSIX-003 (cosmetic) - mspdbcmf.exe warning during MSIX build

**Filed:** 2026-05-07 (M0 D2 Code Sweep)
**Surface:** `scripts/build-msix.ps1` invocation of `dotnet build`.
**Issue:** Warning "Path to mspdbcmf.exe could not be found. A symbols package will not be generated." prints during every MSIX build. Benign — only suppresses optional .appxsym output.
**Fix:** Add `-p:SymbolPackageFormat=none` to the `dotnet build` invocation in `build-msix.ps1`, OR install Visual Studio Build Tools 2022's debugging tools workload. Cosmetic only.
**Blocking:** No.

### INFO-M1-001 — DeviceGroupMember missing global query filter (low)
**Filed:** 2026-05-08 (M1 Code Sweep)
**Surface:** `Data/AppDbContext.cs`, EF model validation warning
**Issue:** EF Core warning: "Entity 'Device' has a global query filter defined and is the required end of a relationship with 'DeviceGroupMember'." `DeviceGroupMember` has no TenantId column so cannot have its own filter. In practice it is only ever loaded through Device or DeviceGroup (both filtered), so cross-tenant leakage is not possible via normal query paths.
**Fix:** Acceptable as-is for M1. Could add a TenantId column to DeviceGroupMember in a future migration if the warning becomes a compliance concern.
**Blocking:** No.

### INFO-M1-003 — **RESOLVED 2026-05-10 (M9.C, forward-only)**
**Filed:** 2026-05-08 (M1 Code Sweep)
**Surface:** `Controllers/DevicesController.cs` `POST /api/devices/register`, `Controllers/AuthController.cs` `Initiate` + legacy `Register`, `Controllers/TenantController.cs`, `DTOs/TenantDtos.cs`, `src/ToastRevival.Dashboard/src/components/DeployCommand.tsx`.
**Resolution:** Auto-generation of `EnrollmentKey` on every new `Tenant` row (24 bytes via `RandomNumberGenerator.GetBytes` → base64). Existing 3 prod tenants backfilled via psql + `pgcrypto.gen_random_bytes(24)`. `TenantSettingsResponse` exposes the key to admin role only (Technicians get `null`). New `POST /api/tenant/enrollment-key/regenerate` (admin-gated) for rotation. `DeployCommand.tsx` fetches `/api/tenant/settings` on mount and includes `ENROLLMENTKEY=<key>` in the msiexec command, surfacing the value in the parameter chip row. `DevicesController.Register` gate semantics preserved (validates when tenant has key set) — every tenant now has a key, so the gate fires for all registrations going forward. Agent side already coded pre-this-session (`BootstrapConfig.EnrollmentKey`, `RegistrationService.RegisterAsync` sends it, WiX MSI declares the `ENROLLMENTKEY` property and pipes through `--setup-bootstrap`); ships next signed agent build.
**Backwards compat note:** v0.3.x agents in the field that don't pass `enrollmentKey` will be 403'd at `/api/devices/register` after this milestone. Field install base = Keith's lab only, which won't re-register until the next signed MSI ships.

### INFO-M9B-001 — **RESOLVED 2026-05-10 (M9.C, source-only)**
**Filed:** 2026-05-10 (M9.B Code Sweep)
**Surface:** `src/ToastRevival.Agent/AgentClient.cs::RunCatchupAsync`
**Resolution:** Agent now passes `&limit=500` (the M9.B cap) and loops until a partial page is returned. `MaxLoops=64` ceiling guards against runaway. Per-iteration `since` advances to `items[^1].CreatedAt + 1 tick`. DiagLog tracks total drained across pages. With `CatchupPageSize=500` and the `device-catchup-per-hour=60` rate limit, the per-hour drain ceiling is 30,000 notifications. Source change only — ships next signed agent build alongside INFO-M9C-002 (DiagLog rotation) when that lands.

### INFO-M1-004 — No test coverage (low)
**Filed:** 2026-05-08 (M1 Code Sweep)
**Surface:** `src/ToastRevival.Api/` — entire project
**Issue:** Zero unit tests, zero integration tests.
**Fix:** M8 integration testing milestone. For earlier milestones, individual controller/service tests can be added incrementally.
**Blocking:** No.

### INFO-M1-005 — **RESOLVED 2026-05-09 (M5.C)**

**Filed:** 2026-05-08 (M1 Code Sweep)
**Resolved:** 2026-05-09 (M5.C)
**Surface:** `Services/NotificationQueueService.cs`
**Resolution:** `EnqueueDueScheduledAsync` runs at startup (backfill) and every 60 seconds (via `RunSchedulerLoopAsync` PeriodicTimer). Backfill loads `Notifications WHERE Status=Queued AND ScheduledAt<=now` and enqueues them. Timer tick does the same sweep continuously. `ProcessAsync` now guards on `Status != Queued` to prevent double-fanout if a startup + timer tick overlap. Both tasks run concurrently via `Task.WhenAll` alongside the existing queue consumer (`ProcessQueueAsync`).

### INFO-M1-006 — RESOLVED 2026-05-09 (see entry above for resolution detail)

### INFO-MSIX-004-D — **RESOLVED 2026-05-09 (M2.A)**

**Filed:** 2026-05-08 (M0 D2)
**Resolved:** 2026-05-09 (M2.A activation handler implementation)
**Surface:** `src/ToastRevival.Agent/Program.cs`, `src/ToastRevival.Api/Controllers/NotificationsController.cs`
**Resolution:** `AgentEntryPoint.TryFindActivationArg` detects the framework sentinel `----AppNotificationActivated:` in argv before mutex acquisition or hub spin-up. When matched, `ActivationMode.RunAsync` takes over: (1) loads `DeviceConfig` from disk; (2) subscribes to `AppNotificationManager.Default.NotificationInvoked`; (3) calls `Register()` (the framework fires `NotificationInvoked` synchronously during this call with the original toast's argument string); (4) parses click args; (5) if `source==hub`, posts to new device-JWT-authenticated `POST /api/notifications/{notificationId}/interactions` REST endpoint via `InteractionFallback.PostAsync`; (6) calls `Unregister()` and exits clean. 5-second timeout on the NotificationInvoked wait (exit 7) and 15-second timeout on the REST POST. Activation mode never spins up SignalR or contests the primary mutex.

### INFO-M2A-002 — RESOLVED 2026-05-08 (M3, commit 362f9d3) — see entry above for resolution detail.

### INFO-M2A-003 — **RESOLVED 2026-05-09 (M2.B)**

**Filed:** 2026-05-09 (M2.A Code Sweep)
**Resolved:** 2026-05-09 (M2.B, `NotificationQueueService.RecoverOrphansAsync`)
**Surface:** `src/ToastRevival.Api/Services/NotificationQueueService.cs::RecoverOrphansAsync` (new, called once at `ExecuteAsync` startup before the channel loop).
**Resolution:** Sweep `Notifications WHERE Status=Sending AND SentAt < now() - INTERVAL '5 minutes'` → Status=`Failed`, CompletedAt=now. **Pending deliveries are NOT touched** (Carl's M2.B overrule on the originally-planned "deliveries to Failed accordingly") — the `GET /pending` catch-up endpoint can still serve them to the agent on reconnect. The state divergence (notification Failed, deliveries Pending → Delivered later) is acceptable: dashboard sees Failed-fanout while delivery counts trickle up; the alternative (mark deliveries Failed) would have defeated catch-up entirely. Sweep is non-fatal (try/catch around it; the queue still serves new traffic if recovery fails). Idempotent — rerun after a fast restart finds nothing because the threshold rejects rows under 5 minutes old.

### INFO-M2A-004 — **RESOLVED 2026-05-09 (M2.B)**

**Filed:** 2026-05-09 (M2.A Code Sweep)
**Resolved:** 2026-05-09 (M2.B, `AgentHubClient.RenderAndReportAsync`)
**Surface:** `src/ToastRevival.Agent/AgentClient.cs::AgentHubClient` (`_renderedCache: MemoryCache<Guid, byte>`, 1-hour sliding expiration; checked in `RenderAndReportAsync`).
**Resolution:** Notification render + ReportDelivery now go through a shared `RenderAndReportAsync` helper called from both the hub-pushed path (`OnReceiveNotificationAsync`) and the catch-up path (`RunCatchupAsync`). Dedup short-circuits BOTH render AND ReportDelivery — once a notificationId has been delivered in this process, no path re-acknowledges it. The cache entry is set ONLY after `Show()` returns successfully, so a render failure does not poison the cache and prevents a future retry. Sliding window resets on every touch — a notification re-served on every reconnect for an hour stays cached.

### INFO-M2B-002 — **RESOLVED 2026-05-10 (M9.B)**

**Filed:** 2026-05-09 (M2.B Code Sweep)
**Surface:** `src/ToastRevival.Api/Controllers/NotificationsController.cs::GetPending`
**Resolution:** Added optional `[FromQuery] int limit = 100` parameter, server-clamped to `[1, 500]` via `Math.Clamp`. Default unchanged at 100 — v0.3.x agents in the field that omit the param continue to receive the same 100-cap response in the same array wire shape. New callers can request up to 500 items per call to drain large Pending backlogs in fewer round-trips. Wire shape preserved (still `PendingNotificationItem[]`), so no agent rebuild required this milestone. Agent-side adoption (`?limit=500` in `AgentClient.cs::RunCatchupAsync`) deferred to the next signed agent build (INFO-M9B-001 carry-forward).
**Test:** `SecurityTests.PendingEndpoint_LimitParamControlsPageSize_ClampsToBounds` exercises default (100), explicit-in-range (limit=200 → 200), upper-clamp (limit=999 → 500), lower-clamp (limit=0 → 1) against a real Postgres container with 510 seeded Pending deliveries.

### INFO-M2B-003 — **RESOLVED 2026-05-09 (already shipped, doc fix M9.C)**

**Filed:** 2026-05-09 (M2.B Code Sweep)
**Surface:** `src/ToastRevival.Api/Data/AppDbContext.cs::OnModelCreating` (NotificationDelivery), migration `20260509024211_M3SecurityHardening`.
**Resolution:** The composite index `(DeviceId, Status, CreatedAt)` on `NotificationDeliveries` was added in migration `20260509024211_M3SecurityHardening` (constraint name `IX_NotificationDeliveries_DeviceId_Status_CreatedAt`). The model-level `e.HasIndex(d => new { d.DeviceId, d.Status, d.CreatedAt })` is at `AppDbContext.cs:110`. Catch-up query at `NotificationsController.GetPending` uses an index-aligned filter (`DeviceId == me AND Status == Pending`) followed by `OrderBy(CreatedAt)` — PostgreSQL plans this as an index range scan. The FIX-LIST entry was carried as open through M9.B because the resolution wasn't documented; M9.C closes the doc.

### INFO-M2B-004 — **RESOLVED 2026-05-08 (M3, commit `362f9d3`)**

**Filed:** 2026-05-09 (M2.B Code Sweep)
**Surface:** `src/ToastRevival.Agent/AgentClient.cs::AgentHubClient._renderedCache`
**Resolution:** `MemoryCacheOptions { SizeLimit = 50_000 }` + `Size = 1` on each entry. 50K × ~100 bytes ≈ 5MB ceiling.

### INFO-M2C-001 (M9 — pre-launch) — Tray icon HICON handles not freed

**Filed:** 2026-05-08 (M2.C Code Sweep)
**Surface:** `src/ToastRevival.Agent/TrayIconService.cs::CreateCircleIcon`
**Issue:** `Bitmap.GetHicon()` creates Win32 HICON handles. `Icon.FromHandle()` wraps them without taking ownership — handles are not freed when the Icon or TrayIconService is disposed. For process-lifetime tray icons (5 handles total), the leak is ~5 HICONs per agent session, released on process exit. Non-issue at current scale.
**Fix:** Before M9 GA: store HICON handles and call `DestroyIcon` (P/Invoke) in TrayIconService.Dispose(). Low priority until production tile assets replace placeholder GDI+ icons anyway.
**Blocking:** No.

### INFO-M2C-002 (M3) — SetupMode after OS version check

**Filed:** 2026-05-08 (M2.C Code Sweep)
**Surface:** `src/ToastRevival.Agent/Program.cs::AgentEntryPoint.RunAsync`
**Issue:** `--setup-bootstrap` detection is after the `IsWindowsVersionAtLeast(10,0,19041)` guard. On a sub-19041 machine, the WiX WriteBootstrapJson CA exits 2 and bootstrap.json is not written. This is the unsupported OS floor — the agent wouldn't run on that machine anyway. A MSI-level OS version condition (LaunchCondition or Condition on Feature) would prevent installs on unsupported OS entirely, eliminating the ambiguity.
**Fix:** Add `LaunchCondition` in WiX requiring `VersionNT64 >= 1904` (hex 0x774) at M3 or before M9.
**Blocking:** No.

### INFO-M2C-003 (acceptable) — async void ReconnectRequested lambda

**Filed:** 2026-05-08 (M2.C Code Sweep)
**Surface:** `src/ToastRevival.Agent/Program.cs::PrimaryMode.RunAsync`
**Issue:** `async void` lambda subscribed to `tray.ReconnectRequested`. Unhandled exceptions in async void crash the process. The entire body is wrapped in try/catch, which mitigates this. Pattern is consistent with existing `async void OnNotificationInvoked` in AgentClient.cs.
**Fix:** Acceptable as-is. If future modifications add code paths outside the try/catch, revisit.
**Blocking:** No.

### INFO-M2C-004 (acceptable) — TrayIconService 3s STA init wait

**Filed:** 2026-05-08 (M2.C Code Sweep)
**Surface:** `src/ToastRevival.Agent/TrayIconService.cs` constructor
**Issue:** Constructor blocks the calling thread up to 3 seconds waiting for `_uiReady`. In normal conditions the STA thread initializes in <50ms. Under extreme resource contention, if initialization exceeds 3 seconds, `_notifyIcon` may be null and the tray icon never appears. The agent functions normally — tray icon is cosmetic/UX surface, not correctness-critical.
**Fix:** Acceptable. The graceful degradation path (ApplyState null-checks _notifyIcon) is verified.
**Blocking:** No.

### INFO-M2D-003 (acceptable) — updateTask not awaited on shutdown

**Filed:** 2026-05-08 (M2.D Code Sweep)
**Surface:** `src/ToastRevival.Agent/Program.cs::PrimaryMode.RunAsync`
**Issue:** `updateTask` (background Velopack check loop) is started via `Task.Run` but never awaited in the cleanup path. On shutdown, `PeriodicTimer.WaitForNextTickAsync` returns false when the CancellationToken is cancelled; the task completes shortly after. All exceptions inside `RunUpdateLoopAsync` are caught within the loop body, so the task never faults. Fire-and-forget posture.
**Fix:** Acceptable. Same pattern as the conceptual model of `_pingLoop` in AgentHubClient. If an awaited-cleanup pattern is adopted at M9, add `updateTask` to the shutdown sequence.
**Blocking:** No.

### INFO-M2D-004 (acceptable) — _updateItem Font object not explicitly disposed

**Filed:** 2026-05-08 (M2.D Code Sweep)
**Surface:** `src/ToastRevival.Agent/TrayIconService.cs::RunMessageLoop`
**Issue:** `new System.Drawing.Font(SystemFonts.MenuFont!, FontStyle.Bold)` creates a GDI font object stored in the `_updateItem` ToolStripMenuItem's Font property. Not explicitly disposed in TrayIconService.Dispose(). Single process-lifetime object; negligible resource.
**Fix:** Acceptable. The ContextMenuStrip.Dispose() disposes child items but may not release the custom font. Before M9 GA: store reference in a field and Dispose() it explicitly.
**Blocking:** No.

### INFO-M2D-005 — RESOLVED 2026-05-08 (M3) — see entry above for resolution detail.

### INFO-M2D-006 (acceptable) — FastCallback hooks fire before DiagLog.Init()

**Filed:** 2026-05-08 (M2.D Code Sweep)
**Surface:** `src/ToastRevival.Agent/Program.cs` top-level statements
**Issue:** `VelopackApp.Build().OnAfterInstallFastCallback().OnAfterUpdateFastCallback().Run()` is called before `AgentEntryPoint.RunAsync` which calls `DiagLog.Init()`. The two FastCallback handlers call `DiagLog.Write()`. Because `DiagLog.LogFilePath` is `""` until `Init()` is called, `Write()` returns early and the messages are silently dropped.
**Fix:** Acceptable — FastCallbacks only fire during install/update lifecycle events, not normal startup. The lifecycle events are self-reporting (Velopack has its own log) and the dropped DiagLog messages carry no information that isn't already in Velopack's output. If verbose lifecycle logging becomes a requirement, call `DiagLog.Init()` before `VelopackApp.Build().Run()`.
**Blocking:** No.

### FIX-M3-001 — **PATCHED PRE-COMMIT 2026-05-08 (M3 Code Sweep — Abish caught)**

**Filed:** 2026-05-08 (M3 Code Sweep)
**Resolved:** 2026-05-08 (same session, before commit)
**Surface:** `installer/ToastRevival.Agent.Setup.wxs` `<Launch Condition>`
**Issue:** Condition written as `VersionNT64 >= 1904` — WiX `VersionNT64` is `major*100+minor` (Windows 10/11 = 1000), not the OS build number. `1000 >= 1904` evaluates false → MSI would have blocked installation on every Windows 10/11 machine with the message "requires Windows 10 version 2004".
**Fix:** Changed to `VersionNT64 >= 1000` (catches pre-Windows-10 installs; precise build-19041 floor is enforced at runtime by `Program.cs` line 54).
**Blocking:** WAS BLOCKING — patched before commit.

### INFO-M3-001 — RESOLVED 2026-05-09 (SEC-005) — see entry above for resolution detail.

### INFO-M3-002 (M4) — BlocklistService is concrete injection in NotificationsController

**Filed:** 2026-05-08 (M3 Code Sweep)
**Surface:** `src/ToastRevival.Api/Controllers/NotificationsController.cs`
**Issue:** `BlocklistService` is a concrete class injected directly; no `IBlocklistService` interface. Makes the controller hard to unit test without the DB.
**Fix:** Extract `IBlocklistService` interface at M4 when unit tests are introduced.
**Blocking:** No.

### INFO-M3-003 (M4) — ContentSafetyService logs to Console.Error

**Filed:** 2026-05-08 (M3 Code Sweep)
**Surface:** `src/ToastRevival.Api/Services/ContentSafetyService.cs`
**Issue:** Azure scan failures are written to `Console.Error` — not structured logging. Will disappear in production without log capture.
**Fix:** Inject `ILogger<ContentSafetyService>` at M4 when the DI logging infrastructure is wired.
**Blocking:** No.

### INFO-M2B-005 — **RESOLVED 2026-05-08 (M3)**

**Filed:** 2026-05-09 (M2.B Code Sweep)
**Resolved:** 2026-05-08 (M3, commit `362f9d3`)
**Surface:** `src/ToastRevival.Api/Program.cs`, `NotificationsController.cs`
**Resolution:** Added `device-catchup-per-hour` fixed-window policy (60 req/hr). Catch-up endpoint (`GET /api/notifications/pending`) switched from `device-per-hour` to `device-catchup-per-hour`. Existing `device-per-hour` (10/hr) retained for `ReportInteraction` and heartbeat ping.

### FIX-M2B-001 — **PATCHED PRE-COMMIT 2026-05-09 (M2.B Code Sweep)**

**Filed:** 2026-05-09 (M2.B Code Sweep — Abish caught)
**Resolved:** 2026-05-09 (same session, before commit)
**Surface:** `src/ToastRevival.Agent/AgentClient.cs::AgentHubClient._lastCatchupSince`
**Issue:** First implementation initialized `_lastCatchupSince = DateTime.UtcNow` at ctor. The catch-up GET would then send `since=<ctor_time>` on the very first call. Server filter `delivery.CreatedAt >= since` would have excluded EVERY pre-existing Pending delivery — exactly the case M2.B exists to fix (agent rebooted, has Pending from before the reboot, reconnects). The catch-up endpoint would have returned zero results in its primary scenario.
**Fix:** Changed `_lastCatchupSince` to nullable `DateTime?`, default null. First catch-up call omits the `since` query param entirely so the server returns all Pending up to the cap. Subsequent calls send the captured `nextSince` timestamp from the previous call. Side benefit: avoids time-zone coercion issues with `DateTime.MinValue.Kind=Unspecified` against Npgsql `timestamptz` columns.
**Blocking:** WAS BLOCKING — patched before commit. Build clean post-patch.

### INFO-M2A-005 (M9 — deploy doc) — Migration backfill requires Postgres 13+

**Filed:** 2026-05-09 (M2.A Code Sweep)
**Surface:** `src/ToastRevival.Api/Migrations/20260509002218_AddTenantSigningKey.cs`
**Issue:** The backfill SQL uses `gen_random_uuid()` which is built-in to Postgres 13+ (previously required `pgcrypto` extension). Acceptable for any modern Postgres deployment but the floor should be documented.
**Fix:** Document Postgres minimum-version (13+) in M9 deployment infra.
**Blocking:** No.

### INFO-M4-001 — **RESOLVED 2026-05-09 (M5.A)**

**Filed:** 2026-05-08 (M4 Code Sweep — Abish caught)
**Resolved:** 2026-05-09 (M5.A)
**Surface:** `src/ToastRevival.Dashboard/src/pages/Compose.tsx`, `src/ToastRevival.Api/Controllers/TemplatesController.cs`
**Resolution:** `GET /api/templates` endpoint added (TemplatesController). 6 default templates seeded on tenant registration (AuthController.Register). Compose.tsx fetches templates on mount, builds slug→Guid map, includes `templateId` in `buildRequest()` when a template has been applied. Graceful degradation: if fetch fails, templateId stays undefined. `TemplateDbRecord` interface added to notifications.ts. `templateId` added to `SendNotificationRequest` interface.

### INFO-M4-002 — **RESOLVED 2026-05-09 (M5.A)**

**Filed:** 2026-05-08 (M4 Code Sweep — Abish)
**Resolved:** 2026-05-09 (M5.A)
**Surface:** `src/ToastRevival.Api/Controllers/DeviceGroupsController.cs` (new)
**Resolution:** `DeviceGroupsController` added with 6 endpoints: GET list, POST create, DELETE group, GET members, POST add member, DELETE remove member. DeviceCount maintained manually (increment on add, decrement with floor guard on remove). Admin-only for write operations, all-authenticated for reads.

### INFO-M4-003 — **RESOLVED 2026-05-09 (M5.A)**

**Filed:** 2026-05-08 (M4 Code Sweep — Abish)
**Resolved:** 2026-05-09 (M5.A)
**Surface:** `src/ToastRevival.Api/Controllers/NotificationsController.cs::History`
**Resolution:** `page` (default 1) and `pageSize` (default 25, clamped 1–100) query params added. `Skip/Take` applied server-side. Frontend's existing `notificationsApi.list(page, pageSize)` call now honored correctly.

### INFO-M5-001 — **RESOLVED 2026-05-09 (M6)**

**Resolved:** 2026-05-09 (M6). Template seeding in `AuthController.Register` now wrapped in try/catch with explicit `RollbackAsync` + clean 500 response: "Registration succeeded but template initialization failed. Contact support."

---

### INFO-M5-001 (M6 — hardening) — No explicit error handling on template seeding in AuthController.Register

**Filed:** 2026-05-09 (M5.A Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Controllers/AuthController.cs::Register`
**Issue:** Template seeding (`TemplatesController.BuildDefaultTemplates` → `SaveChangesAsync`) runs inside the existing registration transaction but without an explicit try/catch + RollbackAsync. If seeding throws, the EF exception propagates, the transaction is not committed, and the DB rolls back implicitly — correct behavior. But the caller receives a 500 response instead of a clean error message.
**Fix:** Wrap template seeding in try/catch at M6; on failure, roll back and return a clean 500 with a meaningful message: "Registration succeeded but template initialization failed. Contact support."
**Blocking:** No. Template model has no constraints that would cause legitimate failures under normal operation.

### INFO-M5-002 (acceptable) — UsersController.Invite has no role ceiling

**Filed:** 2026-05-09 (M5.A Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Controllers/UsersController.cs::Invite`
**Issue:** An Admin can invite a SuperAdmin. No role ceiling enforcement.
**Fix:** Acceptable for MSP context (admins are trusted operators). If compliance requires it, add: `if (req.Role > callerRole) return Forbid()` at M6.
**Blocking:** No.

### INFO-M5B-001 (performance, future) — AnalyticsController.Summary materializes statuses in memory

**Filed:** 2026-05-09 (M5.B Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Controllers/AnalyticsController.cs::Summary`
**Issue:** `_db.NotificationDeliveries.Where(d => d.CreatedAt >= since).Select(d => d.Status).ToListAsync()` brings all status values into memory for the period, then counts in C#. For MVP scale (thousands of records per MSP tenant), this is acceptable. For high-volume tenants (millions of deliveries), a server-side `GROUP BY Status COUNT(*)` would be significantly faster.
**Fix:** Replace with `GroupBy(d => d.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync()` then materialize to dict. EF Core 8 translates this to a server-side GROUP BY.
**Blocking:** No.

### INFO-M5B-002 (acceptable) — UpdateSettings silently ignores invalid DefaultScenario

**Filed:** 2026-05-09 (M5.B Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Controllers/TenantController.cs::UpdateSettings`
**Issue:** If the client sends `{"defaultScenario": "INVALID"}`, `Enum.TryParse` returns false, the field is not updated, and a 204 is returned with no indication that the value was rejected.
**Fix:** Return `BadRequest("Invalid defaultScenario value.")` when `req.DefaultScenario != null && Enum.TryParse fails`. M6+.
**Blocking:** No. Frontend dropdown is constrained to valid values.

### INFO-M5B-003 (acceptable) — PrimaryColor stored without hex-format validation

**Filed:** 2026-05-09 (M5.B Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Controllers/TenantController.cs::UpdateSettings`
**Issue:** `PrimaryColor` is stored as-is. A malicious admin could store arbitrary text. Downstream rendering uses the value only in a color picker input (not injected as CSS), so no XSS vector. But the data is untrusted.
**Fix:** Add regex validation (`^#[0-9A-Fa-f]{6}$`) at M6+.
**Blocking:** No.

### INFO-M5C-001 (M9 — deploy doc) — Uploaded assets are publicly accessible by URL
**Filed:** 2026-05-09 (M5.C Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Program.cs` — `app.UseStaticFiles()`
**Issue:** Files in `wwwroot/assets/` are served without authentication. Any client that knows a valid asset URL can fetch the image. This is intentional — the Windows agent must fetch hero/logo images from toast payloads without a user JWT.
**Fix:** Document in M9 deployment notes. If privacy of notification images is ever required, move to a signed-URL pattern (Azure Blob SAS, S3 presigned). Not a concern for MSP-managed endpoint images.
**Blocking:** No.

### INFO-M5C-002 — **RESOLVED 2026-05-09 (M6)**

**Resolved:** 2026-05-09 (M6). `IX_Notifications_Status_ScheduledAt` partial index added in M6Billing migration.

---

### INFO-M5C-002 (M6+) — No index on (Status, ScheduledAt) for scheduler sweep
**Filed:** 2026-05-09 (M5.C Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Services/NotificationQueueService.cs::EnqueueDueScheduledAsync`
**Issue:** The scheduler sweep queries `Notifications WHERE Status=Queued AND ScheduledAt<=now` across all tenants. No composite index. At MVP scale acceptable; at production scale (millions of rows) will become a sequential scan.
**Fix:** Add `HasIndex(n => new { n.Status, n.ScheduledAt }).HasFilter("scheduled_at IS NOT NULL")` in AppDbContext and generate a migration at M6+.
**Blocking:** No.

### INFO-M5C-003 (acceptable) — Drop zone MIME type accepts image/* in addition to extension whitelist
**Filed:** 2026-05-09 (M5.C Code Sweep — Abish)
**Surface:** `src/ToastRevival.Dashboard/src/pages/Assets.tsx` — file input `accept` attribute and drop handler
**Issue:** The frontend accepts any `image/*` MIME type in addition to the explicit extension list. The backend validates extension strictly (`.jpg/.jpeg/.png/.gif/.webp`). Frontend MIME check is UX-only — the backend is the real gate.
**Fix:** Acceptable. Backend extension whitelist is the authoritative check.
**Blocking:** No.

### INFO-M5D-001 — **RESOLVED 2026-05-09 (M6)**

**Resolved:** 2026-05-09 (M6). `CsvHelper` static class created in `Utilities/CsvHelper.cs`. `AuditController` and `NotificationsController` updated to use `CsvHelper.Cell()`. Private `CsvCell` methods removed from both controllers.

---

### INFO-M5D-001 (low) — CsvCell helper duplicated

**Filed:** 2026-05-09 (M5.D Code Sweep — Abish)
**Surface:** `Controllers/AuditController.cs`, `Controllers/NotificationsController.cs`
**Issue:** `CsvCell` private static helper implemented identically in both controllers.
**Fix:** Extract to `CsvHelper` static class in a `Utilities/` namespace at M6+.
**Blocking:** No.

### INFO-M5D-002 — **RESOLVED 2026-05-09 (M6)**

**Resolved:** 2026-05-09 (M6). `IX_AuditLogs_Timestamp` index added in M6Billing migration.

---

### INFO-M5D-002 (M6+) — No index on AuditLog.Timestamp

**Filed:** 2026-05-09 (M5.D Code Sweep — Abish)
**Surface:** `Data/AppDbContext.cs` — `AuditLog` entity model
**Issue:** `GET /api/audit/export?days=90` does a full-table scan on `AuditLogs`. At MVP scale (thousands of entries) acceptable; at production scale (millions of rows across many tenants) this becomes a concern.
**Fix:** Add `e.HasIndex(l => l.Timestamp)` in `OnModelCreating` + generate migration at M6+.
**Blocking:** No.

### INFO-M5D-003 — RESOLVED 2026-05-09 (SEC-004) — see entry above for resolution detail.

### INFO-M5D-004 (M9 scale) — PdfExportService.GeneratePdf() is synchronous

**Filed:** 2026-05-09 (M5.D Code Sweep — Abish)
**Surface:** `Services/PdfExportService.cs` — both `GenerateAuditLogPdf` and `GenerateDeliveryReportPdf`
**Issue:** QuestPDF's `.GeneratePdf()` is a synchronous call that blocks the ASP.NET request thread. For an MSP admin exporting a 90-day audit log of 10K+ entries, this could block for >500ms. Acceptable for infrequent admin export; would become a concern under concurrent export load.
**Fix:** Wrap in `await Task.Run(() => _pdf.GenerateXxxPdf(...))` at the controller call site at M9 scale.
**Blocking:** No.

### INFO-M5-003 (low) — TemplatesController.BuildDefaultTemplates couples Auth and Templates

**Filed:** 2026-05-09 (M5.A Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/Controllers/TemplatesController.cs::BuildDefaultTemplates`
**Issue:** `internal static` method on a controller is an unusual pattern. Creates implicit coupling between AuthController and TemplatesController.
**Fix:** Extract to a `TemplateSeederService` or `DefaultTemplates` static class at M6+.
**Blocking:** No. One caller only (AuthController.Register).

### INFO-M6-001 (M9 — deploy doc) — Stripe keys are placeholder values in appsettings.json

**Filed:** 2026-05-09 (M6 Code Sweep — Abish)
**Surface:** `src/ToastRevival.Api/appsettings.json`
**Issue:** `Stripe:SecretKey`, `Stripe:WebhookSecret`, `Stripe:PerDevicePriceId` are placeholder strings. Production must override via environment variables: `Stripe__SecretKey`, `Stripe__WebhookSecret`, `Stripe__PerDevicePriceId`. BillingController checks for placeholder prefixes and returns 503 instead of creating a bad checkout session.
**Fix:** Document in M9 DEPLOY.md alongside existing JWT key guidance.
**Blocking:** No. Test and production configs handled via env vars.

### INFO-M6-002 (M9 scale) — SyncConsumedCountAsync on every plan fetch

**Filed:** 2026-05-09 (M6 Code Sweep — Abish)
**Surface:** `Controllers/BillingController.cs::Plan`
**Issue:** `SyncConsumedCountAsync` executes one extra DB query per `GET /api/billing/plan` call. At MSP scale (infrequent admin page loads) acceptable.
**Fix:** Add short-TTL in-memory cache keyed by tenantId at M9.
**Blocking:** No.

### INFO-M6-003 (M9 scale) — Invoice list makes live Stripe API call per request

**Filed:** 2026-05-09 (M6 Code Sweep — Abish)
**Surface:** `Controllers/BillingController.cs::Invoices`
**Issue:** `InvoiceService.ListAsync` is a live Stripe API call on every request. No caching.
**Fix:** Cache with 5-minute TTL per tenantId at M9.
**Blocking:** No.

### INFO-0.4.8-002 — Multi-user (RDS/terminal-server) toast delivery not tested

**Filed:** 2026-05-12 (0.4.8 post-ship — Keith)
**Surface:** `src/ToastRevival.Agent/LegacyToastShim.cs`, agent scheduled-task configuration
**Issue:** 0.4.8 switches `Show()` to `Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(aumid).Show()`. This path was verified on a single-user Windows Server 2025 session (ScreenConnect console, COL-BU-001). Behavior in a multi-session RDS/terminal-server context is untested. Specific unknowns:
  1. Does `ToastNotificationManager.Show()` deliver the toast to the calling process's session desktop, or does it attempt a cross-session dispatch that Server drops?
  2. With multiple concurrent user sessions each running an agent instance (`Local\` mutex is session-scoped, so each session gets its own primary worker), do the `CreateToastNotifier` calls remain isolated per-session, or do they collide on the shared AUMID registration?
  3. The Start Menu shortcut with `System.AppUserModel.ID` is written per-user (HKCU at install time) — correct — but on an RDS host where the MSI is installed once for all users, does the shortcut ShellFolder install correctly into each user's `%APPDATA%\Microsoft\Windows\Start Menu`?
**Risk:** MSPs frequently deploy to Windows Server RDS hosts. A toast that silently delivers to the wrong session or drops entirely in multi-user context would be a regression from the expected behavior.
**Fix:** Test on a Windows Server box with two concurrent user sessions. Confirm each session receives only its own hub-pushed notifications and that `Show()` delivers to the correct session desktop. No code change expected — `Local\` mutex scoping + per-user HKCU should handle it — but needs evidence.
**Blocking:** No (single-user Server 2025 confirmed working). Block MSP go-live on RDS hosts until verified.

### INFO-M6-004 (closed 2026-05-09) — Onboarding.tsx welcome step uses emoji placeholder icons

**Filed:** 2026-05-09 (M6 Code Sweep — Abish)
**Surface:** `src/ToastRevival.Dashboard/src/pages/Onboarding.tsx`
**Issue:** Welcome step uses emoji (🔔, 📋, 📦, 🚀). Diana's standing preference: no emojis in UI. These are placeholder scaffolding — Diana will provide SVG replacements with the M7 onboarding design spec.
**Fix:** CLOSED 2026-05-09 by Codex. Onboarding uses SVG icon components and the single Standard billing step.
**Blocking:** No.

---

## Resolved

- **FIX-MSIX-004** (medium) - 2026-05-08, commit `6e3495c`. Packaged MSIX install did not fire toasts because `<com:ExeServer>` was missing `Arguments="----AppNotificationActivated:"`. Patched, signed, installed; visible toast verified on Win11 lab with button-click routing through `NotificationInvoked`. See entry above for full root-cause detail.

- **INFO-D5-001** (low) - 2026-05-09 (M2.A). Named mutex (`Local\Toast2IT.ToastNotification.PrimaryWorker`) gates primary worker mode. Activation + diagnostic modes short-circuit before mutex acquisition. See entry above.

- **INFO-MSIX-004-D** (low) - 2026-05-09 (M2.A). Activation-handler short-circuits before SignalR; routes button-click events to new REST `POST /api/notifications/{id}/interactions` endpoint. See entry above.

- **INFO-M2A-003** (M2.B) - 2026-05-09 (M2.B). Orphan `Sending` notification recovery sweep at queue-service startup. Marks stuck notifications Failed but leaves Pending deliveries Pending so catch-up can deliver. See entry above.

- **INFO-M2A-004** (M2.B) - 2026-05-09 (M2.B). Agent notificationId dedup via `MemoryCache` 1-hour sliding. Shared between hub-push and catch-up paths. See entry above.

- **FIX-M2B-001** (BLOCKING) - 2026-05-09 (M2.B Code Sweep, patched pre-commit). Agent `_lastCatchupSince` now nullable; first call omits `since` query param so server drains full Pending backlog. See entry above.
