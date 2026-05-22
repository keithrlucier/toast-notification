# Toast Notification — Self-Hosted

Built in 2020 for MSPs drowning in help desk calls when the world went remote.
Teams and Slack filled most of that gap. For the shops where OS-level fleet
notification still matters — it's here, it works, and it's yours.

986,000 messages delivered across 17 production tenants. A passion project that
outlived its market peak because some problems don't need scale to be worth solving.

---

## What It Does

Sends branded, signed Windows toast notification banners to enrolled endpoints —
without requiring Teams, Slack, or any third-party app installed on the machine.
The agent is a signed Windows service that connects to your backend via SignalR.
The backend is a standard ASP.NET Core 8 / PostgreSQL stack.

**Use cases:**
- Fleet-wide announcements to endpoints that don't have chat apps installed
- Maintenance window warnings that appear at the OS level, not buried in email
- Security alerts that break through Do Not Disturb on Windows 11
- MSP-branded notifications delivered under your tenant's logo

---

## Requirements

- Docker and Docker Compose (V2) on a Linux host
- A DNS name or IP your Windows agents can reach
- The signed Windows agent MSI (see [Agent Distribution](#agent-distribution))

---

## Deploy in Three Steps

**1. Clone and configure**

```bash
git clone https://github.com/keithrlucier/toast-notification.git
cd toast-notification
cp .env.example .env
```

Open `.env` and fill in three required values:

```
POSTGRES_PASSWORD=   # any strong password
JWT_KEY=             # openssl rand -base64 32
PUBLIC_URL=          # https://toast.yourcompany.com (or http://192.168.x.x)
REVIEW_EMAIL=        # where trial approval requests land
```

Everything else in `.env` has a safe default and can be configured later
through the admin dashboard.

**2. Start the stack**

```bash
docker compose up -d
```

This starts three containers: the ASP.NET Core API, the React dashboard served
via nginx, and PostgreSQL. Database migrations run automatically on first boot.

**3. Create your first admin**

Open `http://your-host` in a browser. The first account to register becomes
SuperAdmin. Subsequent registrations require admin approval.

---

## Agent Distribution

The Windows agent is a `.msi` installer. It **must be code-signed** to install
silently via RMM or Intune — unsigned MSIs trigger a SmartScreen warning that
blocks silent deployment, and many managed endpoint policies block unsigned
executables outright.

When you self-host, you build and sign the agent yourself. We do **not**
distribute a pre-signed agent for self-hosted instances — the only agents that
carry our **Toast2IT, LLC** certificate are the ones we deliver to managed
subscribers from their portal, running against our infrastructure. A binary
signed under our certificate is never meant to run against a server we don't
operate. For your own deployment, the agent is signed under *your*
organization's certificate, which is also what puts your name (not ours) in the
SmartScreen trust prompt your endpoints will see.

### Compile from source and sign yourself

The agent binary must carry your own organization's name in its Authenticode
signature. You build and sign it yourself. This is a non-trivial operational
commitment:

**What you need:**

1. **An OV Code Signing certificate** (~$300–400/yr) from a trusted CA
   (Sectigo, DigiCert, GlobalSign). EV certificates are no longer required for
   Windows SmartScreen trust — OV is sufficient.
   - Validation takes 1–3 business days. This is not an instant purchase.
   - The certificate Subject CN becomes your agent's publisher identity.

2. **A way to store and use the certificate.** Options:
   - **Software PFX** — simplest, but the private key is exportable. Acceptable
     for internal/non-MSP use.
   - **Hardware Security Module (HSM)** — a USB token (Thales SafeNet, etc.)
     that stores the key non-exportable. Required if you're deploying to
     customer endpoints under your brand.

3. **Windows SDK signtool.exe** — included with Visual Studio Build Tools.

**Steps:**

1. Fork the repo.
2. Update `Package.appxmanifest` — set `Publisher` to match your cert Subject
   exactly (copy it from the cert's Details tab, or run
   `(Get-AuthenticodeSignature <file>).SignerCertificate.Subject`).
3. Update `installer/ToastRevival.Agent.Setup.wxs` with your publisher info.
4. Run `.\scripts\build-msi.ps1 -Version "1.0.0.0"` — this builds and signs
   the MSI using whatever signing cert Windows CryptoAPI can find. If you're
   using a hardware token, plug it in and unlock it first.
5. Verify the signature: `Get-AuthenticodeSignature .\ToastNotification.Agent.msi`

**The honest trade-off:** signing the agent yourself puts your organization's
name on the binary and keeps full control of the trust chain in your hands — but
it costs a cert purchase, a 1–3 day validation wait, ongoing annual renewal, and
a signing workflow you own and maintain. If you don't want to own that, Managed
SaaS exists precisely so you don't have to: we run the certificate, the signing
pipeline, and the renewals, and deliver the signed agent from your portal.

---

## Reverse Proxy and TLS

The compose stack serves HTTP on port 80. For production, put a reverse proxy in
front and terminate TLS there. Example nginx upstream block:

```nginx
server {
    listen 443 ssl;
    server_name toast.yourcompany.com;

    ssl_certificate     /etc/ssl/certs/yourcompany.crt;
    ssl_certificate_key /etc/ssl/private/yourcompany.key;

    location / {
        proxy_pass         http://localhost:80;
        proxy_http_version 1.1;
        proxy_set_header   Upgrade    $http_upgrade;
        proxy_set_header   Connection "upgrade";
        proxy_set_header   Host       $host;
        proxy_set_header   X-Forwarded-Proto https;
    }
}
```

Update `PUBLIC_URL` in `.env` to your HTTPS address and restart: `docker compose up -d`.

---

## Configuration Reference

All configuration is in `.env`. The most common post-install settings:

| Setting | Where to configure | Notes |
|---|---|---|
| Email (Mailjet) | Admin dashboard → Settings → Messaging | For registration + password reset |
| SMS MFA (ClickSend) | Admin dashboard → Settings → Messaging | Optional — TOTP is an alternative |
| Content moderation | `.env` → `CONTENT_SAFETY_*` | Azure keys; leave blank to disable |
| Bot protection | `.env` → `TURNSTILE_*` | Cloudflare Turnstile; `false` by default |
| Trial length | `.env` → `TRIAL_DAYS` | Days before admin approval required |

---

## Billing

Billing is disabled in self-hosted deployments. Device limits are removed.
The Stripe integration in the codebase is the managed SaaS path — it does
nothing when `Stripe__SecretKey` is empty.

---

## Data and Persistence

Two Docker volumes survive container restarts and upgrades:

- `db-data` — PostgreSQL data directory
- `assets` — uploaded tenant logos and hero images

Back these up before upgrading.

---

## Upgrading

```bash
docker compose pull   # if using pre-built images
docker compose build  # if building from source
docker compose up -d
```

Migrations run automatically on startup. No manual schema steps needed.

---

## Contributing

Pull requests welcome. This is a side project, not a product — issues get looked
at when they get looked at. If you fix something useful, send the PR.

---

## License

MIT. Do whatever you want with it.
