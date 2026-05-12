# nginx config — TOASTWEB1

Snapshot of the production nginx site config from `/etc/nginx/sites-available/toast` on TOASTWEB1 (54.82.103.160). Pulled 2026-05-09 alongside the INFO-SEC-006 deploy.

## What lives here

`toast.conf` — the live config. Not auto-deployed; this is a snapshot for documentation, audit, and reference. The authoritative copy is on the box.

Production currently has `/etc/nginx/sites-enabled/toast` as a regular file, not
a symlink to `/etc/nginx/sites-available/toast`. Push config changes to both
paths unless you intentionally replace the enabled file with a symlink. Do not
leave `*.bak` files under `sites-enabled/`; nginx includes every file matched by
that directory.

## Sync workflow

When the live config is changed in production, snapshot it back to this directory in the same commit that documents the change. Drift between the repo snapshot and the live config is a documentation bug.

```bash
# Pull live → repo
ssh -i Docs/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem \
    ubuntu@54.82.103.160 'sudo cat /etc/nginx/sites-available/toast' \
    > infrastructure/nginx/toast.conf

# Push repo → live (if rolling forward a config change)
scp -i Docs/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem \
    infrastructure/nginx/toast.conf \
    ubuntu@54.82.103.160:/tmp/toast.conf.new
ssh -i Docs/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem \
    ubuntu@54.82.103.160 \
    'STAMP=$(date +%Y%m%d-%H%M); \
     sudo mkdir -p /etc/nginx/sites-available/enabled-backups; \
     sudo cp /etc/nginx/sites-available/toast /etc/nginx/sites-available/toast.bak.$STAMP; \
     sudo cp /etc/nginx/sites-enabled/toast /etc/nginx/sites-available/enabled-backups/toast.bak.$STAMP; \
     sudo cp /tmp/toast.conf.new /etc/nginx/sites-available/toast; \
     sudo cp /tmp/toast.conf.new /etc/nginx/sites-enabled/toast; \
     sudo nginx -t && sudo systemctl reload nginx'
```

## Certbot-managed lines

The `ssl_certificate` / `ssl_certificate_key` lines and the `:80` redirect block are managed by Certbot's nginx plugin. Certbot annotates them with `# managed by Certbot` and rewrites them on cert renewal. Don't touch those lines via repo edits — Certbot will revert.

The defensive `add_header` directives at server scope are NOT touched by Certbot renewal — they sit in the user-managed body of the server block.

## What protects what

The `add_header ... always` directives at server scope inherit to every location unless that location adds its own headers (none currently do). This protects:

- `/` and `/login` — React SPA HTML served from `/opt/toast/dashboard/` (closes INFO-SEC-006)
- `/api/*` — proxied to ASP.NET; defense-in-depth duplicates ASP.NET's own middleware-set headers
- `/hubs/*` — SignalR; same defense-in-depth posture
- `/assets/*` — uploaded MSP assets (M5.C); HSTS especially relevant for image hotlinks

Defense-in-depth on `/api/*` produces duplicate `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, and `Permissions-Policy` headers (one set from nginx, one from ASP.NET). Browsers honor either; harmless. The redundancy means a misconfigured nginx (e.g., a future Certbot renewal that rewrites the server block) still leaves the API protected by its own middleware, and a misconfigured ASP.NET pipeline leaves the API protected by nginx.
