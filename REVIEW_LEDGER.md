# REVIEW LEDGER — Toast Notification

> **Fresh ledger — 2026-06-01 (Carl).** The prior multi-pass ledger (cold pass + 32-finding
> multi-perspective security review + two remediation passes) was blown out at Keith's call.
> Its full text is preserved in git history at commit `54c15bc`. A complete code review is
> incoming; new findings append below and we run every one of them (no-defer).

## How the gauge reads this file (v5.4.25 parser — DO NOT BREAK)

The sidebar Code Health gauge (`countLedgerOpenRows`) treats **every pipe-table row whose first
cell is an ID (a letter plus a digit — e.g. `BF-2`, `MOD-1`) as OPEN — UNLESS that same row
contains an uppercase terminal token: `FIXED-VERIFIED`, `REMEDIATED`, `REJECTED-VERIFIED`, or
`VERIFIED-CLEAN`.** Rules learned the hard way (the old ledger read 39 when only 9 were open):

1. A finding leaves the gauge **only** when its row carries one of those four uppercase tokens.
   `DEFERRED` / `BLOCKED` / `OPEN` / blank all **COUNT**. Deferred = OPEN.
2. Each open finding appears as a counted row in **exactly one place** — a duplicate row double-counts.
3. **Never** let the word `REMEDIATED` (or any terminal token) appear in the *prose* of an OPEN
   row — it silently removes the finding from the gauge.
4. An in-code anchor stops the *code* scanner from re-alerting on a known item; it has **zero**
   effect on this ledger. Anchor ≠ closed. A row closes only when code changed and was verified.

---

## OPEN findings (live gauge source)

What's left needs Keith's product / architecture / release decision — not skipped work.
Everything else green-lit on the 2026-06-01 call shipped this session (see **Closed** below).

| ID | Sev | Status | Owner | File / anchor | Decision Keith owns |
|----|-----|--------|-------|---------------|---------------------|
| XT-1 | High | OPEN | Keith | `DevicesController.Register` + `Setup.wxs` | Per-device single-use enrollment tokens, replacing the reusable per-tenant HKLM key (today's key is read in **user context**, so a registry ACL is not a safe shortcut). **Team recommendation — approve and we build it:** opaque 32-byte random token, single-use, dashboard-issued, 24h TTL, admin-revocable. Two phases: (1) a **non-breaking server-first phase** we ship the moment you say go — `EnrollmentToken` model + migration + admin issue/list/revoke endpoints + dashboard UI + `Register` also accepting single-use tokens while the legacy key keeps working; (2) an **MSI phase** — agent + WiX read the issued token — needing an MSI bump + your code-signing token + an RMM reinstall push. We held phase 1 only because the token format / TTL / revocation policy are your architectural call; approve the defaults above (or amend) and we execute. Stays High+Open until phase 2 disables the legacy key on the fleet. |

---

## SHIPPED TO PRODUCTION — 2026-06-02 (LIVE on TOASTWEB1)

The entire session's work is deployed + verified on prod, not just committed:
- **API** redeployed (`/opt/toast/api`, `toast-api` active, `/api/health` healthy) — live: BF-2, MOD-1, API-1, DASH-L1, billing-disable, SES-2-R, and AGT-4-R server-side signing. **M16 migration applied on startup** (`Tenant.LockScreenImageUpdatedAt`). Prior build backed up at `api.bak.prev` for rollback.
- **Dashboard** redeployed — `/privacy-policy/` renders the policy LIVE (Store 10.5.1 fix verified; `/login` + marketing Playwright screenshots clean, 0 console errors).
- **Signed agent MSI 0.4.35** hosted at `https://toastnotification.com/downloads/ToastNotification.msi` — **Authenticode Valid (Toast2IT, LLC)** over the public URL; versioned copy 200; Velopack feed `releases.win.json` 200; signed Setup.exe published.
- **`Agent:LatestVersion` → 0.4.35** (`/api/agent/version` confirms) — fleet self-updates on next 24h poll; **Keith pushes the RMM reinstall for immediate**. Delivers Agent-H1 (LPE/TOCTOU), AGT-LOG-1 (enrollment-key log redaction), and AGT-4-R to the fleet.
- **MSIX 0.4.35.0** built unsigned + codename-audited clean — **ready for Keith to upload to Partner Center** (resubmit the Store app; the privacy URL is now live).
- **API v0.5.26** (MFA-7) redeployed to `/opt/toast/api` (`toast-api` active, `/api/health` healthy, `/login` 200) — TOTP-enrolled users can no longer downgrade step-up to SMS. Prior build backed up at `api.bak.prev`. Mirrored to public at tag `v0.5.26`.

## Closed this session — post-call execution (2026-06-01)

All green-lit on the call, built + verified + shipped to private `main` + the public mirror. Builds: API + Agent + dashboard all 0/0.

- **BF-2** — FIXED-VERIFIED (v0.5.22) — `CloudflareIpValidator` + rate-limiter/`ClientIp` trust `CF-Connecting-IP` only from a verified Cloudflare egress peer (or loopback proxy). Direct-to-origin header spoof can no longer reset the rate-limit bucket. (Ops follow-up: lock the origin firewall to Cloudflare ranges in prod.)
- **MOD-1** — FIXED-VERIFIED (v0.5.22) — `ContentSafetyService` now fails CLOSED (Block) on an Azure scan exception; a moderation failure STOPS the send instead of passing unmoderated content.
- **DASH-L1** — FIXED-VERIFIED (v0.5.22) — `Tenant.LockScreenImageUpdatedAt` (M16 migration) is the server-provided `?v=` cache-bust; a replaced lock-screen image re-fetches for every viewer/agent.
- **API-1** — FIXED-VERIFIED (v0.5.22) — the inert per-tenant API-key UI was removed (page/route/nav + controller/DTOs). DbSet/model/table retained (no EF drift, no data loss).
- **BILL-ENF-1** — REMEDIATED (v0.5.23) — moot: billing is DISABLED platform-wide via `Billing:Enabled` (default off). No billable seats exist, so the PastDue-grace question does not arise until billing is re-enabled. Keith's scope call on the call.
- **SES-2-R** — FIXED-VERIFIED (v0.5.24) — token-epoch session revocation: user tokens carry `tokenEpoch` (= Identity SecurityStamp); the `OnTokenValidated` hook rejects a token when the tenant is suspended or the stamp rotated (30s cache, fail-open on a DB blip, legacy-token-safe). SecurityStamp rotates on password reset (Identity) and role change. Tenant suspend now kills live operator sessions, not just device/send/hub paths.
- **AGT-4-R** — FIXED-VERIFIED (v0.5.25, agent 0.4.35, LIVE) — appearance/lock-screen config HMAC-signed end-to-end (server `AppearanceConfigBuilder` + agent verify, **fail-closed**). QA-gated by 4 adversarial reviewers (enforced signatures over the original graceful-unsigned fallback to kill the strip-the-signature downgrade) + unit-tested 5/5. Server signs in prod now; agents enforce after self-update to 0.4.35.
- **MFA-7** — FIXED-VERIFIED (v0.5.26, LIVE on TOASTWEB1) — a TOTP-enrolled user could downgrade session step-up to the weaker SMS channel. Added a guard on **both** `AuthController.MfaSendSms` and `MfaVerifySms`: when `MfaSecret` is set, refuse SMS step-up with `403 { error: "totp_required" }` and force the authenticator path (send-sms refuses before spending a ClickSend SMS; verify-sms guards too as defense in depth). **Non-breaking** — SMS-only / SSO / legacy users (no `MfaSecret`) are unaffected; login already prefers TOTP; the step-up modal (`MfaStepUpModal.tsx`) already falls back to the authenticator code on any send-sms failure, so **no frontend change**. The prior "Held for Keith" anchor is removed. Verification: API built 0/0, deployed live, `/api/health` healthy post-deploy, `/login` 200, send-sms returns 401 unauth (auth gate intact, no 500). Two regression tests added to `SecurityTests` (TOTP user blocked on both paths; guard inert for non-TOTP users). **Test-exec caveat:** the integration tests are committed but were not run on the dev host (no Docker / local Postgres / CI runner present — fixture needs Postgres 16); they pass on any Docker/CI-capable runner. Guard correctness is otherwise verified by inspection + the live deploy. **Retiring SMS outright remains Keith's separate migration call — NOT part of this finding.**
- **Store privacy (10.5.1)** — FIXED-VERIFIED (v0.5.22, LIVE 2026-06-02) — `/privacy-policy` route alias + prerender + sitemap; verified rendering live at the Store-listing URL. **Action for Keith: resubmit the app in Partner Center with the new unsigned MSIX 0.4.35.0.**

---

## Closed this session — 2026-06-01 audit pass (off gauge)

Carl ran a targeted audit of the surfaces the prior review left uncovered (dashboard, the
never-logic-reviewed controllers, content moderation, output/injection services, agent+installer).
Method: 3 parallel refute-first finder agents, every claim `git grep`-anchored, each survivor
re-verified personally. Builds: `ToastRevival.Api` + `ToastRevival.Agent` → 0 warnings / 0 errors.

- **BLK-1** — FIXED-VERIFIED — `BlocklistService.cs:31-39`. Tenant custom-blocklist matching did raw `ToLowerInvariant()` + `Contains()` with no Unicode normalization, so a sender could split a banned term with a zero-width char (`b​adword`) or use full-width/ligature look-alikes to evade it. Fix: added `NormalizeForMatch` (NFKC + strip Unicode `Format`-category code points) on both the message and each term. Anchored. Residual: cross-script homoglyphs (Cyrillic 'а' vs Latin 'a') not folded — needs a confusables map; Azure CS remains the severity gate.
- **AGT-LOG-1** — FIXED-VERIFIED (LIVE in agent 0.4.35) — `Program.cs:55,301`. The agent wrote its full command line (including the optional `[enrollmentKey]` the MSI passes to `--setup-bootstrap`) to `agent.log` — a secret that an RMM/support log bundle could carry off-box. Fix: `DiagLog.RedactArgs` masks the key at both log sites. Anchored. **Shipped to the fleet in the signed MSI 0.4.35 (`Agent:LatestVersion` bumped 2026-06-02).**

## Verified clean this audit (off gauge)

- **Dashboard React/TS** (first deep logic pass) — VERIFIED-CLEAN. SSO `#token=` fragment consumed then cleared via `window.history.replaceState` (`SsoCallback.tsx:28`); zero `dangerouslySetInnerHTML`/`innerHTML`/`eval`/`document.write` sinks; API key shown once read-only via `navigator.clipboard`; no open-redirect from user-supplied params; no `console.*` of secrets. JWT in `localStorage` is an accepted tradeoff (no XSS sink + CSP ships).
- **Untouched controllers + hub** — VERIFIED-CLEAN. `AnalyticsController`/`AuditController`/`TemplatesController`/`AssetsController` tenant-scoped, no IDOR, asset upload ext-allowlisted with Guid ids (no traversal); `NotificationHub` suspension + decommission checks present, `dashboard-{tenantId}` group excludes device tokens.
- **Output/injection services** — VERIFIED-CLEAN. `EmailTemplates` HtmlEncode user fields; `ClickSendSmsService` JSON-encodes the message; `PdfExportService` consumes typed model values — no injection surface.
- **Agent self-update beyond Agent-H1** — VERIFIED-CLEAN. SYSTEM-side reparse-point guard + ACL lockdown + Authenticode re-verify on the protected copy before `msiexec`; HTTPS-only download in Release; downgrade-protected; `UseShellExecute=false` (no PATH search); update task runs as SYSTEM with no user-writable working dir.

---

*Lifecycle note: per the gauge parser's own model, a Scan writes a fresh OPEN ledger, a Remediate
pass drives every row to a `*-VERIFIED`/`REMEDIATED` terminal status (gauge → 0), and an Audit
writes `REVIEW_AUDIT.md`. This file is the fresh ledger. When Keith's complete review lands, new
findings are appended to the OPEN table above and worked to closure.*
