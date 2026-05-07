# ToastRevival - Current Status

Last updated: 2026-05-07

## Project State

ToastRevival is in M0A. The first local Windows agent spike exists and can send an unpackaged local Windows App SDK app notification from this development machine.

The GitHub repository at `https://github.com/keithrlucier/toast` now has the initial planning baseline on `main`.

## Completed

- Code signing certificate has been renewed.
- Initial planning documents exist under `Docs/ToastRevival`.
- Repository baseline was created and pushed to GitHub.
- .NET SDK `8.0.420` was installed and pinned with `global.json`.
- `ToastRevival.sln` was created.
- `src/ToastRevival.Agent` was created.
- The first unpackaged local notification spike built successfully.
- `.\scripts\run-agent-spike.ps1 -WaitSeconds 5` ran successfully and reported that the M0A notification was sent.
- Framework-dependent and self-contained Release publish artifacts were produced under `artifacts/ToastRevival.Agent`.
- Published executables ran successfully and captured the Acknowledge button activation callback.

## Not Yet Completed

- No backend API project exists yet.
- No admin dashboard project exists yet.
- No MSIX/MSI package has been produced yet.
- No package has been signed with the renewed certificate yet.
- No Store, Intune, RMM, or clean-machine install validation has been run.
- No automated tests exist yet.

## Local Environment Notes

- Git is installed.
- .NET SDK `8.0.420` and `10.0.203` are installed.
- The repo is pinned to .NET SDK `8.0.420`.
- NuGet had no package sources configured globally, so the repo includes `NuGet.config` for nuget.org.
- Windows App SDK CLI builds required `<EnableMsixTooling>true</EnableMsixTooling>` in the agent project.
- `signtool.exe` and `makeappx.exe` are not currently on PATH.
- No code-signing certificate with a private key was visible in `Cert:\CurrentUser\My` or `Cert:\LocalMachine\My`.
- The renewed code-signing certificate is token-backed, so signing will likely require the vendor middleware/provider and an interactive PIN/signing flow unless the token supports unattended signing policy.
- Publish artifact sizes: framework-dependent is about 35.83 MB; self-contained is about 160.62 MB.

## Product Toast Target

The current M0A notification is intentionally plain. The product target is rich, curated Windows app notifications with template-specific content, hero images, app logo overrides, action buttons, scenario/audio choices, and eventually structured inputs where they are useful.

The next notification spike should exercise this richer payload surface before backend work begins.

## Immediate Goal

Continue `M0A - Signed Toast Agent Spike`:

1. Add a rich local notification spike with hero image, logo, action buttons, and audio.
2. Decide the first package path: MSIX first, MSI first, or both in sequence.
3. Install/locate signing and packaging tools.
4. Make the token-backed renewed certificate available to Windows signing tools.
5. Package and sign the agent.
6. Install on a clean Windows machine and confirm toast behavior after login/reboot.
