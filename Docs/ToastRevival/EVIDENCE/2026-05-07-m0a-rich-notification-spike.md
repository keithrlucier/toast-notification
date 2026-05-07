# M0A Rich Notification Spike - 2026-05-07

## Purpose

Extend the M0A local Windows agent spike to exercise the rich Windows toast surface area documented in `Docs/ToastRevival/DESIGN-SPEC.md`: hero image, app logo override, multiple action buttons, scenario flag (urgent / reminder / default), and `ms-winsoundevent` audio. This closes the "rich payload surface" gap called out in `STATUS.md` before any packaging, signing, or backend work begins.

## Scope (this session only)

- Local-only. No build server changes. No code-signing certificate work. No backend API. No deploy.
- Six template definitions (Announcement, Alert, Action Required, Reminder, Celebration, Maintenance) implemented in code, each wired to the correct Windows scenario and predefined audio.
- One legacy "plain" template retained so the original 2-line text + single Acknowledge button payload still ships.
- Branded toast assets generated procedurally and committed to source. Real curated assets are a Diana deliverable for M4 templates - this spike intentionally uses generated brand-tone placeholders.

## Files Added or Changed

- `src/ToastRevival.Agent/Assets/toast-hero.png` (364x180, teal gradient + ToastRevival wordmark)
- `src/ToastRevival.Agent/Assets/toast-logo.png` (48x48, brand teal square + T glyph)
- `src/ToastRevival.Agent/Assets/toast-inline.png` (200x120, dark panel placeholder, not yet used by any template)
- `src/ToastRevival.Agent/ToastTemplates.cs` (new - template catalog, builder, asset abstraction)
- `src/ToastRevival.Agent/Program.cs` (refactored - parses `--template`, dispatches to `ToastTemplateBuilder`)
- `src/ToastRevival.Agent/ToastRevival.Agent.csproj` (added `Content` item to copy `Assets\*.png` to bin / publish, and `RootNamespace`)
- `scripts/generate-toast-assets.ps1` (new - regenerate the three brand placeholder PNGs)
- `scripts/run-agent-spike.ps1` (added `-Template` parameter)

## Template Catalog

| Template       | Scenario  | Sound       | Hero | Logo override | Buttons                          |
|---             |---        |---          |---   |---            |---                               |
| `plain`        | Default   | (none)      | no   | no            | Acknowledge                      |
| `announcement` | Default   | Default     | yes  | yes           | View details                     |
| `alert`        | Urgent    | Alarm       | yes  | yes           | Acknowledge / Report to IT       |
| `action`       | Default   | Reminder    | no   | yes           | Reset now / Remind later         |
| `reminder`     | Reminder  | Reminder    | no   | yes           | Got it                           |
| `celebration`  | Default   | Default     | yes  | yes           | Thanks                           |
| `maintenance`  | Default   | Default     | no   | yes           | Details / Acknowledge            |

Scenario and sound choices follow `Docs/ToastRevival/DESIGN-SPEC.md`. The Alert template uses `AppNotificationScenario.Urgent` so it can break through Do Not Disturb on supported builds, and `AppNotificationSoundEvent.Alarm` for the looping alarm tone.

## Run Commands

```powershell
# Single template, dev loop
.\scripts\run-agent-spike.ps1 -Template alert -WaitSeconds 5

# Smoke test all templates from a published exe
$exe = 'artifacts\ToastRevival.Agent\win-x64-framework-dependent\ToastRevival.Agent.exe'
foreach ($t in 'plain','announcement','alert','action','reminder','celebration','maintenance') {
    & $exe --template $t --no-wait
}
```

## Verification (2026-05-07)

- `dotnet build ToastRevival.sln`: 0 warnings, 0 errors.
- `dotnet run` for all 7 templates (`plain`, `announcement`, `alert`, `action`, `reminder`, `celebration`, `maintenance`): each reported `ToastRevival M0A notification sent` with the expected scenario, sound, and button count.
- One late activation callback was observed during the smoke loop: `Notification activated: action=acknowledge;source=m0a;template=Plain` printed at the start of the next run. Expected behaviour - the activation handler bridges across processes via COM, and the previous template's Acknowledge click was delivered into the next process instance.
- `dotnet publish` framework-dependent: 35 files, 35.86 MB. `Assets/` folder (3 PNGs, 15.6 KB) shipped beside the exe.
- `dotnet publish` self-contained (`-p:WindowsAppSDKSelfContained=true`): 451 files, 160.65 MB. Same `Assets/` folder shipped.
- Published `ToastRevival.Agent.exe --template alert --no-wait` ran outside `dotnet run` and reported the expected scenario/sound/button count.

## Boundaries

- This confirms rich notification rendering via the Windows App SDK from an unpackaged process. Visual confirmation in Action Center requires a person at the screen - the spike deliberately fires `--no-wait` so all 7 templates queue up for review.
- This does not yet prove packaged install behaviour, signing, reboot or login persistence, Store submission, Intune deployment, or RMM deployment.
- Hero image, logo, and inline assets are generated brand placeholders. They are NOT the curated images Diana will spec for M4. Replacing them later is a per-template content swap, not a code change.
- Audio is limited to the Windows predefined `ms-winsoundevent:*` set via `AppNotificationBuilder.SetAudioEvent`. Custom audio URIs are not exercised in this spike.
- Inline image, text input, and selection input controls are not exercised in this spike. The asset is staged for a future template iteration.
- Microsoft documents that app notifications are not supported for elevated/admin processes - the spike refuses to run elevated.

## Notes For Next Session

- M0A still owes packaging (MSIX or MSI), signing with the renewed token-backed OV cert, and clean-machine install with reboot/login persistence verification.
- Codex is still finishing the build server (Visual Studio Build Tools, Windows SDK with `signtool.exe` / `makeappx.exe`, GitHub Actions runner). Local signing will likely move first because the dev workstation can host the hardware token directly.
- The `Inline` asset is committed but unused. When templates need an inline image (e.g., a hero variant under the body lines), wire `AppNotificationBuilder.SetInlineImage` and surface it through `ToastTemplate.UseInlineImage` mirror of the existing flags.
