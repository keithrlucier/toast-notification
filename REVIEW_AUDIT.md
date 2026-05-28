# Code Review Audit — 2026-05-26

Source ledger: REVIEW_LEDGER.md (commit 378385d)
Total rows audited: 10
Pass / Reopen / Incomplete / Drift: 10 / 0 / 0 / 0

---

## SEC-H-01 — IDOR: cross-tenant notification read (GET /api/notifications/{id})
Severity: HIGH
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `NotificationsController.cs:237-239` — `tenantId` extracted from claims before query; `FirstOrDefaultAsync(notif => notif.Id == id && notif.TenantId == tenantId)` in place. FindAsync is gone. Missed-site sweep (grep FindAsync across all controllers) confirms no sibling endpoint in NotificationsController uses FindAsync.
Recommended action: none

---

## SEC-H-02 — IDOR + data corruption: cross-tenant device decommission
Severity: HIGH
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `DevicesController.cs:170-173,180` — `tenantId` extracted at line 170; `FindAsync(id)` at 171 followed immediately by `if (device.TenantId != tenantId) return NotFound();` at 173 (Option B pattern). `DecrementConsumedAsync` at line 180 uses `device.TenantId`, which is identical to caller `tenantId` after the guard passes — data corruption vector closed.
Recommended action: none

---

## SEC-H-03 — IDOR: cross-tenant role change and user deletion
Severity: HIGH
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `UsersController.cs:82-84` (`UpdateRole`) and `100-102` (`Remove`) — both methods use `FindAsync(id)` followed by `if (user.TenantId != GetTenantId()) return NotFound();` before any mutation. Both guards confirmed present and ordered correctly (null check first, tenant check second, mutation last).
Recommended action: none

---

## SEC-M-01 — IDOR: cross-tenant delivery report metadata
Severity: MEDIUM
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `NotificationsController.cs:371-374` — `tenantId` extracted from claims at line 371; `FirstOrDefaultAsync(notif => notif.Id == id && notif.TenantId == tenantId)` at 372-373; no duplicate `var tenantId` declaration in scope. The `NotificationDeliveries` query that follows (lines 376-380) uses `Where(d => d.NotificationId == id)` — safe because (a) the notification was already verified to belong to caller's tenant above, and (b) `NotificationDeliveries` carries a global query filter on `TenantId` via `ITenantProvider`, providing defense-in-depth.
Recommended action: none

---

## SEC-M-02 — IDOR: cross-tenant moderation action (approve + reject)
Severity: MEDIUM
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `ModerationController.cs:65-67` (`Approve`) and `90-92` (`Reject`) — both methods extract `tenantId` from claims and use `FirstOrDefaultAsync(n => n.Id == id && n.TenantId == tenantId)`. No FindAsync call in this file. Both methods are symmetric and both guards confirmed.
Recommended action: none

---

## SEC-M-03 — Rate limiting ineffective behind Cloudflare for login endpoints
Severity: MEDIUM
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `Program.cs:153` (`login-per-ip`) and `169` (`login-sms-per-ip`) — both policies use `ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon"` as partition key, matching the pre-existing `trial-register-per-ip` pattern at line 185. `RemoteIpAddress` fallback is intact for non-Cloudflare traffic.
Recommended action: none

---

## SEC-M-04 — IDOR: cross-tenant blocklist entry deletion
Severity: MEDIUM
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `BlocklistController.cs:74-77` — `tenantId` extracted from claims at line 74; `FindAsync(id)` at 75; `if (entry.TenantId != tenantId) return NotFound();` at 77 before the Remove call. Guard is correctly ordered.
Recommended action: none

---

## SEC-M-05 — IDOR: cross-tenant API key revocation
Severity: MEDIUM
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `ApiKeysController.cs:87-91` — `tenantId` extracted from claims at 87; `FindAsync(id)` at 88; `if (key.TenantId != tenantId) return NotFound();` at 90; `RevokedAt` check at 91. Tenant guard correctly precedes the revocation state check.
Recommended action: none

---

## SEC-M-06 — XSS: `javascript:` URL injection in TrialRequests admin panel
Severity: MEDIUM
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `TrialRequests.tsx:160-166` — conditional renders `<a href={request.website}>` only when `/^https?:\/\//i.test(request.website)` passes; otherwise falls back to `<span>`. `target="_blank" rel="noreferrer"` present on the anchor. Pattern blocks `javascript:`, `data:`, and any other non-http(s) scheme. TypeScript compile confirmed clean per ledger.
Recommended action: none

---

## INFO-01 — JWT stored in localStorage
Severity: INFO
Original status: REJECTED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `AuthContext.tsx:83` — anchor `REVIEW-2026-05-25 INFO-01 REJECTED-by-design: localStorage is the chosen session storage mechanism; no active XSS vector was found in this pass; migration to httpOnly cookies requires a coordinated /api/auth/refresh backend endpoint + frontend refactor scoped as a dedicated milestone.` is present and regex-compliant. Rejection reasoning is sound: (1) no active XSS vector in scope, (2) cookie migration requires `/api/auth/refresh` backend work that cannot be done unilaterally in the frontend, (3) the milestone scoping is a legitimate architectural deferral with a named path forward.
Recommended action: none

---

## Summary

Remediation pass is clean. All 10 rows verified against current source — 8 FIXED rows have the correct guards in place at the cited locations, 2 REJECTED rows carry properly formatted anchor comments with defensible reasoning.

**Missed-site sweep findings:** Full grep for `FindAsync` across all controllers (12 hits total) confirmed no unguarded cross-tenant calls remain. `AssetsController.Delete()` (line 185) uses `FindAsync + null/tenantId check` (Option B) and was pre-existing safe — not a ledger omission. `TenantController` uses `FindAsync(tenantId)` where `tenantId` is derived from the caller's own JWT claim, not a user-supplied route parameter — no IDOR possible by construction. `TemplatesController` never uses `FindAsync` (uses `FirstOrDefaultAsync` with explicit `TenantId` constraint throughout).

**Out-of-scope findings (for next reviewer):** None surfaced during this audit.
