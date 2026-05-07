# ToastRevival - Current Status

Last updated: 2026-05-07

## Project State

ToastRevival is at Pre-M0. No product code has been started in this local workspace or in the GitHub repository.

The GitHub repository at `https://github.com/keithrlucier/toast` exists, but it was empty before this repository baseline was created.

## Completed

- Code signing certificate has been renewed.
- Initial planning documents exist under `Docs/ToastRevival`.

## Not Yet Completed

- No Windows agent project exists yet.
- No backend API project exists yet.
- No admin dashboard project exists yet.
- No packaging, signing, Store, Intune, RMM, or clean-machine install validation has been run.
- No product tests have been run.

## Local Environment Notes

- Git is installed.
- .NET 8 runtime is installed.
- .NET SDK is not installed on this machine yet, so project scaffolding and builds are currently blocked.

## Immediate Goal

Start with `M0A - Signed Toast Agent Spike`:

1. Install the .NET SDK and required Windows App SDK tooling.
2. Create the smallest possible Windows agent.
3. Show one hardcoded real Windows toast.
4. Package and sign the agent.
5. Install on a clean Windows machine and confirm toast behavior after login/reboot.
