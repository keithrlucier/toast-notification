# Build Server Bootstrap - 2026-05-07

## Purpose

Prepare a Windows server to build unsigned ToastRevival Windows artifacts that can later be downloaded and signed locally with the hardware-token code-signing certificate.

## Server

- Host: `52.21.249.120`
- Hostname: `EC2AMAZ-A5EU435`
- OS: Windows Server 2022 Datacenter
- SSH: reachable on port 22
- RDP: reachable on port 3389
- WinRM: ports 5985/5986 were not reachable

## Security Notes

- The Administrator password was pasted into chat and should be rotated.
- The password is not documented in this repository.
- Key-based SSH was configured for this workstation using `C:\Users\keith\.ssh\toast-lightsail-build`.

## Verified Completed

- OpenSSH key login works as `Administrator`.
- .NET SDK `8.0.420` is installed.
- Git `2.53.0.windows.2` is installed.
- Repository is cloned at `C:\toast`.
- `C:\toast` is clean and tracks `origin/main`.

## Not Verified Complete

- Visual Studio Build Tools installation was still running during the last server check.
- `vswhere` returned an empty product list.
- `signtool.exe` was not found.
- `makeappx.exe` was not found.
- GitHub Actions self-hosted runner is not installed.

## Last Verification Commands

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' --list-sdks
& 'C:\Program Files\Git\cmd\git.exe' --version
& 'C:\Program Files\Git\cmd\git.exe' -C C:\toast status --short --branch
```

## Next Steps

1. Check whether the Visual Studio Build Tools installer has finished.
2. Verify Windows SDK tools are present, especially `signtool.exe` and `makeappx.exe`.
3. Run the repo build on the server.
4. Add GitHub Actions workflow for unsigned Windows agent artifacts.
5. Install and register a GitHub Actions self-hosted runner.
