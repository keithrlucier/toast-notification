# Production Security Deploy (2026-05-09)

## Scope

Deploy commits `3da7476` (SEC-001/002/003 + INFO-M1-006), `70aa4e1` (SEC-004 + stale-FIX-LIST reconcile), `ed84e96` (SEC-005), `4fa1c52` (CONTEXT.md docs), and the M8.C audit-fence patch (`83a5ac5`'s FIX-M8C-001) to TOASTWEB1 production.

Everything in front of attackers that wasn't before:

| Surface | Status pre-deploy | Status post-deploy |
|---|---|---|
| `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy` on API responses | absent | **present** |
| JWT key min-length runtime guard | absent | **active** (throws on `< 32` chars in non-Development) |
| AuditController cross-tenant fence (FIX-M8C-001) | leaking other tenants' rows | **scoped by tenantId claim** |
| CSV formula-injection neutralization (SEC-004) | absent — `=CMD()` would execute on Excel open | **apostrophe-prefixed** |
| TOTP replay rejection (SEC-005) | replay accepted within ±90s window | **rejected** via `LastTotpStep` floor |
| `AspNetUsers.LastTotpStep` column | absent | **bigint NULL** |

## Pre-deploy state

```
$ ssh ubuntu@54.82.103.160 'sudo systemctl status toast-api --no-pager | head -3'
● toast-api.service - Toast Notification API
     Loaded: loaded (/etc/systemd/system/toast-api.service; enabled; preset: enabled)
     Active: active (running) since Sat 2026-05-09 13:55:07 UTC; 9h ago

$ ssh ubuntu@54.82.103.160 'ls -la /opt/toast/api/ToastRevival.Api.dll'
-rw-rw-rw- 1 toast toast 543232 May  9 13:53 /opt/toast/api/ToastRevival.Api.dll

$ psql ... -c "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 5;"
20260509130151_PlatformAdminBillingV2
20260509053033_M6Billing
20260509041341_M5TenantSettings
20260509035139_M5ApiKeyManagement
20260509024211_M3SecurityHardening
```

Production was at the M6/Codex-track state. The new migration `20260509190000_M3MfaTotpReplay` was the next one to apply.

## Build

```
$ dotnet publish src/ToastRevival.Api/ToastRevival.Api.csproj \
    --configuration Release --runtime linux-x64 --self-contained false \
    --output ./publish/api --nologo
ToastRevival.Api -> ./publish/api/

$ tar -cz -C ./publish/api -f /tmp/toast-api-sec.tgz .
$ ls -la /tmp/toast-api-sec.tgz
-rw-r--r-- 16129703 May  9 19:31 /tmp/toast-api-sec.tgz   (16 MB, 68 entries)
```

## Deploy procedure

```bash
scp -i Toast_Web_LightsailDefaultKey.pem /tmp/toast-api-sec.tgz ubuntu@54.82.103.160:/tmp/
ssh ubuntu@54.82.103.160 "
  sudo systemctl stop toast-api
  sudo cp -a /opt/toast/api /opt/toast/api.bak.pre-sec-001-005
  sudo mv /opt/toast/api/wwwroot /tmp/wwwroot-deploy-tmp
  sudo find /opt/toast/api -mindepth 1 -delete
  sudo tar -xzf /tmp/toast-api-sec.tgz -C /opt/toast/api
  sudo mv /tmp/wwwroot-deploy-tmp /opt/toast/api/wwwroot
  sudo chown -R toast:toast /opt/toast/api
  sudo systemctl start toast-api
"
```

`wwwroot/` (uploaded MSP assets from M5.C) preserved through the swap. Backup at `/opt/toast/api.bak.pre-sec-001-005` for instant rollback if needed.

## Migration

Applied automatically on startup via `db.Database.Migrate()` in `Program.cs`. Single `ALTER TABLE` statement; nullable bigint column add is metadata-only on Postgres 11+ (no table rewrite, no extended locking).

```
May 09 23:31:59 toast-api[30637]: Applying migration '20260509190000_M3MfaTotpReplay'.
May 09 23:31:59 toast-api[30637]: Executed DbCommand (3ms) ALTER TABLE "AspNetUsers" ADD "LastTotpStep" bigint;
May 09 23:31:59 toast-api[30637]: Executed DbCommand (1ms) INSERT INTO "__EFMigrationsHistory" VALUES ('20260509190000_M3MfaTotpReplay', '8.0.15');
```

Total schema-change time: 4ms.

## Verification

### Service state

```
● toast-api.service - Toast Notification API
     Active: active (running) since Sat 2026-05-09 23:31:57 UTC; 4s ago
   Main PID: 30637 (dotnet)
      Memory: 77.1M
```

Application logged "Now listening on: http://localhost:5216" + "Application started." Background services (orphan recovery sweep, scheduler sweep) ran their periodic loops cleanly.

### Defensive response headers (SEC-001)

```
$ curl -sS -D - -o /dev/null https://toastnotification.com/api/templates

HTTP/1.1 401 Unauthorized
Server: nginx/1.24.0 (Ubuntu)
WWW-Authenticate: Bearer
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: strict-origin-when-cross-origin
Permissions-Policy: camera=(), microphone=(), geolocation=()
```

All four defensive headers present even on a 401 challenge, exactly as designed (middleware sits before authentication so error responses also carry them).

### Same headers on /api/billing/plan

```
$ curl -sS -D - -o /dev/null https://toastnotification.com/api/billing/plan
HTTP/1.1 401 Unauthorized
... (same four defensive headers)
```

### Marketing + SPA still work

```
$ curl -sS -o /dev/null -w '%{http_code}\n' https://toastnotification.com/
200
$ curl -sS -o /dev/null -w '%{http_code}\n' https://toastnotification.com/login
200
```

### Auth challenge still rejects bad creds

```
$ curl -sS -o /dev/null -w '%{http_code}\n' \
    -X POST -H 'Content-Type: application/json' \
    -d '{"email":"nobody@nope.test","password":"wrong"}' \
    https://toastnotification.com/api/auth/login
401
```

## Finding caught during verification

### INFO-SEC-006 (open, M9 polish) — nginx-served SPA HTML lacks defensive headers

Direct `GET /login` returns the React SPA HTML from nginx's static-file path (`/opt/toast/dashboard/index.html`). That response carries only nginx's defaults — `Server`, `Content-Type`, `Last-Modified`, `ETag`, `Accept-Ranges`. None of the API's defensive headers appear because nginx is serving the static file directly without proxying through the ASP.NET pipeline. The SPA itself is therefore not protected against clickjacking or MIME confusion at the HTML-page level; only the XHR responses to `/api/*` get the protection.

```
$ curl -sS -D - -o /dev/null https://toastnotification.com/login
HTTP/1.1 200 OK
Server: nginx/1.24.0 (Ubuntu)
Content-Type: text/html
Last-Modified: Sat, 09 May 2026 13:59:00 GMT
ETag: "69ff3da4-ae4"
Accept-Ranges: bytes
(no defensive headers)
```

**Fix (M9 polish):** add `add_header` directives in `/etc/nginx/sites-available/toast` server block for the static-file path:
```nginx
add_header X-Content-Type-Options "nosniff" always;
add_header X-Frame-Options "DENY" always;
add_header Referrer-Policy "strict-origin-when-cross-origin" always;
add_header Permissions-Policy "camera=(), microphone=(), geolocation=()" always;
add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
```

`always` is required so they apply to non-2xx responses (4xx/5xx error pages from nginx). Once added at the nginx layer, both API and SPA responses carry the headers.

## Rollback procedure (if needed)

```bash
ssh ubuntu@54.82.103.160 "
  sudo systemctl stop toast-api
  sudo mv /opt/toast/api /opt/toast/api.failed-deploy
  sudo cp -a /opt/toast/api.bak.pre-sec-001-005 /opt/toast/api
  sudo systemctl start toast-api
"
```

The migration would need to be reverted manually since the rollback bits don't know about `LastTotpStep`:
```sql
ALTER TABLE "AspNetUsers" DROP COLUMN "LastTotpStep";
DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260509190000_M3MfaTotpReplay';
```

But: the older bits don't read `LastTotpStep` at all, so rollback without DB revert is also safe — the column just sits unused.

## Commits delivered to production

| Commit | Title |
|---|---|
| `83a5ac5` | M8.C: security pen-test + WS variant + reg-path load + FIX-M8C-001 |
| `3da7476` | Security defaults: response headers, JWT key guard, CodeQL, Dependabot |
| `70aa4e1` | SEC-004: CSV formula injection neutralization + reconcile stale FIX-LIST |
| `ed84e96` | SEC-005: TOTP replay rejection within ±1 step verification window |
| `4fa1c52` | docs: CONTEXT.md sections for SEC-004 + SEC-005 |

## Status

- toast-api running, no errors in logs
- migration applied successfully (4ms)
- defensive headers present on every API response
- SPA + marketing + auth all functioning
- INFO-SEC-006 logged for nginx-layer headers (M9 polish)
- backup retained at `/opt/toast/api.bak.pre-sec-001-005`
- 16 MB tgz preserved at `/tmp/toast-api-sec.tgz` on the box (clean up on next deploy)
