# GitHub Actions Runner Setup - 2026-05-07

## Server

- Host: `52.21.249.120`
- Hostname: `EC2AMAZ-A5EU435`
- Login used for bootstrap: SSH as `Administrator`
- Repo clone on server: `C:\toast`
- Runner root: `C:\actions-runner-toast`

## Installed / Verified

- .NET SDK: `8.0.420` at `C:\Program Files\dotnet\dotnet.exe`
- Git: `2.53.0.windows.2` at `C:\Program Files\Git\cmd\git.exe`
- Visual Studio Build Tools 2022:
  - Path: `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools`
  - Version: `17.14.37216.2`
  - `vswhere -latest -products *` reported `isComplete: 1`, `isLaunchable: 1`, `isRebootRequired: 0`
- Windows SDK tools:
  - `signtool.exe`: `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe`
  - `makeappx.exe`: `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe`
  - SDK/tool paths were added to both machine PATH and Administrator user PATH.
- WiX:
  - Tool: `C:\Users\Administrator\.dotnet\tools\wix.exe`
  - Version: `5.0.2+aa65968c`
  - Installed as a .NET global tool under the Administrator profile.

## Runner

- Repository scope: `keithrlucier/toast`
- Runner name: `EC2AMAZ-A5EU435-toast-build`
- Labels reported by GitHub API: `self-hosted`, `Windows`, `X64`, `toast-build`
- Service name: `actions.runner.keithrlucier-toast.EC2AMAZ-A5EU435-toast-build`
- Service path: `C:\actions-runner-toast\bin\RunnerService.exe`
- Service account: `.\Administrator`
- Service state after final verification: `Running`
- GitHub API state after final verification: `online`, `busy=False`

The runner was first registered as the default `NT AUTHORITY\NETWORK SERVICE` account. That failed the MSI workflow because `scripts\build-msi.ps1` resolves WiX through `$env:USERPROFILE\.dotnet\tools\wix.exe`, which pointed at `C:\Windows\ServiceProfiles\NetworkService\.dotnet\tools\wix.exe`. The runner was removed and re-registered as `.\Administrator`, matching the required WiX global-tool install profile.

## Workflow

- File added: `.github/workflows/agent-build.yml`
- Commit pushed to `main`: `9363764` (`Add unsigned MSI build workflow`)
- Triggers: `push` to `main`, `workflow_dispatch`
- Runner selector: `[self-hosted, windows, x64, toast-build]`
- Build version expression: `0.2.${{ github.run_number }}.0`
- Artifact:
  - Name: `unsigned-msi`
  - Path: `artifacts/installer/*.msi`
  - Retention: `30` days

## Run Evidence

- Successful manual dispatch run: `#4`
- Run URL: `https://github.com/keithrlucier/toast/actions/runs/25520928311`
- Run status: `completed`
- Conclusion: `success`
- Job: `Build unsigned MSI`
- Artifact: `unsigned-msi`
- Artifact ID: `6866279823`
- Artifact size reported by GitHub: `52772966` bytes
- Artifact expiration: `2026-06-06T20:59:45Z`

Downloaded MSI from the run artifact:

- MSI filename: `ToastNotification.Agent-0.2.4.0.msi`
- MSI size after extraction: `53056831` bytes
- `Property.ProductName`: `Toast Notification Agent`
- `Property.Manufacturer`: `Toast2IT, LLC`
- `Property.ProductVersion`: `0.2.4.0`

## Earlier Runs

- Run `#1` was the push-triggered run for the workflow commit. It failed in `Build unsigned MSI` because the runner service account was `NETWORK SERVICE` and WiX was installed under the Administrator profile.
- Run `#2` was an initial `workflow_dispatch` run queued behind run `#1`; it was cancelled after the service-account issue was identified.
- Run `#3` was another push-triggered run created while the runner was still being corrected; it was cancelled before doing useful work.

## Gaps / Follow-up

- The successful run took about 19 minutes from job start to completion. The longest steps were the normal build/publish/MSI path, not signing. If the acceptance target remains strict at about 10 minutes, the next session should profile `scripts\build-msi.ps1` output and decide whether caching or packaging changes are acceptable. The MSI build script itself was not changed in this setup.
- No automated signing was configured. The artifact is unsigned by design for local Thales-token signing.
