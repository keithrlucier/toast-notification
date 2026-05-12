# 2026-05-12 — M10 Trial Approval Gate Production Deploy

## Summary

Deployed the M10 trial approval gate, tenant install surface, and SEO refresh to production (TOASTWEB1, 54.82.103.160). Migration applied via auto-apply on service restart. Both deploy blockers from commit 2a6d5bf closed in this session.

## What shipped

### Env vars added to `/opt/toast/.env`

```
Turnstile__SiteKey=<configured>
Turnstile__SecretKey=<configured>
Turnstile__Required=true
Registration__ReviewEmail=support@toastnotification.com
Registration__TrialDays=14
Registration__AllowLegacyDirectRegister=false
```

Pre-change backup preserved at `/opt/toast/.env.bak.2026-05-12-pre-m10`. File ownership and 600 perms restored after edit.

### Binaries swapped

- API: `dotnet publish src/ToastRevival.Api --configuration Release --runtime linux-x64 --no-self-contained --output ./publish/api` → tar → scp → `/opt/toast/api/` swap. Prior version preserved at `/opt/toast/api.bak.pre-m10-2026-05-12/`. Uploaded asset directory `wwwroot/assets/` preserved across the swap.
- Dashboard: `npm run build` → tar → scp → `/opt/toast/dashboard/` swap. Prior version preserved at `/opt/toast/dashboard.bak.pre-m10-2026-05-12/`.

### Migration

`M10TrialApprovalGate` applied automatically on `systemctl restart toast-api.service`. Migrate() runs unconditionally at startup (Program.cs:253, "safe because Migrate() is idempotent"). Journal confirmed:

```
CREATE INDEX "IX_TrialRequests_Status_SubmittedAt" ON "TrialRequests" ("Status", "SubmittedAt");
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ('20260512003900_M10TrialApprovalGate', '8.0.15');
```

## Smoke tests (all PASS)

| Endpoint | Method | Expected | Result |
|---|---|---|---|
| `/api/auth/register/config` | GET | `{turnstileEnabled:true, turnstileSiteKey:"<site key>"}` | ✅ |
| `/api/health` | GET | `{status:"healthy", checks:{db:{healthy:true}, queue:{healthy:true}}}` | ✅ (DB 37ms) |
| `/register` | GET | Dashboard HTML loads | ✅ |
| `/api/auth/register` (legacy, valid body) | POST | 410 Gone with redirect message | ✅ |
| `/api/auth/register/init` (no Turnstile token) | POST | 400 "Complete the human verification challenge." | ✅ |
| `/api/auth/register/init` (fake Turnstile token) | POST | 400 "Human verification failed. Please try again." | ✅ — proves Cloudflare siteverify reachable from prod with the configured secret |

## Service state post-deploy

- `toast-api.service`: active, listening :5216
- Hosting environment: Production
- ASP.NET Core 8.0.26 runtime
- All prior migrations preserved, M10 cleanly applied
- nginx unchanged, no config drift

## Rollback procedure (if needed)

```bash
ssh -i Docs/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem ubuntu@54.82.103.160 "
  sudo systemctl stop toast-api.service
  sudo mv /opt/toast/api /opt/toast/api.bad-m10
  sudo mv /opt/toast/api.bak.pre-m10-2026-05-12 /opt/toast/api
  sudo mv /opt/toast/dashboard /opt/toast/dashboard.bad-m10
  sudo mv /opt/toast/dashboard.bak.pre-m10-2026-05-12 /opt/toast/dashboard
  sudo cp /opt/toast/.env.bak.2026-05-12-pre-m10 /opt/toast/.env
  sudo chown toast:toast /opt/toast/.env
  sudo systemctl start toast-api.service
"
```

The M10 migration is additive (new TrialRequests table only), so a code rollback to pre-M10 binaries does not require a schema rollback — the unused table can stay.

## Open items post-deploy

- Real end-to-end test: submit a trial request from a browser (passing real Turnstile challenge), approve via `/system/trial-requests` as platform admin, verify password setup email arrives, verify approved tenant can sign in and download MSI. Pending Keith.
- INFO-RATELIMIT-001 still open — `UseForwardedHeaders` not configured. Rate-limit partition currently falls back from `CF-Connecting-IP` (which works) to `Connection.RemoteIpAddress` (which under Cloudflare collapses to the Cloudflare edge IP). The CF-Connecting-IP path is the intended one and is wired correctly in the policy; the forwarded-headers gap matters more for ASP.NET's HttpContext.Connection.RemoteIpAddress reads elsewhere.
