# M0A Local Agent Spike - 2026-05-07

## Purpose

Prove the smallest local Windows endpoint path before backend, dashboard, packaging, or signing work.

## Initial Implementation

- Created `ToastRevival.sln`.
- Created `src/ToastRevival.Agent`.
- Pinned the repo to .NET SDK `8.0.420` with `global.json`.
- Targeted `net8.0-windows10.0.19041.0`.
- Added `Microsoft.WindowsAppSDK` package version `1.7.250310001`.
- Used `Microsoft.Windows.AppNotifications.AppNotificationManager` for the first local app notification spike.
- Added `<EnableMsixTooling>true</EnableMsixTooling>` because Windows App SDK CLI builds failed without the MSIX tooling workaround.

## Run Command

```powershell
.\scripts\run-agent-spike.ps1 -WaitSeconds 15
```

## Verification

```powershell
dotnet restore ToastRevival.sln
dotnet build ToastRevival.sln --no-restore
.\scripts\run-agent-spike.ps1 -WaitSeconds 5
dotnet publish src\ToastRevival.Agent\ToastRevival.Agent.csproj -c Release -r win-x64 --self-contained false -o artifacts\ToastRevival.Agent\win-x64-framework-dependent
.\artifacts\ToastRevival.Agent\win-x64-framework-dependent\ToastRevival.Agent.exe --wait 5
dotnet publish src\ToastRevival.Agent\ToastRevival.Agent.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o artifacts\ToastRevival.Agent\win-x64-self-contained
.\artifacts\ToastRevival.Agent\win-x64-self-contained\ToastRevival.Agent.exe --wait 5
```

Results:

- Restore passed after adding repo-local `NuGet.config`.
- Build passed with 0 warnings and 0 errors.
- Runtime command passed and reported `ToastRevival M0A notification sent.`
- Framework-dependent publish passed.
- Framework-dependent published exe passed and captured `Notification activated: action=acknowledge;source=m0a`.
- Self-contained publish passed.
- Self-contained published exe passed and captured `Notification activated: action=acknowledge;source=m0a`.
- Framework-dependent artifact: 32 files, about 35.83 MB.
- Self-contained artifact: 448 files, about 160.62 MB.

## Current Blockers For Signing/Packaging

- `signtool.exe` is not on PATH.
- `makeappx.exe` is not on PATH.
- No renewed code-signing certificate with private key was visible in the current user or local machine certificate stores.

## Notes

- This is intentionally unpackaged for the first local proof.
- This is intentionally anemic: it proves local notification registration, display, publish, and activation callback handling only.
- The intended product notification surface is richer: hero images, app logo override, action buttons, scenario/audio choices, and later inputs/dropdowns where useful.
- Packaging, signing, Store submission, Intune deployment, and RMM deployment are still future M0A/M0 steps.
- The renewed code-signing certificate is token-backed, so the signing path must account for token middleware/provider and PIN/interactive signing behavior.
- Microsoft documents that app notifications are not supported for elevated/admin processes, so this spike should be run unelevated.
- This evidence does not yet prove package install behavior, signing, reboot/login behavior, or deployment channel behavior.
