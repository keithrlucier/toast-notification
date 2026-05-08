# ToastRevival — Technical Context

## Architecture Overview

Three-component product with shared .NET 8 ecosystem:

```
┌─────────────────────────────────────────────────────────────┐
│                    ADMIN DASHBOARD (React)                    │
│  Template Designer | Device Manager | Delivery Analytics     │
│  Send/Schedule | Tenant Settings | User Management           │
└──────────────────────────┬──────────────────────────────────┘
                           │ HTTPS / REST
┌──────────────────────────▼──────────────────────────────────┐
│                  BACKEND API (ASP.NET Core 8)                │
│  Multi-tenant Auth | Notification Queue | Content Moderation │
│  Device Registry | License Enforcement | Audit Logging       │
│  SignalR Hub (real-time push to agents)                       │
│  EF Core + PostgreSQL                                        │
└──────────────────────────┬──────────────────────────────────┘
                           │ SignalR WebSocket
┌──────────────────────────▼──────────────────────────────────┐
│              WINDOWS AGENT (.NET 8 / WinUI 3)                │
│  MSIX-packaged | COM Activator for toast handling            │
│  SignalR client (auto-reconnect)                             │
│  Device registration | Payload verification (HMAC)           │
│  Renders Windows toast notifications via WinRT APIs          │
└─────────────────────────────────────────────────────────────┘
```

## Technology Decisions

| Component | Technology | Rationale |
|---|---|---|
| Windows Agent | .NET 8 / WinUI 3 / MSIX | Required for WinRT toast APIs, COM activator, Store distribution |
| Backend API | ASP.NET Core 8 | SignalR native, same ecosystem as agent, EF Core maturity |
| Real-time Push | SignalR | Built-in reconnection, fallback transport, scales with Azure SignalR Service |
| Database | PostgreSQL | Cost-effective for SaaS, mature multi-tenant patterns, no SQL Server licensing |
| ORM | Entity Framework Core 8 | Code-first migrations, LINQ, interceptors for tenant filtering |
| Admin Dashboard | React + TypeScript | Component ecosystem, real-time state for live preview |
| Content Moderation | Azure Content Safety | Best category coverage, severity scoring, in-region processing |
| Auth (Dashboard) | ASP.NET Identity + JWT | Standard, no Azure AD B2C dependency (original's biggest deployment friction) |
| Auth (Agent) | Device registration tokens | Tenant-scoped, revocable, tied to device identity |
| Code Signing | OV certificate (~$300/yr) | EV no longer grants SmartScreen bypass; Store signs its own |
| Auto-Update (non-Store) | Velopack | Modern Squirrel successor, delta updates, .NET native |

## Deployment Channels

### 1. Microsoft Store (Primary)
- Existing listing: app ID 9P5L0MRMFRRF, TOAST2IT LLC account
- Microsoft signs the package — no SmartScreen issues
- Intune Store integration — MSPs search and assign
- Auto-updates via Store
- ACTION REQUIRED: Keith must accept updated App Developer Agreement in Partner Center

### 2. MSIX Sideload (Intune LOB)
- Signed with OV code signing certificate
- Deployed as Line of Business app in Intune
- Auto-update via .appinstaller file hosted on HTTPS
- Certificate must be trusted on endpoints (deploy via Intune config profile or use public CA)

### 3. MSI (RMM Deployment)
- Silent install: `msiexec /i Toast.msi /qn CLIENTID=xxx SERVERURL=https://...`
- Supports custom MSI properties for configuration
- MSI creates scheduled task in user context to run the agent
- Auto-update via Velopack (toggle-able via registry key)
- RMM tools: NinjaOne, Datto RMM, ConnectWise Automate, etc.

## Code Signing (MSI and MSIX)

The Sectigo OV cert lives on a Thales SafeNet hardware token. Keith plugs in the token, unlocks it via the SafeNet tray app, and the cert becomes available to Windows CryptoAPI for any signing tool. Cert details:

- **Subject (authoritative)**: `CN="Toast2IT, LLC", O="Toast2IT, LLC", S=Florida, C=US`
  - Four RDNs in this order. CN and O contain commas, so both are quoted.
  - This is the string MSIX `Package.Identity.Publisher` MUST match exactly. Read it from the DigiCert Cert Utility's Details tab Subject field, or `(Get-AuthenticodeSignature <signed-file>).SignerCertificate.Subject`.
- **Issuer**: `CN=Sectigo Public Code Signing CA R36, O=Sectigo Limited, C=GB`
- **Thumbprint**: `19B07B46712C2D87FF6AA99842F7EF6B036FEDA7`
- **NotAfter**: 2027-04-15
- **Timestamp authority**: `http://timestamp.digicert.com` (timestamped signatures stay valid past cert expiry)

### Tools that work for each format

| Format | DigiCert Cert Utility (v2.x) | signtool.exe (Windows SDK) |
|---|---|---|
| .exe / .dll | YES | YES |
| .msi        | YES (this is how M0A signed) | YES |
| .msix       | **NO** — utility doesn't support MSIX format | YES |
| .appx / .appxbundle | NO | YES |

**For MSIX, signtool.exe is the only path.** The DigiCert Certificate Utility 2.x is built around classic Authenticode formats; MSIX (with its AppxBlockMap, AppxSignature.p7x, and Publisher-vs-cert-subject DN match enforcement) is unsupported.

### Where signtool.exe lives on this dev box

Two reliable locations. The script `scripts/sign-msix.ps1` searches both:

1. **Windows SDK**: `C:\Program Files (x86)\Windows Kits\10\bin\<sdk-version>\x64\signtool.exe`
   - Only present if "Windows SDK Signing Tools for Desktop Apps" was selected during SDK install.
   - On this dev box the SDK is installed but the signing tools sub-component was NOT — so this path is empty.
2. **NuGet cache**: `%USERPROFILE%\.nuget\packages\microsoft.windows.sdk.buildtools\<ver>\bin\<ver>\x64\signtool.exe`
   - **ALWAYS present** after a successful WinAppSDK build because Microsoft.WindowsAppSDK 1.7 brings `Microsoft.Windows.SDK.BuildTools` as a transitive dep, and that package ships signtool.
   - This is the path that worked for M0 D2 signing.

### Signing flow

```powershell
# Plug in token, unlock via SafeNet tray app, then:
.\scripts\sign-msix.ps1 -Path artifacts\installer\msix\ToastNotification.Agent-0.2.0.1.msix
```

The script:
1. Searches both signtool locations and picks the highest-versioned x64 binary.
2. Invokes `signtool sign /a /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 <file>`. `/a` lets signtool pick the cert from any provider, including the token's CSP/KSP. SafeNet's PIN dialog pops automatically when signtool reaches for the private key.
3. Verifies via `Get-AuthenticodeSignature` that Status=Valid and the signer/issuer/timestamp match expectations.

### MSIX-specific failure modes (what we hit at M0 D2)

- `0x80091005` from the DigiCert Cert Utility on first MSIX sign attempt. **Two causes**:
  1. The utility doesn't support MSIX at all — switch to signtool.
  2. Manifest `Package.Identity.Publisher` does not match cert Subject DN exactly. Every RDN in the cert (CN, OU, O, L, S, C — whichever the cert has) must appear in the manifest Publisher in the same order with the same quoting. Our M0 D2 first build had only `CN, S, C` and missed `O=Toast2IT, LLC`; sign rejected with 0x80091005 even before we hit the format-support issue.
- `0x800B0109` (`CERT_E_UNTRUSTEDROOT`) at install time on the endpoint: cert chain isn't trusted on the target machine. For sideload, deploy the Sectigo intermediate via Intune cert profile or trust manually.

### Standing rules

1. **Authoritative cert subject reference**: the DigiCert Cert Utility Details tab Subject field, or `(Get-AuthenticodeSignature <signed-file>).SignerCertificate.Subject` from a previously-signed binary. **NOT** the team's prior memory string — that's been wrong (truncated) before.
2. **Code Sweep Step 4 for any change to MSIX manifest Publisher**: enumerate every RDN in the cert subject; verify each appears in the manifest Publisher in the same order; build the .msix; extract `AppxManifest.xml` from the produced .msix and re-verify (the build pipeline normalizes whitespace/quoting); only then hand off for sign.
3. **Token signing is not "hard"** — the SafeNet client exposes the token cert to Windows CryptoAPI when unlocked. Any signtool/signtool-wrapper that talks to CryptoAPI (DigiCert Utility, signtool.exe, custom wrappers) sees the cert through that surface. The token is incidental once SafeNet is set up.

## Toast Activator Class ID (MSIX, FIX-MSIX-004)

**CLSID**: `7FA7762F-41EC-4D72-9F06-58964AB36FEA`

Generated 2026-05-08 via `[guid]::NewGuid()`. Declared identically in both extension blocks of `src/ToastRevival.Agent/Package.appxmanifest`:

- `<com:Extension Category="windows.comServer">` → `<com:Class Id="7FA7762F-41EC-4D72-9F06-58964AB36FEA">`
- `<desktop:Extension Category="windows.toastNotificationActivation">` → `ToastActivatorCLSID="7FA7762F-41EC-4D72-9F06-58964AB36FEA"`

**Why this exists.** Packaged WinAppSDK `AppNotificationManager.Default.Register()` does NOT auto-inject a CLSID into HKCU like the unpackaged path does — the framework looks up the activator CLSID from the manifest. Without these two extension declarations, `Register()` returns success but the activation channel never wires, so subsequent `Show()` calls produce no visible toast and no Action Center entry. That was FIX-MSIX-004.

**Standing rules.**

1. The two CLSIDs in the manifest MUST be byte-for-byte identical and stay locked at this value across all future versions of the product. Changing the CLSID after public distribution orphans every installed client (their HKCU\SOFTWARE\Classes\CLSID registration points at the old GUID). Until M0 D5 Store flight there is no installed base, so churning is cheap; once we flight, this GUID is permanent.
2. The `<com:ExeServer>` element MUST include `Arguments="----AppNotificationActivated:"`. The four-dash sentinel is the framework's marker for "this is the toast activator surface." Missing it causes `AppNotificationManager.Default.Register()` to throw `COMException 0x80070490` (HRESULT_FROM_WIN32(ERROR_NOT_FOUND)) because the framework's COM class registration lookup fails. We hit this on 0.2.0.2 (commit `eca31dc`); fixed in 0.2.0.3.
3. Code Sweep Step 4 for any change to `<Extensions>`: confirm both CLSIDs match, confirm `xmlns:com` and `xmlns:desktop` are declared on `<Package>`, confirm `IgnorableNamespaces` includes `com desktop`, confirm `Arguments="----AppNotificationActivated:"` is on `<com:ExeServer>`, extract `AppxManifest.xml` from the produced .msix and re-verify post-build.
4. Reference: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/notifications/app-notifications/app-notifications-quickstart (Packaged section).

## Security Architecture

### Authentication & Authorization
- Multi-tenant isolation: tenant ID partition key on all queries
- RBAC: Technician (< 10 devices), Admin (groups), Super Admin (broadcast)
- MFA required for broadcast operations (> N devices)
- Per-tenant API keys for programmatic access
- Device registration tokens: tenant-scoped, revocable

### Content Moderation Pipeline
```
Submit Notification
  ├── Text: Azure Content Safety (Sexual/Violence/Self-harm/Hate, 0-6 severity)
  ├── Image: Skip if from approved asset library; scan ad-hoc uploads
  └── Decision:
      PASS (0-1) → Send immediately
      REVIEW (2-4) → Queue for admin approval
      BLOCK (5-6) → Reject + log + alert
```
- Estimated cost at MSP scale: ~$5/month for 100 tenants
- Tenant-configurable blocklists for custom terms

### Rate Limiting
- Per-tenant: 60/min, 500/hr, 5,000/day (configurable by tier)
- Per-device: 10/hr
- Broadcast gate: > 100 devices requires elevated permission + confirmation

### Payload Security
- HMAC-signed notification payloads
- Agent verifies signature before rendering
- Prevents MITM content injection

### Audit Trail
- Every notification: sender, content, targets, moderation scores, delivery status
- 90-day minimum retention (configurable)
- Exportable for compliance/incident response

## Agent Architecture (Windows)

### Deployment Flow (MSI/RMM)
1. RMM pushes MSI as SYSTEM
2. MSI installs agent binary + creates scheduled task in user context
3. On user login, scheduled task starts agent
4. Agent registers with backend (tenant ID from MSI property)
5. Agent establishes SignalR connection
6. Agent renders toast notifications on command

### Toast Notification Constraints (Windows)
- Max 3 text lines: 1 title + 2 body (truncates beyond)
- Hero image: 364x180px effective (2:1 ratio)
- App logo: 48x48 or 64x64px
- Inline images: up to 200px height
- Up to 5 action buttons (2-3 practical)
- Text inputs, dropdown selectors
- Audio: custom or ms-winsoundevent:* predefined
- Duration: short (7s) or long (25s)
- Scenarios: default, alarm, reminder, incomingCall, urgent

### Resilience
- SignalR auto-reconnect with exponential backoff
- Missed notification catch-up on reconnect
- Survives sleep/wake, network transitions
- Graceful degradation if backend unreachable

## Database Schema (Core Entities)

```
Tenant
  - Id (UUID), Name, Subdomain
  - LicenseCount, ConsumedCount
  - LicenseStart, LicenseEnd
  - SubscriptionTier, BillingStatus
  - CreatedAt, UpdatedAt

User (Dashboard)
  - Id, TenantId (FK)
  - Email, PasswordHash, MfaSecret
  - Role (Technician/Admin/SuperAdmin)
  - LastLogin, CreatedAt

Device
  - Id (UUID), TenantId (FK)
  - DeviceName, Username, OSVersion
  - AgentVersion, RegistrationToken
  - Status (Active/Inactive/Decommissioned)
  - LastPing, RegisteredAt

DeviceGroup
  - Id, TenantId (FK)
  - Name, Description
  - DeviceCount (denormalized)

NotificationTemplate
  - Id, TenantId (FK)
  - Name, Category (Announcement/Alert/ActionRequired/Reminder/Celebration/Maintenance)
  - TitleTemplate, BodyLine1Template, BodyLine2Template
  - HeroImageId, LogoImageId
  - ActionButtons (JSON: label, type, url)
  - AudioSetting
  - IsDefault, CreatedAt, UpdatedAt

Notification (sent instance)
  - Id (UUID), TenantId (FK), TemplateId (FK)
  - SenderId (FK → User)
  - Title, Body, HeroImageUrl, LogoUrl
  - TargetType (Device/Group/All)
  - TargetIds (JSON)
  - TargetDeviceCount
  - ModerationResult (JSON: scores, decision)
  - Status (Queued/Sending/Sent/PartialFailure/Failed)
  - ScheduledAt, SentAt, CompletedAt

NotificationDelivery
  - Id, NotificationId (FK), DeviceId (FK)
  - Status (Pending/Delivered/Clicked/Dismissed/Failed)
  - DeliveredAt, InteractedAt
  - Action (button clicked, input submitted)
  - ErrorMessage (if failed)

AssetLibrary
  - Id, TenantId (FK)
  - Name, Type (HeroImage/Logo/Icon)
  - Url, ContentHash
  - ModerationResult (JSON)
  - UploadedBy, UploadedAt

AuditLog
  - Id, TenantId (FK), UserId (FK)
  - Action, ResourceType, ResourceId
  - Details (JSON)
  - IpAddress, Timestamp
```

## Server Infrastructure

### Production/Build Server (AWS EC2)
| Property | Value |
|---|---|
| IP | 52.21.249.120 |
| Platform | AWS EC2 Windows Server |
| Hostname | EC2AMAZ-A5EU435 |
| SSH | Port 22 (OpenSSH — password auth) |
| RDP | Port 3389 |
| Username | Administrator |
| Password | See `Docs/ToastRevival/.env` (gitignored) |

**Installed as of 2026-05-07 (provisioned by Codex):**
- .NET SDK 8.0.420 (matches repo `global.json` pin)
- Git 2.53.0
- Visual Studio Build Tools 2022 (17.14.31)
- IIS (inetpub present, no sites configured yet)
- Amazon SSM Agent, EC2Launch, AWS PV Drivers

**Connection (SSH via Posh-SSH from dev machine):**
```powershell
Import-Module Posh-SSH
$pass = ConvertTo-SecureString $env:TOAST_SERVER_PASS -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential('Administrator', $pass)
$session = New-SSHSession -ComputerName 52.21.249.120 -Credential $cred -AcceptKey
```

**Notes:**
- WinRM ports (5985/5986) blocked by Security Group — SSH only
- PATH not set for SSH sessions — use full paths: `C:\Program Files\dotnet\dotnet.exe`, `C:\Program Files\Git\cmd\git.exe`
- Codex is handling provisioning — coordinate before running commands during active installs

---

### Server Verification Update - 2026-05-07

The server infrastructure section above was created during active provisioning. Direct verification later in the same session found:

- Key-based SSH from the development workstation works.
- .NET SDK `8.0.420` is installed at `C:\Program Files\dotnet\dotnet.exe`.
- Git `2.53.0.windows.2` is installed at `C:\Program Files\Git\cmd\git.exe`.
- Repo is cloned at `C:\toast` and tracks `https://github.com/keithrlucier/toast`.
- Visual Studio Build Tools installation was still running during the last verification.
- `vswhere` returned an empty product list.
- `signtool.exe` and `makeappx.exe` were not found yet.
- GitHub Actions self-hosted runner is not installed yet.
- The Administrator password was pasted into chat and should be rotated; do not document passwords in the repository.

Use the verification update as the current source of truth until the installer state is checked again.

## Competitive Landscape

| Product | Status | Gap |
|---|---|---|
| BurntToast (PowerShell) | Active, v1.1.0 | Script-only, no dashboard, no branding UI, no central management |
| imab.dk Toast Script | Active, v3.0 | Intune/ConfigMgr only, no management console |
| RMM built-ins | Basic | Limited customization, no rich templates |
| Recast Right Click Tools | Enterprise | Not a dedicated notification product |
| ToastNotification.com (original) | Discontinued standalone | Our own product — reviving it |

## Reference Architecture (Original Source)

Full Visual Studio solution in `ToastNotification.zip`:
- `Toast2IT.UWP` — UWP client (→ replace with WinUI 3)
- `Toast2IT.Web.API` — ASP.NET Core API (→ modernize to .NET 8)
- `Toast2IT.Web.AdminPanel` — Admin portal (→ replace with React dashboard)
- `Toast2IT.Web.UserPanel` — User portal
- `Toast2IT.Background` — Background tasks
- `Toast2IT.Common` — Shared models
- `AlternatePushChannel.Library` — WinRT encryption/Web Push
- `ToastNotification.HelperApp` — Windows Forms socket server (→ eliminate, use SignalR)

Key improvements over original:
1. SignalR replaces WNS + local socket server — eliminates two layers of complexity
2. React dashboard replaces Razor MVC — modern, responsive, real-time
3. JWT auth replaces Azure AD B2C — removes Azure dependency for customers
4. Content moderation pipeline — didn't exist in original
5. RBAC with broadcast controls — didn't exist in original
6. Live notification preview — didn't exist in original
