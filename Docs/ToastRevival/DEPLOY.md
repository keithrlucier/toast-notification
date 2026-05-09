# ToastRevival — Production Deployment

**Target**: Two AWS Lightsail instances. One session to provision and go live.

---

## Lightsail Console — Provision Both Boxes

Do this before the deployment session. Takes 5 minutes.

### Box 2 — Data (provision first so private IP is known before Box 1 config)

1. Lightsail → **Create instance**
2. Platform: **Linux/Unix**
3. Blueprint: **OS Only → Ubuntu 22.04 LTS**
4. Instance plan: **$10/mo (2 vCPU, 1 GB RAM, 40 GB SSD)**
5. Instance name: `toast-data`
6. **Launch** — note the private IP from the Networking tab once running (format: `172.26.x.x`)
7. Firewall (Networking tab):
   - Delete the default HTTP (80) rule
   - Keep SSH (22) — restrict source to your IP
   - Do NOT add any other rules — PostgreSQL stays off the public internet

### Box 1 — Web/App

1. Lightsail → **Create instance**
2. Platform: **Linux/Unix**
3. Blueprint: **OS Only → Ubuntu 22.04 LTS**
4. Instance plan: **$12/mo (2 vCPU, 2 GB RAM, 60 GB SSD)**
5. Instance name: `toast-web`
6. **Launch** — note the **public IP** from the Networking tab
7. Firewall (Networking tab):
   - SSH (22) — restrict source to your IP
   - HTTP (80) — source: Any
   - HTTPS (443) — source: Any

### Static IP (Box 1 only)

Attach a Lightsail static IP to `toast-web`. Without this, the public IP changes on stop/start and your DNS breaks.

Lightsail → **Networking** → **Create static IP** → attach to `toast-web`.

### SSH Key

Lightsail → **Account** → **SSH keys** — download the default key or use the one you already have from DocPro. Same key works for both boxes.

### Snapshots

After go-live, enable automated snapshots on both instances:
Lightsail → instance → **Snapshots** → **Enable automatic snapshots** → daily, retain 7.

---

## Architecture

```
Internet
    │
    ▼ HTTPS :443
┌─────────────────────────────┐
│  Box 1 — Web / App          │  2 GB RAM · 2 vCPU · 60 GB SSD
│  Ubuntu 22.04 LTS           │
│                             │
│  nginx                      │  terminates TLS, serves React,
│    /api/* → :5216           │  proxies API + SignalR
│    /hubs/* → :5216 (WS)     │
│    /assets/* → :5216        │
│    /* → dist/ (SPA)         │
│                             │
│  ToastRevival.Api (Kestrel) │  listens :5216 (localhost only)
│    ASP.NET Core 8           │
│    UseStaticFiles()         │  serves wwwroot/assets/*
│                             │
│  React dist/                │  built from src/ToastRevival.Dashboard
└──────────────────┬──────────┘
                   │ private network
                   ▼ PostgreSQL :5432
┌─────────────────────────────┐
│  Box 2 — Data               │  1 GB RAM · 2 vCPU · 40 GB SSD
│  Ubuntu 22.04 LTS           │
│                             │
│  PostgreSQL 15              │
│    database: toastrevival   │
│    user: toast              │
│    listen: Box 1 IP only    │
└─────────────────────────────┘
```

Box 1 is the only public-facing machine. Box 2 listens only on the private Lightsail network (firewall: accept port 5432 from Box 1 IP only, drop everything else).

---

## DNS

| Record | Type | Value |
|---|---|---|
| `toastnotification.com` | A | Box 1 public IP |
| `www.toastnotification.com` | CNAME | `toastnotification.com` |

The React dashboard and API live on the same domain (`/api/...` paths). No subdomain split needed.

**Before changing DNS**: lower the TTL on the A record to 300 (5 minutes) at least an hour before the cutover. This limits propagation lag to 5 minutes instead of potentially hours.

**DNS cutover order**: point the A record → get Let's Encrypt cert → reload nginx → verify HTTPS → done.

---

## Box 2 — Database

### Provision
- Lightsail: Ubuntu 22.04, 1 GB, region matching Box 1
- Firewall: allow SSH (22) from your IP only, block everything else at Lightsail level
- PostgreSQL listens on Box 1's private IP only (set in `postgresql.conf` + `pg_hba.conf`)

### Setup commands (run in deployment session)
```bash
sudo apt update && sudo apt install -y postgresql
sudo -u postgres psql <<'SQL'
CREATE DATABASE toastrevival;
CREATE USER toast WITH ENCRYPTED PASSWORD '<STRONG_PASSWORD>';
GRANT ALL PRIVILEGES ON DATABASE toastrevival TO toast;
SQL
```

### postgresql.conf
```
listen_addresses = '<box2_private_ip>'
```

### pg_hba.conf (append)
```
host  toastrevival  toast  <box1_private_ip>/32  scram-sha-256
```

### Connection string (for Box 1 appsettings.Production.json)
```
Host=<box2_private_ip>;Port=5432;Database=toastrevival;Username=toast;Password=<STRONG_PASSWORD>;
```

---

## Box 1 — Web / App

### Provision
- Lightsail: Ubuntu 22.04, 2 GB, same region as Box 2
- Firewall: allow SSH (22) from your IP, HTTP (80), HTTPS (443) from everywhere

### Software stack
```bash
# .NET 8 runtime
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt update && sudo apt install -y aspnetcore-runtime-8.0

# nginx
sudo apt install -y nginx

# certbot (Let's Encrypt)
sudo apt install -y certbot python3-certbot-nginx

# Node (for dashboard builds)
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
sudo apt install -y nodejs

# git
sudo apt install -y git
```

### App directory layout
```
/opt/toast/
  api/          ← published API (dotnet publish output)
    wwwroot/
      assets/   ← uploaded MSP assets (created at startup)
  dashboard/    ← npm run build output (dist/)
  .env          ← environment variables (chmod 600, owned by toast user)
```

### Deployment user
```bash
sudo useradd -m -s /bin/bash toast
sudo mkdir -p /opt/toast
sudo chown -R toast:toast /opt/toast
```

### Build and deploy API (run in deployment session)
```bash
# On dev machine:
dotnet publish src/ToastRevival.Api \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained false \
  --output ./publish/api

# rsync to Box 1:
rsync -avz ./publish/api/ toast@<box1_ip>:/opt/toast/api/
```

### Build and deploy dashboard (run in deployment session)
```bash
# On dev machine:
cd src/ToastRevival.Dashboard
npm ci
npm run build

# rsync to Box 1:
rsync -avz ./dist/ toast@<box1_ip>:/opt/toast/dashboard/
```

---

## Environment Variables (Box 1)

File: `/opt/toast/.env` (chmod 600, owned by toast)

```env
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://localhost:5216

# Generate with: openssl rand -base64 48
Jwt__Key=<64_CHAR_RANDOM_SECRET>
Jwt__Issuer=toast-api
Jwt__Audience=toast-dashboard

ConnectionStrings__DefaultConnection=Host=<box2_private_ip>;Port=5432;Database=toastrevival;Username=toast;Password=<STRONG_PASSWORD>;

# Azure Content Safety — leave blank to degrade gracefully to Pass
ContentSafety__Endpoint=
ContentSafety__Key=

# CORS — the public domain
AllowedOrigins__0=https://toastnotification.com
AllowedOrigins__1=https://www.toastnotification.com
```

---

## systemd Unit (Box 1)

File: `/etc/systemd/system/toast-api.service`

```ini
[Unit]
Description=Toast Notification API
After=network.target

[Service]
Type=exec
User=toast
WorkingDirectory=/opt/toast/api
EnvironmentFile=/opt/toast/.env
ExecStart=/usr/bin/dotnet /opt/toast/api/ToastRevival.Api.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=toast-api

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable toast-api
sudo systemctl start toast-api
sudo journalctl -u toast-api -f   # verify startup
```

EF Core migrations run automatically on startup in Production (the `IsDevelopment()` block already does this — change to always run, or run `dotnet ef database update` manually once before first start).

**Important**: Change the migration auto-apply to run unconditionally for production, or run migrations manually before starting:
```bash
cd /opt/toast/api
ASPNETCORE_ENVIRONMENT=Production dotnet ef database update
```

---

## nginx Config (Box 1)

File: `/etc/nginx/sites-available/toast`

```nginx
server {
    listen 80;
    server_name toastnotification.com www.toastnotification.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name toastnotification.com www.toastnotification.com;

    ssl_certificate     /etc/letsencrypt/live/toastnotification.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/toastnotification.com/privkey.pem;
    ssl_protocols       TLSv1.2 TLSv1.3;

    # React SPA static files
    root /opt/toast/dashboard;
    index index.html;

    # API proxy
    location /api/ {
        proxy_pass         http://localhost:5216;
        proxy_http_version 1.1;
        proxy_set_header   Host $host;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
    }

    # SignalR WebSocket
    location /hubs/ {
        proxy_pass         http://localhost:5216;
        proxy_http_version 1.1;
        proxy_set_header   Upgrade $http_upgrade;
        proxy_set_header   Connection "upgrade";
        proxy_set_header   Host $host;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_read_timeout 86400;
    }

    # Uploaded assets (served by Kestrel UseStaticFiles)
    location /assets/ {
        proxy_pass         http://localhost:5216;
        proxy_http_version 1.1;
        proxy_set_header   Host $host;
        proxy_cache_valid  200 7d;
        add_header         Cache-Control "public, max-age=604800";
    }

    # SPA fallback — all other routes serve index.html
    location / {
        try_files $uri $uri/ /index.html;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/toast /etc/nginx/sites-enabled/
sudo nginx -t
sudo certbot --nginx -d toastnotification.com -d www.toastnotification.com
sudo systemctl reload nginx
```

---

## Agent Configuration for Production

The Windows Agent connects to the backend via `SERVERURL`. For MSI deployment:

```
msiexec /i ToastNotification.Agent-x.y.z.msi /qn \
  CLIENTID=<tenant-guid> \
  SERVERURL=https://toastnotification.com
```

`appsettings.json` in the API must have a correct `Jwt:Issuer` and `Jwt:Audience` that matches what the agent bootstrap expects. The signing key and server URL come from the tenant registration — the agent gets them at device registration time, not from the MSI directly (the MSI only sets `bootstrap.json` → `tenantId` + `serverUrl`).

---

## Velopack Feed (M9)

The auto-update feed URL is currently a placeholder:
```
https://releases.toastnotification.com/agent/win-x64
```

Before M9, this needs to be a public HTTPS endpoint serving Velopack's release metadata. Options:
- GitHub Releases + a simple redirector
- A path on the same Box 1 (nginx serves static release files from `/opt/toast/releases/`)
- A CDN

Not a deployment blocker for the backend launch.

---

## EF Migration Note for Production

The `Program.cs` auto-migration block is gated on `IsDevelopment()`. For production first run, either:

**Option A** — Run migrations manually before starting the service:
```bash
cd /opt/toast/api
ASPNETCORE_ENVIRONMENT=Production \
ConnectionStrings__DefaultConnection="..." \
dotnet ef database update --project ToastRevival.Api.dll
```

**Option B** — Change the guard in `Program.cs` to always run:
```csharp
// Remove the IsDevelopment() check:
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}
```

Option B is simpler for initial deployment. Option A is safer for future deploys (explicit control over migration timing). Carl's call at deployment session.

---

## Go-Live Checklist — COMPLETE 2026-05-09

**Pre-session (Keith)**
- [x] Confirm domain DNS control for `toastnotification.com`
- [x] Provision Box 1 — TOASTWEB1, 2 GB Lightsail, Ubuntu 22.04, static IP 54.82.103.160
- [x] Provision Box 2 — TOASTDATA1, 1 GB Lightsail, Ubuntu 22.04, private IP 172.26.3.164
- [x] Private IPs noted (TOASTWEB1: 172.26.0.161, TOASTDATA1: 172.26.3.164)
- [x] `Jwt__Key` generated via `openssl rand -base64 48`
- [x] Azure Content Safety — blank (degrades to Pass); configure before M9 GA

**Deployment session (team — 2026-05-09)**
- [x] Box 2: PostgreSQL 16 installed, `toastrevival` DB + `toast` user created, `pg_hba.conf` allows 172.26.0.161/32, `postgresql.conf` listens on 172.26.3.164
- [x] Box 1: ASP.NET Core 8.0.26 runtime, nginx 1.24, Node 20.20.2, certbot installed
- [x] Box 1: `toast` user created, `/opt/toast/api/`, `/opt/toast/dashboard/`, `/opt/toast/api/wwwroot/assets/` created
- [x] Box 1: `/opt/toast/.env` written (chmod 600, owned by toast)
- [x] Box 1: API published (`dotnet publish --runtime linux-x64 --no-self-contained`) and deployed via scp+tar
- [x] Box 1: Dashboard built (`npm run build`) and deployed via scp+tar
- [x] Box 1: `toast-api.service` systemd unit installed, enabled, started — active
- [x] Box 1: migrations ran clean on first startup (all 5 migrations applied)
- [x] Box 1: nginx configured with API proxy + SignalR WebSocket + SPA fallback
- [x] Box 1: Let's Encrypt cert issued for toastnotification.com + www — HTTP→HTTPS redirect active
- [x] `https://toastnotification.com` — React dashboard loads (200 ✓)
- [x] `https://toastnotification.com/api/auth/login` — API responds (401 on bad creds ✓)
- [ ] Register first tenant via `/register`
- [ ] Register a test Windows Agent (MSI deploy with `SERVERURL=https://toastnotification.com`)
- [ ] Send a test notification end-to-end
- [ ] Check delivery tracking in dashboard history
- [ ] Terminate AWS Windows VM (52.21.249.120) — CI is now on GitHub Actions

**Post-launch**
- [ ] Set up Lightsail automated snapshots on both boxes (daily, 7-day retention)
- [ ] Set up Lightsail alarms: CPU > 80% on TOASTWEB1, disk > 80% on TOASTDATA1
- [ ] Configure Azure Content Safety endpoint + key before onboarding real MSP tenants

## Redeploy Procedure

When code changes need to go to production (run from dev machine):

```powershell
# 1. Build
dotnet publish src/ToastRevival.Api --configuration Release --runtime linux-x64 --no-self-contained --output ./publish/api
cd src/ToastRevival.Dashboard && npm ci && npm run build && cd ../..

# 2. Pack and upload
cd publish/api && tar -czf ../api.tar.gz . && cd ../..
cd src/ToastRevival.Dashboard/dist && tar -czf ../../../publish/dashboard.tar.gz . && cd ../../..

$KEY = "Docs/ToastRevival/Assets/Toast_Web_LightsailDefaultKey-us-east-1.pem"
scp -i $KEY publish/api.tar.gz ubuntu@54.82.103.160:/tmp/
scp -i $KEY publish/dashboard.tar.gz ubuntu@54.82.103.160:/tmp/

# 3. Extract and restart
ssh -i $KEY ubuntu@54.82.103.160 "
  sudo tar -xzf /tmp/api.tar.gz -C /opt/toast/api/ && sudo chown -R toast:toast /opt/toast/api
  sudo tar -xzf /tmp/dashboard.tar.gz -C /opt/toast/dashboard/ && sudo chown -R toast:toast /opt/toast/dashboard
  rm /tmp/api.tar.gz /tmp/dashboard.tar.gz
  sudo systemctl restart toast-api
  sleep 3 && sudo systemctl is-active toast-api
"
```
