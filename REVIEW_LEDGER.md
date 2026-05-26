# Toast Notification — Code Review Ledger

**Pass date:** 2026-05-25  
**Reviewer:** Carl (architect sweep)  
**Scope:** Full cold review — all API controllers, AppDbContext, Program.cs, frontend security surface  
**Prior anchors:** None (first ledger on this workspace; no docs/review_history/ existed)  
**Remediation date:** 2026-05-25  
**Remediator:** Carl, Anthony, Abish (team-docpro)

---

## Summary

| Severity | Count | FIXED | REJECTED |
|----------|-------|-------|----------|
| HIGH     | 3     | 3     | 0        |
| MEDIUM   | 6     | 5     | 1        |
| INFO     | 1     | 0     | 1        |
| **Total**| **10**| **8** | **2**    |

---

## Findings

### HIGH

| ID | Status | File : Line | Finding | Disposition |
|----|--------|-------------|---------|-------------|
| SEC-H-01 | FIXED-VERIFIED | `src/ToastRevival.Api/Controllers/NotificationsController.cs:234` | **IDOR — cross-tenant notification read.** `GET /api/notifications/{id}` called `FindAsync(id)`, bypassing global filter. | Replaced with `FirstOrDefaultAsync(notif => notif.Id == id && notif.TenantId == tenantId)`. `tenantId` extracted from claims before query. Verified: build clean, verifier agent confirmed guard in place. |
| SEC-H-02 | FIXED-VERIFIED | `src/ToastRevival.Api/Controllers/DevicesController.cs:170` | **IDOR + data corruption — cross-tenant device decommission.** `FindAsync(id)` bypassed filter; `DecrementConsumedAsync` used caller tenantId not device tenantId. | Added `tenantId` extraction before FindAsync; added `if (device.TenantId != tenantId) return NotFound();`; changed DecrementConsumedAsync to lookup by `device.TenantId`. Verified clean. |
| SEC-H-03 | FIXED-VERIFIED | `src/ToastRevival.Api/Controllers/UsersController.cs:82,99` | **IDOR — cross-tenant role change and user deletion.** `UpdateRole` and `Remove` both called `FindAsync(id)` bypassing filter. | Added `if (user.TenantId != GetTenantId()) return NotFound();` after each FindAsync, before mutation. Verified clean. |

---

### MEDIUM

| ID | Status | File : Line | Finding | Disposition |
|----|--------|-------------|---------|-------------|
| SEC-M-01 | FIXED-VERIFIED | `src/ToastRevival.Api/Controllers/NotificationsController.cs:369` | **IDOR — cross-tenant delivery report metadata.** `DeliveryReport` called `FindAsync(id)` without tenant guard. | Moved `tenantId` extraction before query; replaced with `FirstOrDefaultAsync(notif => notif.Id == id && notif.TenantId == tenantId)`; removed duplicate `var tenantId` declaration. Verified clean. |
| SEC-M-02 | FIXED-VERIFIED | `src/ToastRevival.Api/Controllers/ModerationController.cs:65,89` | **IDOR — cross-tenant moderation action.** `Approve` and `Reject` called `FindAsync(id)` with no tenant check. | Both methods: moved `tenantId` extraction before query; replaced FindAsync with scoped `FirstOrDefaultAsync`; removed duplicate `var tenantId` declarations. Verified clean. |
| SEC-M-03 | FIXED-VERIFIED | `src/ToastRevival.Api/Program.cs:149-172` | **Rate limiting ineffective behind Cloudflare for login endpoints.** `login-per-ip` and `login-sms-per-ip` keyed on `RemoteIpAddress` (always Cloudflare edge IP). | Both policies now read `ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault()` as primary key, falling back to RemoteIpAddress. Matches the pattern already used by `trial-register-per-ip`. Verified clean. |
| SEC-M-04 | FIXED-VERIFIED | `src/ToastRevival.Api/Controllers/BlocklistController.cs:74` | **IDOR — cross-tenant blocklist entry deletion.** `Remove` called `FindAsync(id)` with no tenant check. | Added `var tenantId` extraction and `if (entry.TenantId != tenantId) return NotFound();` guard. Verified clean. |
| SEC-M-05 | FIXED-VERIFIED | `src/ToastRevival.Api/Controllers/ApiKeysController.cs:87` | **IDOR — cross-tenant API key revocation.** `Revoke` called `FindAsync(id)` with no tenant check. | Added `var tenantId` extraction and `if (key.TenantId != tenantId) return NotFound();` guard before RevokedAt check. Verified clean. |
| SEC-M-06 | FIXED-VERIFIED | `src/ToastRevival.Dashboard/src/pages/TrialRequests.tsx:160` | **`javascript:` URL injection in admin panel.** `request.website` rendered directly as `href`. | Replaced `<a href={request.website}>` with conditional: renders `<a>` only for `https?://` URLs, falls back to `<span>` for anything else. Verified via TypeScript clean compile. |

---

### INFO

| ID | Status | File : Line | Finding | Disposition |
|----|--------|-------------|---------|-------------|
| INFO-01 | REJECTED-VERIFIED | `src/ToastRevival.Dashboard/src/contexts/AuthContext.tsx:187-224` | **JWT stored in `localStorage`.** Bearer token persisted to localStorage, accessible to any JS. | REJECTED-by-design: localStorage is the chosen session storage mechanism; no active XSS vector found in this pass; migration to httpOnly cookies requires a coordinated /api/auth/refresh backend endpoint + frontend refactor scoped as a dedicated milestone. Anchor written at `AuthContext.tsx:83` matching regex `REVIEW-\d{4}-\d{2}-\d{2}\s+\S+\s+REJECTED-by-design:\s+.+`. Verified anchor present and regex-compliant. |

---

## FindAsync IDOR — Pattern Reference

The codebase uses EF Core global query filters (`HasQueryFilter`) on all tenant-scoped entities to provide automatic isolation. However, `FindAsync(primaryKey)` **bypasses global query filters** — it checks the first-level cache and falls back to a direct PK lookup with no additional predicates. The safe replacement patterns are:

```csharp
// UNSAFE — bypasses global filter
var entity = await _db.Entity.FindAsync(id);

// SAFE — option A: use LINQ (filter applies)
var entity = await _db.Entity
    .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);

// SAFE — option B: FindAsync + explicit guard
var entity = await _db.Entity.FindAsync(id);
if (entity is null) return NotFound();
if (entity.TenantId != tenantId) return NotFound();  // don't 403 — avoids GUID enumeration
```

Controllers that use the safe pattern post-remediation: `NotificationsController.Get()`, `NotificationsController.DeliveryReport()`, `ModerationController.Approve()`, `ModerationController.Reject()`, `DevicesController.Decommission()`, `UsersController.UpdateRole()`, `UsersController.Remove()`, `BlocklistController.Remove()`, `ApiKeysController.Revoke()`, `AssetsController.Rename()`, `TemplatesController.Update()`, `TemplatesController.Delete()`, `DevicesController.Get()`.

---

## Entities with Global Query Filters

Confirmed from `AppDbContext.cs`:

| Entity | Filter |
|--------|--------|
| `AppUser` | `u.TenantId == _tenantProvider.TenantId` |
| `Device` | `d.TenantId == _tenantProvider.TenantId` |
| `DeviceGroup` | `g.TenantId == _tenantProvider.TenantId` |
| `NotificationTemplate` | `t.TenantId == _tenantProvider.TenantId` |
| `Notification` | `n.TenantId == _tenantProvider.TenantId` |
| `NotificationDelivery` | `d.TenantId == _tenantProvider.TenantId` |
| `AssetLibrary` | `a.TenantId == _tenantProvider.TenantId` |
| `TenantBlocklistEntry` | `b.TenantId == _tenantProvider.TenantId` |
| `TenantApiKey` | `k.TenantId == _tenantProvider.TenantId` |
| `AuditLog` | **None** (intentional — cross-tenant admin view; `AuditController` scopes manually) |
| `Tenant` | **None** (no self-referential tenant filter) |
| `TrialRequest` | **None** (no tenant until approved) |

---

## Closed Anchors from Prior Passes

None — this is the first review ledger for this workspace.

Prior fix history is in `Docs/ToastRevival/FIX-LIST.md`. Notable closed items relevant to this pass:
- **FIX-M8C-001** — Cross-tenant audit log leak: `AuditController` now manually scopes to caller `tenantId` ✓ (verified)
- **SEC-001 through SEC-005** — Prior auth/signing hardening: verified intact ✓

---

## Remediation Verification Gates (2026-05-25)

- `dotnet build` on `ToastRevival.Api` — **PASS** (0 errors, 0 warnings)
- `npx tsc --noEmit` on `ToastRevival.Dashboard` — **PASS** (clean)
- Verifier agent: all 10 rows confirmed correct in source — **PASS**
