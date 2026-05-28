# Toast Notification — Ops scripts

Reusable, idempotent scripts for one-time and recurring local-machine setup.
Production deploy lives elsewhere; this is for the developer / release-cut box.

## setup-git-credentials.ps1

Locks in a git credential configuration that survives both pitfalls of the
default Windows Git install:

1. Multi-helper chain falling through to the GUI Credential Manager dialog.
2. Single-credential-per-host overwriting, which silently breaks when two
   repos under `github.com/keithrlucier/` need distinct PATs.

Run after rotating a Toast PAT, on a fresh dev box, or any time git prompts
have started reappearing:

```powershell
# PATs are SecureString — paste each when prompted; nothing reaches shell history,
# Start-Transcript output, or process-line audit logs.
.\infrastructure\ops\setup-git-credentials.ps1 `
    -PrivatePat (Read-Host 'Private PAT' -AsSecureString) `
    -PublicPat  (Read-Host 'Public PAT'  -AsSecureString)
```

Open a new terminal after running so the env-var changes (`GIT_TERMINAL_PROMPT=0`,
`GCM_INTERACTIVE=Never`) take effect.

What it sets:

| Setting | Value | Why |
|---|---|---|
| `credential.helper` | `store` (chain reset) | File-only, no GUI prompts |
| `credential.useHttpPath` | `true` | Per-path credentials → two PATs coexist |
| `GIT_TERMINAL_PROMPT` (user env) | `0` | Block stdin prompt fallback |
| `GCM_INTERACTIVE` (user env) | `Never` | Block GUI manager fallback |
| `~/.git-credentials` | 4 entries | toast + toast-notification, with and without `.git` |

PATs are never committed — `.git-credentials` lives under `$env:USERPROFILE`
and is ignored by every git scope.
