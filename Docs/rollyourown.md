# Toast Notification: Self-Hosted ("Roll Your Own") Architecture

> **Objective:** Define the architecture, release strategy, and engineering requirements for a publicly distributable, self-hostable version of Toast Notification.

## 1. The Strategy: Open Core & The SaaS Moat

We are adopting a dual-distribution model:
1. **SaaS (Managed):** $22.00/mo minimum (up to 100 devices). We handle the hosting, auto-updates, database backups, Azure Content Safety moderation, and high availability.
2. **Self-Hosted (Roll Your Own):** Users can download the repo or our Linux container images to run the platform on their own infrastructure for free.

**Why this works:** The backend is easily containerized, making it simple for an IT pro to evaluate. However, deploying, code-signing, and updating a Windows App SDK agent across a fleet of endpoints is high-friction. The open repo serves as a transparent trust-builder and lead generator; the friction of Windows endpoint management converts enterprise evaluators into SaaS paying customers.

---

## 2. The "Linux Image" (Docker Deployment)

We will not distribute a bare-metal Linux OS image. We will distribute OCI-compliant container images (Docker). 

A self-hoster's deployment will consist of a single `docker-compose.yml` file that orchestrates three services:

* **`toast-api`**: ASP.NET Core 8 backend.
* **`toast-dashboard`**: Nginx serving the Vite/React static files.
* **`toast-db`**: Standard `postgres:16-alpine` image.

### Engineering Action Items (Backend):
* Create a `Dockerfile` in `src/ToastRevival.Api/` (multi-stage build).
* Create a `Dockerfile` in `src/ToastRevival.Dashboard/` (builds Vite, outputs to an Nginx alpine image).
* Create a `docker-compose.yml` at the repository root.
* Ensure EF Core migrations execute automatically on container startup (already supported via the `IsDevelopment()` bypass in `Program.cs`, but needs robust lock-handling for container restarts).

---

## 3. Decoupling Billing & SaaS Services

The current codebase is tightly integrated with Stripe (M6 Licensing). We need to cleanly sever this for self-hosters without branching the codebase.

### Engineering Action Items (API & Dashboard):
* **Feature Flag:** Introduce an environment variable: `TOAST_REQUIRE_BILLING` (default `true` for our SaaS, `false` for self-hosters).
* **Backend Enforcement:** When `false`, `LicenseService.CanRegisterDeviceAsync` always returns `true`. Stripe webhooks and checkout API endpoints return 501 Not Implemented.
* **Dashboard UI:** When the frontend detects billing is disabled (via a new property on `GET /api/system/config` or similar), it hides the "Billing" sidebar navigation, hides the "Plan Limits" progress bars, and bypasses the Stripe checkout step during the onboarding wizard.
* **Azure Content Safety:** Already gracefully degrades to `Pass` if keys are missing. No changes required.

---

## 4. The Windows Agent Distribution Strategy

The Windows Agent requires an OV Code Signing certificate to bypass SmartScreen and install silently via RMM/Intune. We will offer self-hosters two paths:

### Path A: Use Our Pre-Compiled Agent (Recommended)
Self-hosters can download our official `.msi` from GitHub Releases. It is signed by `Toast2IT, LLC`.
To point it at their self-hosted Linux box, they deploy it via RMM with:
`msiexec /i ToastNotification.Agent.msi /qn CLIENTID=<guid> SERVERURL=https://toast.theircompany.com DISABLEAUTOUPDATE=1`

*Requirement:* We must ensure the `DISABLEAUTOUPDATE` registry key prevents the agent from pinging `releases.toastnotification.com` so self-hosters aren't pulling our Velopack updates and overwriting their configurations.

### Path B: Compile from Source
Self-hosters fork the repo, change the Publisher in `Package.appxmanifest`, buy their own $300+ Sectigo/DigiCert OV certificate, and run `build-msi.ps1`. 

---

## 5. Security & Default Configuration

If we make this repo public, we must assume malicious actors will read the source code.

* **Secrets:** Ensure no AWS Lightsail IPs, passwords, or SafeNet PINs are hardcoded anywhere in the git history. (Code Sweep confirms `Docs/Assets/*.pem` are properly gitignored, but a history scrub may be required before public launch).
* **Admin Bootstrapping:** Self-hosted instances need a "First Run" experience. If the database has 0 users, the first person to hit `/register` becomes the `SuperAdmin` + `IsPlatformAdmin`. Subsequent registrations must be invite-only or tenant-scoped.

---

## 6. Next Steps for Implementation Team

1. **Dockerization Sprint:** Write the `Dockerfiles` and `docker-compose.yml`. Verify the system spins up locally on a clean machine using only Docker Desktop.
2. **Feature Flag Sprint:** Implement `TOAST_REQUIRE_BILLING=false` and audit the frontend to ensure Stripe logic is hidden.
3. **Documentation:** Write a `README-SELF-HOST.md` with the 3-step Docker compose instructions.
4. **Public Repo Prep:** Audit git history for `.env` files or password leaks before flipping the GitHub repository from Private to Public.