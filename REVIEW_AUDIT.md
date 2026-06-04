# Code Review Audit — 2026-06-03

Source ledger: REVIEW_LEDGER.md (commit 9532252)
Total rows audited: 7
Pass / Reopen / Incomplete / Drift: 7 / 0 / 0 / 0

---

## AUTH-H1 — TOTP replay guard non-atomic
Severity: High
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `src/ToastRevival.Api/Services/MfaService.cs:105-120` — `VerifyAndClaimAsync` implemented exactly as described: `TryMatch` returns `matchedStep` in-memory, then one `ExecuteUpdateAsync` WHERE `LastTotpStep IS NULL OR LastTotpStep < matchedStep`, returns `true` only when `claimed == 1`. All three auth call sites switched (`AuthController.cs:590, 843, 876`). The remaining `VerifySecret` call (`AuthController.cs:798`) is for new-secret enrollment confirmation — not a claim path, not a missed site. Concurrency test `AuthH1_ConcurrentSameTotpCode_AdvancesReplayFloorExactlyOnce` confirmed present at `SecurityTests.cs:789`.
Recommended action: none

## DGC-M1 — DeviceGroupsController fetches tenant-unsafe
Severity: Medium
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `src/ToastRevival.Api/Controllers/DeviceGroupsController.cs:71,94,106,124,152,208` — all six DeviceGroup reads (`FirstOrDefaultAsync` × 5, `AnyAsync` × 1) carry `&& g.TenantId == GetTenantId()`. Blast-radius scan of `_db.DeviceGroups.` shows only `Add` and `Remove` calls lacking the predicate, both operating on objects already gate-checked by the fetch above them (`:58` and `:97`). No unguarded fetch remains.
Recommended action: none

## DGM-M2 — DeviceGroupMember missing global query filter
Severity: Medium
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `src/ToastRevival.Api/Data/AppDbContext.cs:79` — `e.HasQueryFilter(m => m.DeviceGroup.TenantId == _tenantProvider.TenantId)` is present, scoped through the required navigation (DeviceGroupMember has no own TenantId column, which is the correct shape given the schema). Anchor comment at `:77-79` documents the navigation scope decision clearly.
Recommended action: none

## XT-M1 — Single-use token reinstall carve-out not hardware-bound
Severity: Medium
Original status: OPEN
Audit verdict: AUDIT-PASS
Evidence: `src/ToastRevival.Api/Controllers/DevicesController.cs:719-735` — anchor is present, extensive, and accurate. Gap correctly described (self-reported `(DeviceName, Username)` tuple, not hardware-backed). Severity context is sound ("HKLM read already implies machine compromise"). Keith's phone decision recorded verbatim (fresh-token-per-reinstall REJECTED due to RMM mass-deploy conflict). Fix path named (machine SID binding, XT-3 build-mode project). OPEN status is the only correct status here — no code changed, no fix shipped, and the anchor explicitly scopes it as Keith-owned until XT-3 ships.
Recommended action: none — OPEN status is justified; re-evaluate when XT-3 ships

## XT-L1 — Atomic enrollment-token claim missing tenant predicate
Severity: Low
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `src/ToastRevival.Api/Controllers/DevicesController.cs:700` — `WHERE t.Id == token.Id && t.TenantId == tenantId && t.UsedAt == null && t.RevokedAt == null` in the `ExecuteUpdateAsync` atomic claim. Tenant predicate is present and load-bearing.
Recommended action: none

## XT-L2 — Lost-race re-read missing tenant predicate
Severity: Low
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `src/ToastRevival.Api/Controllers/DevicesController.cs:710-711` — `FirstOrDefaultAsync(t => t.Id == token.Id && t.TenantId == tenantId)` on the lost-race re-read path. Anchor comment at `:708-709` documents why the predicate is required. Tenant scope is correctly preserved so a leaked `token.Id` cannot surface another tenant's row into the reinstall carve-out.
Recommended action: none

## XT-L3 — No automated coverage for XT-1 enrollment-token feature
Severity: Low
Original status: REMEDIATED
Audit verdict: AUDIT-PASS
Evidence: `tests/ToastRevival.Api.Tests/SecurityTests.cs:831,853,872,896,921,963` — six XT-L3-tagged tests present: admin-only gating, expired-token rejection, cross-tenant rejection, revoked-token rejection, reinstall-same-machine-allowed / different-machine-rejected, and concurrent-claim atomicity. The remediation log's description of all six scenarios is accurate. Token seeds use lowercase hex (matching production lookup). No behavior bugs remain from the remediation log's self-correction notes.
Recommended action: none

---

## Summary

The remediation pass was clean and honest. All five FIXED-VERIFIED findings have verifiable code changes at the exact cited locations, and each fix matches the architectural pattern described in the remediation log. The XT-L3 test coverage is comprehensive and correctly structured. The XT-M1 OPEN anchor is the strongest anchor in this codebase — it records a named architectural decision, a concrete counterargument to each rejected alternative, and a clear owner and fix path. The remediation team appropriately declined to rubber-stamp it.

One minor observation for the next review pass: `MfaService.VerifySecret` (a non-atomic in-memory enrollment helper) shares enough naming similarity with the now-atomic `VerifyAndClaimAsync` that a future reviewer could mistake it for a missed call site. The distinction is clear in context (enrollment setup vs. authentication), but a one-line doc comment on `VerifySecret` noting "enrollment-only — not for authenticating an existing secret" would kill the ambiguity permanently.

Out-of-scope findings (for next reviewer): none surfaced during this audit pass.
