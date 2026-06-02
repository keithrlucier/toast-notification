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

These are genuine Keith-decisions carried forward from the prior review — each is OPEN because it
needs Keith's product/architecture/deploy-topology call, not because work was skipped. Each has an
in-code anchor at the cited site so the automated scanner does not re-flag it.

| ID | Sev | Status | Owner | File / anchor | Decision Keith owns |
|----|-----|--------|-------|---------------|---------------------|
| BF-2 | High | OPEN | Keith | `Program.cs` login-per-ip | Trust `CF-Connecting-IP` only from a verified Cloudflare egress range / nginx overwrite / `KnownNetworks` — deploy-topology call. BF-1 account lockout mitigates per-account meanwhile. |
| XT-1 | High | OPEN | Keith | `DevicesController.Register` + `Setup.wxs` BootstrapEnrollReg | Replace the reusable per-tenant HKLM enrollment key with per-device single-use tokens. Architectural; HKLM ACL is USER-context-sensitive (agent reads it as the user). |
| SES-2-R | High | OPEN | Keith | `IsDeviceRevoked` | Instant revocation of live USER/operator sessions on tenant suspend (token-epoch / SecurityStamp pipeline). Device + send + hub paths already shipped; this is the session remainder. |
| MFA-7 | Medium | OPEN | Keith | `AuthController.MfaVerifySms` | SMS step-up must be a factor DISTINCT from the login factor — require enrolled TOTP for step-up, or add a separate SMS-elevation secret. Product/UX call. |
| BILL-ENF-1 | Medium | OPEN | Keith | `LicenseService.IsWithinCap` | May a PastDue tenant keep adding billable seats during grace, or deny? One-line fix either way once decided. Product policy. |
| API-1 | Info | OPEN | Keith | `ApiKeysController` | Implement key-auth properly (constant-time compare, tenant-scoped, must-not-bypass-MFA) OR remove the feature + its dashboard UI. Currently inert (no live bypass). |
| AGT-4-R | Low | OPEN | Keith | `LockScreenService` | End-to-end HMAC-sign the appearance config (versioned server+agent rollout). Image host-pin already shipped; this is the signing remainder. |
| DASH-L1 | Low | OPEN | Keith | `DeviceAppearanceCards.tsx` cache-bust | Lock-screen preview cache-bust is client-side, not a server version. Visible symptom already fixed; robust fix adds `LockScreenImageUpdatedAt` to `Tenant` (DB-schema call) or accept current over-fetch. |
| MOD-1 | Low | OPEN | Keith | `ContentSafetyService` catch blocks | Azure Content Safety scan paths fail open (return Pass) on an Azure exception — deliberate availability tradeoff, mitigated per-tenant by `ModerationRequireApprovalAll`. Decision: should an exception degrade to Review (not Pass) for moderation-enabled tenants? |

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
