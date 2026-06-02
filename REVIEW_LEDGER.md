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
| XT-1 | High | OPEN | Keith | `DevicesController.Register` + `Setup.wxs` | Per-device single-use enrollment tokens (replacing the reusable HKLM key). Scoped: a non-breaking SERVER-FIRST phase (EnrollmentToken model + issue/list/revoke endpoints + Register accepting single-use tokens; the legacy key keeps working) then an MSI phase (agent + WiX read the token) needing an MSI bump + your code-signing token + reinstall push. |
| MFA-7 | Medium | OPEN | Keith | `AuthController.MfaVerifySms` | REVIEW (you're holding this): confirmed the TOTP step-up IS already a distinct enrolled-secret factor. Remaining — a TOTP-enrolled user can still downgrade to the weaker SMS step-up, and removing SMS outright would lock out SMS-only / SSO / legacy users until they enroll TOTP. Approve the safe hardening (block SMS step-up when TOTP is enrolled) + the user-migration plan before ClickSend SMS is retired. |
| AGT-4-R | Low | OPEN | Keith | appearance config / `LockScreenService` | End-to-end HMAC-sign the appearance config (mirrors the existing toast HMAC). Scoped as a 3-phase versioned rollout (server signs → agent verifies → enforce) needing a signed MSI to reach the fleet; the image host-pin already shipped. |

---

## Closed this session — post-call execution (2026-06-01)

All green-lit on the call, built + verified + shipped to private `main` + the public mirror. Builds: API + Agent + dashboard all 0/0.

- **BF-2** — FIXED-VERIFIED (v0.5.22) — `CloudflareIpValidator` + rate-limiter/`ClientIp` trust `CF-Connecting-IP` only from a verified Cloudflare egress peer (or loopback proxy). Direct-to-origin header spoof can no longer reset the rate-limit bucket. (Ops follow-up: lock the origin firewall to Cloudflare ranges in prod.)
- **MOD-1** — FIXED-VERIFIED (v0.5.22) — `ContentSafetyService` now fails CLOSED (Block) on an Azure scan exception; a moderation failure STOPS the send instead of passing unmoderated content.
- **DASH-L1** — FIXED-VERIFIED (v0.5.22) — `Tenant.LockScreenImageUpdatedAt` (M16 migration) is the server-provided `?v=` cache-bust; a replaced lock-screen image re-fetches for every viewer/agent.
- **API-1** — FIXED-VERIFIED (v0.5.22) — the inert per-tenant API-key UI was removed (page/route/nav + controller/DTOs). DbSet/model/table retained (no EF drift, no data loss).
- **BILL-ENF-1** — REMEDIATED (v0.5.23) — moot: billing is DISABLED platform-wide via `Billing:Enabled` (default off). No billable seats exist, so the PastDue-grace question does not arise until billing is re-enabled. Keith's scope call on the call.
- **SES-2-R** — FIXED-VERIFIED (v0.5.24) — token-epoch session revocation: user tokens carry `tokenEpoch` (= Identity SecurityStamp); the `OnTokenValidated` hook rejects a token when the tenant is suspended or the stamp rotated (30s cache, fail-open on a DB blip, legacy-token-safe). SecurityStamp rotates on password reset (Identity) and role change. Tenant suspend now kills live operator sessions, not just device/send/hub paths.
- **Store privacy (10.5.1)** — FIXED-VERIFIED (v0.5.22) — `/privacy-policy` route alias + prerender + sitemap so the Store-listing URL renders the policy (was a blank shell). **Action for Keith: resubmit the app in Partner Center.**

---

## Closed this session — 2026-06-01 audit pass (off gauge)

Carl ran a targeted audit of the surfaces the prior review left uncovered (dashboard, the
never-logic-reviewed controllers, content moderation, output/injection services, agent+installer).
Method: 3 parallel refute-first finder agents, every claim `git grep`-anchored, each survivor
re-verified personally. Builds: `ToastRevival.Api` + `ToastRevival.Agent` → 0 warnings / 0 errors.

- **BLK-1** — FIXED-VERIFIED — `BlocklistService.cs:31-39`. Tenant custom-blocklist matching did raw `ToLowerInvariant()` + `Contains()` with no Unicode normalization, so a sender could split a banned term with a zero-width char (`b​adword`) or use full-width/ligature look-alikes to evade it. Fix: added `NormalizeForMatch` (NFKC + strip Unicode `Format`-category code points) on both the message and each term. Anchored. Residual: cross-script homoglyphs (Cyrillic 'а' vs Latin 'a') not folded — needs a confusables map; Azure CS remains the severity gate.
- **AGT-LOG-1** — FIXED-VERIFIED — `Program.cs:55,301`. The agent wrote its full command line (including the optional `[enrollmentKey]` the MSI passes to `--setup-bootstrap`) to `agent.log` — a secret that an RMM/support log bundle could carry off-box. Fix: `DiagLog.RedactArgs` masks the key at both log sites. Anchored. **Ships to the fleet on the next signed MSI build + `Agent:LatestVersion` bump** (a source fix alone does not reach installed agents).

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
