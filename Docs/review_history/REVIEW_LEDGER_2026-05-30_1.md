# REVIEW LEDGER — Cold Code Review (2026-05-29)

**Pass date:** 2026-05-29
**Reviewer:** Carl (cold reviewer — parallel Explore agents dispatched for Backend API, Frontend, and Infrastructure/Agent surfaces)
**Scope:** Net-new code since prior ledger closed clean at `a6e6e9a` (0.4.18, 2026-05-28). Diff covers 0.4.19 → HEAD (6b041f5) — Platform Admin tenant lifecycle (M13, 0.4.21/0.4.24), Microsoft Entra SSO (M14), installer/credential hygiene, prerender legal pages. 45 files changed, +5,885/−208.
**Prior ledger archived to:** `docs/review_history/REVIEW_LEDGER_2026-05-28_2.md`

Read REVIEW_LEDGER.md / latest review_history? Yes
Closed-pass anchors honored? Yes — INFO-01 REJECTED-by-design anchor on localStorage at `AuthContext.tsx:86` honored. Prior DevicesController IDOR fixes (May 26 pass, 7 IDOR) confirmed in-place and not re-flagged. All 3 findings from the 2026-05-28 pass are FIXED-VERIFIED.
Files scanned: 45 (full diff since a6e6e9a + key prior-pass surfaces spot-checked)
Files with anchors found and respected: 1 (AuthContext.tsx:86 INFO-01 localStorage)

---

## Summary

| Severity | Count | Open | Fixed | Rejected |
|----------|-------|------|-------|----------|
| Critical | 0     | 0    | 0     | 0        |
| High     | 0     | 0    | 0     | 0        |
| Medium   | 1     | 0    | 1     | 0        |
| Low      | 2     | 0    | 1     | 1        |
| ANCHOR-CHALLENGE | 0 | 0 | 0 | 0 |
| **Total**| **3** | **0** | **2** | **1** |

The Platform Admin and SSO surfaces are architecturally sound. The PlatformAdmin policy is enforced at the class level on SystemController — the frontend client-side checks are pure UX. The SSO state/nonce/CSRF handling is correct (DataProtection sealed cookie, fixed-time compare, single-use, 10-min expiry). The prior IDOR fixes hold. One real issue: the agent sends bearer tokens over whatever scheme the MSP installs — no HTTPS gate anywhere in the agent.

---

## Critical

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|

None found.

---

## High

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|

None found.

---

## Medium

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|
| Agent-M1 | Medium | FIXED-VERIFIED | `src/ToastRevival.Agent/Program.cs` | No HTTPS scheme validation on `config.ServerUrl`. Fixed: `#if !DEBUG` HTTPS guards added at three points in `Program.cs`: `SetupMode.RunAsync` (refuses to write `bootstrap.json` with HTTP URL), `TryFirstRunRegistrationAsync` (blocks registration), and `PrimaryMode.RunAsync` (blocks already-registered configs). DiagLog entries on rejection. Allows `localhost` HTTP in DEBUG builds. TS builds clean; C# Release build zero warnings. | Verified by Abish Code Sweep + `dotnet build --configuration Release` zero warnings. | High |

---

## Low

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|
| Frontend-L1 | Low | FIXED-VERIFIED | `src/ToastRevival.Dashboard/src/pages/PlatformTenantDetail.tsx` and `src/ToastRevival.Dashboard/src/pages/PlatformUsers.tsx` | `acting` promoted from `string \| null` to `ReadonlySet<string>` in both components. Each row's action button tracks its own in-flight key independently via functional `Set` updates. `runAction` uses `setActing(prev => new Set(prev).add(key))` on entry and `.delete(key)` in `finally`. All `acting === 'key'` checks changed to `acting.has('key')`. `npx tsc --noEmit` exits 0. | Verified by Abish Code Sweep + TypeScript zero errors. | High |
| Frontend-L2 | Low | REJECTED-VERIFIED | `src/ToastRevival.Dashboard/src/pages/Billing.tsx` | Spoofable `?session=success` banner removed entirely. `useSearchParams` import, hook usage, `successSession` const, and the JSX success-banner block all dropped. Stripe's hosted checkout page provides payment confirmation before redirecting back; billing page fetches fresh server-side billing status on mount. No anchor needed — code removed, not annotated. `npx tsc --noEmit` exits 0. | Verified by Abish Code Sweep + TypeScript zero errors. Design rationale confirmed by Carl: Stripe's own success UX is sufficient; removing the URL-spoofable banner is the correct fix. | High |

---

## ANCHOR-CHALLENGE

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|

None found.

---

## Top fixes (in order)

1. **Agent-M1** — Add an HTTPS enforcement guard to `DeviceConfig` or `BootstrapConfig`. At the earliest point where `ServerUrl` is consumed (registration path is the first use), assert `serverUrl.StartsWith("https://")` and refuse to proceed if not. Emit a clear `DiagLog` entry and return null from `RegisterAsync` so the tray icon shows a configuration error rather than silently operating over HTTP. A `#if !DEBUG` guard is appropriate to allow localhost HTTP in dev builds.

2. **Frontend-L1** — Elevate `acting` to a `Set<string>` (or a `Map<string, true>`) so each row tracks its own in-flight state independently. The set approach: add the key before the call, delete it in `finally`. No mutations fire twice (each API call is guarded on entry by checking `acting.has(key)`), and every row's button reflects its own state.

3. **Frontend-L2** — Either drop the success-session UI entirely (Stripe's hosted page already has a success UX) or verify it via a server-side `GET /api/billing/session?id={checkoutSessionId}` round-trip after Stripe redirects back. The current Stripe checkout `successUrl` pattern can include `{CHECKOUT_SESSION_ID}` which the server can then verify.

---

## Notes on what was reviewed and NOT flagged

- **DevicesController IDOR (×4 — sub-agent claims rejected):** `Get()` (line 155) uses `_db.Devices` with global EF query filter active — TenantId isolation is automatic. `Ping()` (line 257) uses `IgnoreQueryFilters()` but the `deviceId` claim comes from the server-issued, signed device JWT — an attacker cannot forge a different device's id. `GetTenantName()` and `GetAppearanceConfig()` read `tenantId` from the signed device JWT — same logic applies. No IDOR.
- **SystemController platform-admin gate:** Class-level `[Authorize(Policy = "PlatformAdmin")]` at `SystemController.cs:16` enforces the gate server-side. The frontend's `isPlatformAdmin` checks are UX only. Correct architecture.
- **SsoController state/nonce/CSRF:** DataProtection sealed cookie (tamper-proof, time-limited to 10 min), SameSite=Lax, HttpOnly, Secure, `Path="/api/auth/sso/microsoft"`. State echoed by Microsoft compared with FixedTimeEquals. Cookie deleted single-use regardless of outcome. Nonce passed to `ExchangeCodeAsync` for id_token validation. Correct implementation.
- **SsoController `FrontendBase()` redirect:** Reads `App:BaseUrl` config with hardcoded fallback `"https://toastnotification.com"`. Config-controlled base URLs are not user-controlled; changing this requires server access. Not an open redirect.
- **SsoCallback.tsx — state param:** State validation is server-side (cookie comparison before JWT issuance). The SPA callback page reads only the pre-validated JWT from the URL fragment. No client-side re-validation is needed or expected.
- **AuthContext.tsx localStorage:** Anchored at line 86 as `REVIEW-2026-05-25 INFO-01 REJECTED-by-design` — not re-flagged.
- **Billing.tsx Stripe redirects (lines 131-132, 143-144):** `window.location.href = url` where `url` is a Stripe-hosted checkout or portal URL returned by the server. Standard Stripe integration pattern; URLs originate from the Stripe API, not user input.
- **AppUser.TenantId index:** `HasIndex("TenantId")` declared in `M3SecurityHardening` migration designer (line 235). Index exists. Sub-agent claim that it was missing was a false positive.
- **M13 migration idempotency:** EF Core migrations are single-run by design — `__EFMigrationsHistory` prevents re-execution. `AddColumn` calls do not need runtime idempotency guards.
- **`setup-git-credentials.ps1` SecureString:** Ops-L1 from the prior pass was fixed. Parameters are `[SecureString]`; unwrap happens only at point of use via BSTR/ZeroFreeBSTR. Confirmed.
- **Prerender /legal/* routes:** Both `/legal/privacy` and `/legal/terms` have substantive HTML content in `prerender-seo.mjs` (10+ sections each). Store cert failure pattern from 2026-05-29 does not recur here.
- **SSO tenant gate:** `SsoController.Callback()` gates on `t.SsoEnabled && t.AzureAdTenantId == identity.TenantId` — a valid Microsoft token proves identity but not authorization. Correct.
- **SsoController OID binding:** First SSO sign-in matches on `NormalizedEmail` within the authenticated tenant scope, then binds the Entra OID for future sign-ins. Race guard (`oidTaken` check) prevents dual-binding. Correct.
- **Platform admin pages:** `PlatformTenants`, `PlatformTenantDetail`, `PlatformUsers` all pass through `SystemController` which carries the class-level PlatformAdmin policy. Frontend routing guard is defense-in-depth only.
- **Banned codenames:** No "ToastRevival" in user-visible strings on new pages or new API responses in this scope. SSO, platform admin, and prerender content use "Toast Notification" / "Toast" only.
