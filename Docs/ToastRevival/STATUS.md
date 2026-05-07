# ToastRevival - Current Status

Last updated: 2026-05-07

## Project State

ToastRevival is in M0A. The first local Windows agent spike exists and can send an unpackaged local Windows App SDK notification from the development machine. The AWS Windows build server candidate is reachable and partially provisioned.

Codex is also working on this project. Coordinate before running commands on the server during active installer windows.

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
- AWS Windows Server reachable at `52.21.249.120` (by Codex, 2026-05-07).
- Key-based SSH configured from this workstation.
- .NET SDK `8.0.420` installed on the server.
- Git `2.53.0.windows.2` installed on the server.
- Repo cloned to `C:\toast` on the server.

## Not Yet Completed

- No backend API project exists yet.
- No admin dashboard project exists yet.
- No MSIX/MSI package has been produced yet.
- No package has been signed with the renewed certificate yet.
- No Store, Intune, RMM, or clean-machine install validation has been run.
- Rich notification spike (hero image, logo, action buttons, audio) not yet built.
- Server-side Visual Studio Build Tools and Windows SDK install not yet verified complete.
- Server-side `signtool.exe` and `makeappx.exe` not yet available.
- GitHub Actions self-hosted runner not yet installed.
- No automated tests exist yet.

## Server (52.21.249.120)

See `CONTEXT.md` -> Server Infrastructure for full details.

- Windows Server 2022 Datacenter, hostname `EC2AMAZ-A5EU435`.
- SSH port 22 and RDP port 3389 reachable.
- The Administrator password was pasted into chat and should be rotated.
- Key-based SSH configured from this workstation.
- .NET SDK `8.0.420` and Git `2.53.0.windows.2` installed.
- Repo cloned to `C:\toast`, remote: `https://github.com/keithrlucier/toast`.
- Visual Studio Build Tools/Windows SDK installation still needs verification.
- `signtool.exe` and `makeappx.exe` are not available yet.
- WinRM blocked; use SSH.
- PATH is not reliable in SSH sessions; use full binary paths.

## Local Environment Notes

- Git is installed.
- .NET SDK `8.0.420` and `10.0.203` are installed.
- The repo is pinned to .NET SDK `8.0.420`.
- NuGet had no package sources configured globally, so the repo includes `NuGet.config` for nuget.org.
- Windows App SDK CLI builds required `<EnableMsixTooling>true</EnableMsixTooling>` in the agent project.
- `signtool.exe` and `makeappx.exe` are not currently on PATH locally.
- No code-signing certificate with a private key was visible in `Cert:\CurrentUser\My` or `Cert:\LocalMachine\My`.
- The renewed code-signing certificate is token-backed, so signing will likely require the vendor middleware/provider and an interactive PIN/signing flow unless the token supports unattended signing policy.
- Publish artifact sizes: framework-dependent is about 35.83 MB; self-contained is about 160.62 MB.

## Product Toast Target

The current M0A notification is intentionally plain. The product target is rich, curated Windows app notifications with template-specific content, hero images, app logo overrides, action buttons, scenario/audio choices, and eventually structured inputs where they are useful.

The next notification spike should exercise this richer payload surface before backend work begins.

## Immediate Goal

Continue `M0A - Signed Toast Agent Spike`:

1. Add a rich local notification spike with hero image, logo, action buttons, and audio.
2. Resolve/verify the server-side Visual Studio Build Tools and Windows SDK install.
3. Install/locate signing and packaging tools.
4. Add GitHub Actions workflow for unsigned Windows agent artifacts.
5. Install and register the GitHub Actions self-hosted runner on the Windows server.
6. Decide the first package path: MSIX first, MSI first, or both in sequence.
7. Make the token-backed renewed certificate available to Windows signing tools locally.
8. Package and sign the agent.
9. Install on a clean Windows machine and confirm toast behavior after login/reboot.
