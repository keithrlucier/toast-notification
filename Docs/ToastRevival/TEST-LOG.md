# ToastRevival - Test Log

## 2026-05-07

### Environment Checks

- `git --version`: passed.
- `dotnet --info`: initially showed SDK `10.0.203` only.
- Installed .NET SDK `8.0.420` with `winget`.
- `dotnet --list-sdks`: passed with `8.0.420` and `10.0.203`.
- `dotnet nuget list source`: initially showed no sources.
- Added repo-local `NuGet.config` for `https://api.nuget.org/v3/index.json`.

### Build Checks

- `dotnet restore ToastRevival.sln`: passed after adding `NuGet.config`.
- `dotnet build ToastRevival.sln --no-restore`: initially failed because Windows App SDK CLI build could not load `Microsoft.Build.Packaging.Pri.Tasks.ExpandPriContent`.
- Added `<EnableMsixTooling>true</EnableMsixTooling>` to `src/ToastRevival.Agent/ToastRevival.Agent.csproj`.
- `dotnet build ToastRevival.sln --no-restore`: passed with 0 warnings and 0 errors.

### Runtime Checks

- `.\scripts\run-agent-spike.ps1 -WaitSeconds 5`: passed.
- Console output reported `ToastRevival M0A notification sent.`
- `dotnet publish src\ToastRevival.Agent\ToastRevival.Agent.csproj -c Release -r win-x64 --self-contained false -o artifacts\ToastRevival.Agent\win-x64-framework-dependent`: passed.
- `.\artifacts\ToastRevival.Agent\win-x64-framework-dependent\ToastRevival.Agent.exe --wait 5`: passed and captured `Notification activated: action=acknowledge;source=m0a`.
- `dotnet publish src\ToastRevival.Agent\ToastRevival.Agent.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o artifacts\ToastRevival.Agent\win-x64-self-contained`: passed.
- `.\artifacts\ToastRevival.Agent\win-x64-self-contained\ToastRevival.Agent.exe --wait 5`: passed and captured `Notification activated: action=acknowledge;source=m0a`.

### Artifact Checks

- Framework-dependent artifact: 32 files, about 35.83 MB.
- Self-contained artifact: 448 files, about 160.62 MB.

### Signing/Packaging Tool Checks

- `signtool.exe`: not found on PATH.
- `makeappx.exe`: not found on PATH.
- Code-signing cert check: no matching cert with private key visible in `Cert:\CurrentUser\My` or `Cert:\LocalMachine\My`.

### Boundaries

- This confirms local unpackaged notification API execution, published executable execution, and notification activation callback handling from the development machine.
- This does not yet confirm packaging, signing, reboot survival, Store submission, Intune deployment, RMM deployment, or clean-machine behavior.
