# Toast Notification: Self-Hosted ("Roll Your Own") Architecture

> **Objective:** Define the architecture, release strategy, and engineering requirements for a publicly distributable, self-hostable version of Toast Notification.
>
> **How the public repo gets updated:** Every release tag in the private working repo is mirrored to `keithrlucier/toast-notification` via `scripts/sync-public-mirror.ps1`. The workflow, the sanitization rules, and the audit checklist are documented in [`Docs/PUBLIC-MIRROR.md`](PUBLIC-MIRROR.md).

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

The Windows Agent MSI **must be code-signed** with an OV Code Signing certificate
to install silently via RMM or Intune. Unsigned MSIs trigger a SmartScreen block
and are rejected by most endpoint management policies. This is the single highest-
friction element of self-hosting — the backend is a standard Docker stack, but
the Windows agent is a different class of problem.

There is exactly one self-host path: **compile from source and sign the agent
with your own certificate.** We do not distribute a pre-signed agent for
self-hosted instances.

This is a deliberate boundary, not an omission. The only agents that carry the
**Toast2IT, LLC** signature are the ones we deliver to managed subscribers from
their portal, running against infrastructure we operate. A binary signed under
our certificate is our company's identity vouching for what runs on the endpoint
— we will not let that signature run against a server we don't control, and we
can't revoke or stand behind it once it's pointed somewhere we can't see. When
you self-host, the trust chain is yours end to end: your certificate, your name
in the SmartScreen prompt, your fleet.

### Compile from Source and Sign Yourself

The agent binary carries your own organization's name in its Authenticode
signature. You build and sign it yourself. This is a real operational
investment:

**Requirements:**

| Requirement | Detail | Cost |
|---|---|---|
| OV Code Signing certificate | Sectigo, DigiCert, or GlobalSign. OV is sufficient — EV is no longer required for SmartScreen trust. | ~$300–400/yr |
| Certificate validation | CA verifies your organization exists. Not instant. | 1–3 business days |
| Signing infrastructure | Software PFX (simplest, exportable key) or HSM token (Thales SafeNet — non-exportable, required for customer-facing deployments) | $0–$250 for token hardware |
| Windows SDK | `signtool.exe` for signing, `Get-AuthenticodeSignature` for verification | Free with Visual Studio Build Tools |

**Implementation steps:**
1. Fork the repo.
2. Update `Package.appxmanifest` — set `Publisher` to your cert Subject exactly.
3. Update `installer/ToastRevival.Agent.Setup.wxs` with your publisher info.
4. Run `.\scripts\build-msi.ps1 -Version "x.y.z.w"` — signs using whatever cert
   Windows CryptoAPI can access. Plug in your hardware token first if applicable.
5. Verify: `Get-AuthenticodeSignature .\ToastNotification.Agent.msi`

**The honest trade-off:** signing the agent yourself puts your name on the binary
and keeps the whole trust chain in your control. It also means you own the cert
purchase, the 1–3 day validation wait, the annual renewal, the signing hardware,
and the build workflow.

### Why This Is Our Competitive Moat

This is intentional product design, not an oversight. The backend is containerized
and trivially self-hostable. The agent signing workflow is genuinely high-friction,
and we don't paper over it by lending out our signature — self-hosters own it
themselves. Operators who price out a certificate, a hardware token, and an annual
renewal-and-signing workflow tend to look at the $22/month SaaS price differently.

**The managed SaaS value proposition in one line:** We handle the OV certificate,
the hardware token, the signing pipeline, the annual renewal, and keeping the
agent on the Windows trusted publishers list — and deliver the signed agent from
your portal. You deploy. We sign. Self-host, and that's yours to run.

---

## 5. First-Run Admin Bootstrap

Self-hosted instances ship with no users in the database. The first person to
register at `/register` is promoted to `SuperAdmin` + `IsPlatformAdmin`
automatically. Subsequent registrations require approval through the admin
dashboard's Trial Requests queue, or invite-only flows configured per tenant.
This is the same registration pipeline managed SaaS uses, with the trial
approval gate enforced in both modes.

For day-to-day self-host operations — environment variables, three-step
deploy, reverse proxy and TLS, billing-disabled defaults, upgrade procedure,
and the agent distribution paths — see
[`README-SELF-HOST.md`](../README-SELF-HOST.md).