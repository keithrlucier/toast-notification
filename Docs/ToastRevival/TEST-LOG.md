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

## 2026-05-07 (rich notification spike)

### Asset Generation

- `.\scripts\generate-toast-assets.ps1`: passed. Produced `src/ToastRevival.Agent/Assets/toast-hero.png` (364x180, 11,549 bytes), `toast-logo.png` (48x48, 296 bytes), `toast-inline.png` (200x120, 3,872 bytes).
- Image dimensions verified via `System.Drawing.Image.FromFile`.

### Build Checks

- `dotnet build C:\SOURCE\toast\ToastRevival.sln`: passed with 0 warnings and 0 errors after the `ToastTemplates.cs` add and the `Program.cs` refactor.

### Runtime Checks (`dotnet run`)

- `--template plain`: passed - `Scenario: Default, Sound: (none), Buttons: 1`.
- `--template announcement`: passed - `Scenario: Default, Sound: Default, Buttons: 1`.
- `--template alert`: passed - `Scenario: Urgent, Sound: Alarm, Buttons: 2`.
- `--template action`: passed - `Scenario: Default, Sound: Reminder, Buttons: 2`. Captured a late activation callback `action=acknowledge;source=m0a;template=Plain` from the prior `plain` run, which is the expected COM activation bridge behaviour.
- `--template reminder`: passed - `Scenario: Reminder, Sound: Reminder, Buttons: 1`.
- `--template celebration`: passed - `Scenario: Default, Sound: Default, Buttons: 1`.
- `--template maintenance`: passed - `Scenario: Default, Sound: Default, Buttons: 2`.

### Publish Checks

- `dotnet publish` framework-dependent: passed. Output `artifacts/ToastRevival.Agent/win-x64-framework-dependent`, 35 files, 35.86 MB. `Assets/` folder with all three PNGs shipped.
- `dotnet publish` self-contained (`-p:WindowsAppSDKSelfContained=true`): passed. Output `artifacts/ToastRevival.Agent/win-x64-self-contained`, 451 files, 160.65 MB. `Assets/` folder shipped.
- Published exe smoke test: `ToastRevival.Agent.exe --template alert --no-wait` reported the expected scenario, sound, and button count.

### Boundaries

- Templates were verified at the API level (build, send, output reporting). Visual rendering in Action Center has not been pixel-checked by Diana - that is a manual review pending Keith pulling open Action Center on the dev workstation.
- No packaged install was run. No signing was attempted. No reboot/login persistence was tested.
- `signtool.exe` and `makeappx.exe` are still not on PATH locally; the renewed token-backed OV cert is still not available to Windows signing tools.
