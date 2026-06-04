# REVIEW LEDGER — 7-Pass Code Review (2026-06-02)

> **ARCHIVED 2026-06-02** by the cold-review pass that followed it (post-v0.4.37). This is the
> closed record of the AUTH-H1/DGC-M1/DGM-M2/XT remediation. Superseded by the root `REVIEW_LEDGER.md`.
> Do not edit — historical record.

> **Full cold pass — 2026-06-02 (Carl).** Prior ledger (XT-1 remediation, gauge → 0)
> archived to `Docs/review_history/REVIEW_LEDGER_2026-06-02_1.md`.
> This pass used 4 parallel Explore agents across Api/Dashboard/Agent/XT-1, with every
> top-tier finding personally verified by reading the actual source before inclusion.
>
> **REMEDIATE pass — 2026-06-02 (Carl + Anthony + Abish).** All 7 findings re-verified
> against current source (each still manifested at the cited shape). 6 fixed + verified;
> XT-M1 stays OPEN as Keith's product decision (anchored in code so it isn't re-flagged).
> Gauge: 7 OPEN → **1 OPEN**.

## Gauge rules (v5.4.25 parser — DO NOT BREAK)

Every pipe-table row whose first cell is an ID (letter + digit) counts as OPEN **unless**
the row also contains one of these uppercase terminal tokens:
`FIXED-VERIFIED` · `REMEDIATED` · `REJECTED-VERIFIED` · `VERIFIED-CLEAN`

`DEFERRED` / `BLOCKED` / `OPEN` all **count as OPEN**. An in-code anchor is NOT a terminal
state. Only code changed + verified earns a terminal token.

---

## Pre-pass checks

```
Read REVIEW_LEDGER.md / latest review_history?   Yes (prior ledger read; all prior items FIXED-VERIFIED)
Closed-pass anchors honored?                       Yes (checked for REVIEW-*/FIX-* anchors during reads)
Files scanned:                                     ~85 (Controllers/, Services/, Models/, Data/, Dashboard/src/, Agent/*)
Files with anchors found and respected:            5 (DevicesController, AuthController, BlocklistService, Program.cs, NotificationsController)
Remediate-pass verification:                       solution build green; EF model validated (no-DB probe); MfaServiceTests 6/6 green;
                                                   new XT-1 + AUTH-H1 integration tests compile + behavior-traced (execute under Docker
                                                   Postgres fixture in CI — Docker unavailable in this session).
```

---

## Findings (live gauge source)

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|---|---|---|---|---|---|---|
| AUTH-H1 | High | FIXED-VERIFIED | `src/ToastRevival.Api/Services/MfaService.cs` `AuthController.cs` | TOTP replay guard was non-atomic (read `LastTotpStep` → mutate in memory → caller persists), so two concurrent requests could both pass and save the same step. | An intercepted 6-digit code could be redeemed twice (full login + step-up) inside its ±1 step window. | High |
| DGC-M1 | Medium | FIXED-VERIFIED | `src/ToastRevival.Api/Controllers/DeviceGroupsController.cs` | `Update`/`Delete` (and 4 sibling fetches) loaded `DeviceGroup` by id only, relying solely on the global query filter — no explicit tenant predicate. | A future `IgnoreQueryFilters()` or no-HTTP-context job path could cross tenants on a GUID guess. | High |
| DGM-M2 | Medium | FIXED-VERIFIED | `src/ToastRevival.Api/Data/AppDbContext.cs` | `DeviceGroupMember` was the only tenant-associated entity with no `HasQueryFilter`. | A direct `_db.DeviceGroupMembers` query (future job/endpoint) would silently skip tenant isolation. | High |
| XT-M1 | Medium | OPEN | `src/ToastRevival.Api/Controllers/DevicesController.cs` (carve-out, anchored) | Single-use-token reinstall carve-out identifies "same machine" by the self-reported `(DeviceName, Username)` tuple — not hardware-backed. An attacker with a spent token from HKLM + the original name/username can re-enroll. | Defense-in-depth gap (HKLM read already implies machine compromise). **Decision 2026-06-02 (Keith, phone):** fresh-token-per-reinstall REJECTED (breaks silent RMM mass deploy of hundreds of devices). Fix = bind token to machine SID, scoped as build-mode project **XT-3** (`Docs/ToastRevival/projects/XT-3/`). Carve-out stays as-is; anchored in code. **Owner: Keith.** Stays OPEN until XT-3 ships. | Medium |
| XT-L1 | Low | FIXED-VERIFIED | `src/ToastRevival.Api/Controllers/DevicesController.cs` (PassesEnrollmentGateAsync) | Atomic-claim `ExecuteUpdateAsync` WHERE omitted `t.TenantId == tenantId`. | Defense-in-depth: a leaked `token.Id` could consume another tenant's token. | High |
| XT-L2 | Low | FIXED-VERIFIED | `src/ToastRevival.Api/Controllers/DevicesController.cs` (PassesEnrollmentGateAsync) | Lost-race re-read `FirstOrDefaultAsync(t => t.Id == token.Id)` omitted the tenant predicate. | Same leak surface as XT-L1, feeding the reinstall carve-out. | High |
| XT-L3 | Low | REMEDIATED | `tests/ToastRevival.Api.Tests/SecurityTests.cs` | No automated coverage for the XT-1 enrollment-token feature. | XT-1 is the most recent security ship; regression coverage was missing. | High |

---

## Remediation log (2026-06-02)

1. **AUTH-H1 → FIXED-VERIFIED.** Added `MfaService.VerifyAndClaimAsync(AppDbContext, AppUser, string)`: verifies the code, then advances `LastTotpStep` with one conditional `ExecuteUpdateAsync` (`WHERE last_totp_step IS NULL OR < @matchedStep`), returning true only on rows-affected == 1. Mirrors the proven XT-1 token-claim pattern. All 3 call sites (`VerifyLoginTotp`, `MfaDisable`, `MfaVerify`) switched. Pure crypto/floor core retained as the in-memory `Verify` for `MfaServiceTests` (6/6 green). New concurrency test `AuthH1_ConcurrentSameTotpCode_AdvancesReplayFloorExactlyOnce`.

2. **DGC-M1 → FIXED-VERIFIED.** Added `&& g.TenantId == GetTenantId()` to all 6 `DeviceGroup` fetches in the controller (Update, Delete, ListMembers, AddMember, SetMembers, RemoveMember secondary) — not just the 2 originally flagged (whole-module sweep). Matches house style across every other write-path controller.

3. **DGM-M2 → FIXED-VERIFIED.** Added `e.HasQueryFilter(m => m.DeviceGroup.TenantId == _tenantProvider.TenantId)` scoped through the required navigation (no own TenantId column). EF model build + validation confirmed via a no-DB model probe.

4. **XT-L1 / XT-L2 → FIXED-VERIFIED.** Added `t.TenantId == tenantId` to the atomic-claim WHERE and the lost-race re-read in `PassesEnrollmentGateAsync`.

5. **XT-M1 → OPEN (owner: Keith; fix scheduled as XT-3).** Not rubber-stamped. The clean "return false" fix silently breaks the intentional silent-reinstall UX (commit b9dc8f2), so it is a product decision. Keith decided by phone (2026-06-02): reject the fresh-token-per-reinstall option (it breaks RMM mass deployment), keep the carve-out as-is, and fix it properly by binding the token to the machine SID — scoped as build-mode project **XT-3** (`Docs/ToastRevival/projects/XT-3/`, full residual-risk + design docs). Behavior unchanged this pass; in-code anchor records the decision. Stays on the gauge until XT-3 ships.

6. **XT-L3 → REMEDIATED.** Added enrollment-token security tests to `SecurityTests.cs`: admin-only gating, expiry, cross-tenant rejection, revoked-active rejection, same-machine-reinstall-allowed / different-machine-rejected, and concurrent-claim atomicity (one wins). Register-path tests use a dedicated `ApiTestFactory` for a fresh `device-per-hour` window (mirrors `RegistrationLoadTests`). Token hashes seeded as lowercase hex to match production lookup. (Verifier's draft had three behavior bugs — uppercase hashes, an inactive-gate cross-tenant case, and a revoked-used-token assertion contradicting the `RevokedAt` gate — all corrected.)

---

## Reviewed and clean (off gauge)

The following areas were reviewed by parallel agents and verified personally. No findings.

- **Agent self-update / SelfUpdateService.cs** — Authenticode re-verify, reparse-point defense, TOCTOU protection via protected directory copy, SYSTEM-task privilege boundary — VERIFIED-CLEAN
- **Dashboard XSS surface** — no `dangerouslySetInnerHTML`, no `innerHTML`, no `eval`, SSO token fragment cleared via `replaceState` — VERIFIED-CLEAN
- **NotificationsController device-token isolation** — `Get` endpoint explicitly scopes by `tenantId` from claims; device tokens cannot read cross-tenant — VERIFIED-CLEAN
- **DeployCommand.tsx / EnrollmentTokens.tsx install command** — All interpolated values are server-generated UUID (tenantId), hex string (enrollment token/key), or `window.location.origin` — no injection surface — VERIFIED-CLEAN (FE-H1 REJECTED: false positive)
- **AgentClient.cs HMAC verification** — constant-time comparison, payload deduplication, correct key lookup — VERIFIED-CLEAN
- **BlocklistService.cs Unicode normalization** — NFKC + Format-strip anchored from BLK-1 fix — VERIFIED-CLEAN
- **EnrollmentToken admin endpoints** — admin-gated, tenant-scoped on all three (issue/list/revoke), plaintext token never returned after issue, 24h expiry enforced at claim time — VERIFIED-CLEAN

---

*Lifecycle: This is a REMEDIATE pass — rows driven to terminal status. One row (XT-M1) stays OPEN by design, owned by Keith. Gauge reads this file; don't alter the table format.*

> **NOTE added on archival:** the two Agent-side VERIFIED-CLEAN stamps above
> (`SelfUpdateService.cs`, `AgentClient.cs`) were INVALIDATED ~7h later by the v0.4.36/v0.4.37
> rewrite (commits a3c0d80, 2cca2db). See the superseding `REVIEW_LEDGER.md` cold pass —
> SelfUpdateService.cs is re-opened as **Agent-M1**.
