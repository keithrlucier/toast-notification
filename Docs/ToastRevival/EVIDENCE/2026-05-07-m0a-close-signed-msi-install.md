# M0A Close - Signed MSI Install on Win11 Lab Machine - 2026-05-07

## Purpose

Close M0A by closing every deliverable end-to-end: packaged locally (D4), signed with the renewed token-backed OV cert (D5), installed on a clean Windows 11 lab machine (D6), running in the logged-in user context (D7), surviving reboot/login (D8), and documented (D9). Also captures the post-install brand correction that rolled the agent's user-facing surfaces from the project codename to the product name.

## Packaging (D4)

- WiX 5.0.2 (last pre-OSMF release; WiX 7 requires the paid Open Source Maintenance Fee EULA, declined).
- Source: `installer/ToastRevival.Agent.Setup.wxs`. Per-machine install, EmbedCab, MajorUpgrade, x64.
- Build script: `scripts/build-msi.ps1` publishes self-contained (`-p:WindowsAppSDKSelfContained=true`) and runs `wix build` with `PublishDir` and `ProductVersion` variables.
- Two MSIs were produced this session:
  - `ToastRevival.Agent-0.1.0.0.msi` (50.60 MB) - first cut, used the project codename in user-visible strings. Signed and installed on the lab machine.
  - `ToastNotification.Agent-0.2.0.0.msi` (50.60 MB) - rebrand of all user-facing surfaces; same UpgradeCode, MajorUpgrade replaces 0.1.0.0 cleanly.

## Signing (D5)

Keith signed `ToastRevival.Agent-0.1.0.0.msi` locally using the Thales hardware token + Sectigo OV cert. Signature verification on the dev workstation:

```
Status:        Valid
StatusMessage: Signature verified.
Signer:        CN="Toast2IT, LLC", O="Toast2IT, LLC", S=Florida, C=US
Issuer:        CN=Sectigo Public Code Signing CA R36, O=Sectigo Limited, C=GB
NotAfter:      04/15/2027 19:59:59
Thumbprint:    19B07B46712C2D87FF6AA99842F7EF6B036FEDA7
Timestamp:     CN=DigiCert SHA256 RSA4096 Timestamp Responder 2025 1
```

Timestamp is present, so the signature remains valid past the cert expiry. The 0.2.0.0 rebrand MSI was rebuilt after install and is awaiting Keith's re-sign before redeploy.

## Clean-Machine Install (D6, D7, D8)

Keith installed the signed `ToastRevival.Agent-0.1.0.0.msi` on a clean Windows 11 lab machine.

- App installed - no issues. (D6 closed.)
- Shortly after reboot, toasts were seen. No issues. (D7 + D8 closed.)
- The Startup-folder shortcut fires `ToastRevival.Agent.exe --template alert --no-wait` at every login, so reboot/login both trigger the rich Alert toast (Urgent scenario, Alarm sound, hero image, app logo override, Acknowledge / Report to IT buttons).

## Brand Correction (D9 + standing rule)

After the lab install, Keith corrected the team: the app and all its assets is **Toast Notification** (the product, marketed at toastnotification.com). **ToastRevival** is the project codename only and stays internal (repo path, namespace, csproj filename, docs folder).

User-facing surfaces flipped in commit 56b0adb:
- WiX MSI: `ProductName="Toast Notification Agent"`, `Manufacturer="Toast2IT, LLC"`, install folder `%ProgramFiles%\Toast Notification`, shortcuts named `Toast Notification`, version 0.2.0.0.
- Assembly: `<AssemblyName>ToastNotification.Agent</AssemblyName>` so the exe ships as `ToastNotification.Agent.exe`.
- Hero PNG wordmark regenerated as "Toast Notification".
- Program.cs console output and Plain template title updated.
- Add/Remove Programs entry on the lab machine will swap from "ToastRevival Agent" to "Toast Notification Agent" automatically when 0.2.0.0 is installed (same UpgradeCode triggers MajorUpgrade).

## Standing Rule Captured

**Project name vs product name discipline.** The project codename can stay in repo paths, namespaces, csproj filenames, and internal docs. Anything a user sees - MSI ProductName, install folder, shortcut names, exe filename, hero/logo wordmarks, console output, error messages, marketing copy - uses the product name. Audit every new user-visible string against this rule. The brand correction this session was caught after install, not before; the discipline is to catch it before any first user-visible build ships.

## Boundaries

- The 0.2.0.0 rebrand MSI was rebuilt and verified at the WiX level (ProductName, Manufacturer, Version read out of the Property table) but was not re-signed and not re-installed - Keith confirmed re-test was unnecessary because the rename does not change install / login / reboot mechanics, only display strings.
- Lab machine SmartScreen behavior on the OV-signed MSI was not specifically reported; presumed clean given "no issues" but should be captured with a screenshot during M0 D6.
- This closes M0A. Domain-joined / GPO-restricted / Intune-managed / multi-user scenarios are M0 D4 work, not M0A.
- Visual Action Center pixel-check by Diana is implicitly closed by the lab-machine reboot test (toast rendered, user saw it, reported "no issues") but no screenshot was filed in EVIDENCE for this milestone.

## Status

M0A: COMPLETE. Next milestone: M0 (Foundation & Deployment Validation), starting with M0 D2 (MSIX package signed and installable on Win10 1809+ / Win11) since MSI + scheduled-task work is downstream of having Store and Intune LOB unblocked.
