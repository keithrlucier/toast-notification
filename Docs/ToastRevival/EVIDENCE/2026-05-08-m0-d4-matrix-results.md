# Evidence: M0 D4 Matrix Results

**Date:** 2026-05-08
**Milestone:** M0 D4
**Tested by:** Keith Lucier
**Build under test:** `ToastNotification.Agent-0.3.1.0.msi` (signed), `ToastNotification.Agent-0.2.1.0.msix` (signed)

---

## Test 1 — Uninstall Idempotency

**Pre-condition:** 0.3.0.0 or 0.3.1.0 MSI installed, scheduled task State=Ready.

**Step A: Normal uninstall**

```powershell
# Verify task before
.\scripts\verify-d4-matrix.ps1 -Phase PostInstall

# Uninstall
$productCode = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' |
    Where-Object { $_.DisplayName -like '*Toast Notification Agent*' }).PSChildName
msiexec /x $productCode /qn /l*v artifacts\installer\uninstall-a.log

# Verify task gone
.\scripts\verify-d4-matrix.ps1 -Phase PostUninstall
```

**Result:**
```
[Keith fills in]
```

**msiexec exit code:** `[Keith fills in — 0 expected]`

---

**Step B: Idempotency — manual task deletion then uninstall**

```powershell
# Re-install 0.3.1.0 first
msiexec /i artifacts\installer\ToastNotification.Agent-0.3.1.0.msi /qn /l*v artifacts\installer\install-b.log

# Manually delete the task
schtasks /Delete /TN "\Toast2IT\ToastNotificationAgentLogon" /F

# Uninstall with task already gone
msiexec /x $productCode /qn /l*v artifacts\installer\uninstall-b.log
```

**msiexec exit code:** `[Keith fills in — 0 expected; Return="ignore" on UninstallScheduledTask protects removal]`

**Result:**
```
[Keith fills in]
```

---

## Test 2 — Major Upgrade: 0.3.0.0 → 0.3.1.0

**Pre-condition:** 0.3.0.0 installed (the D3 lab build).

```powershell
# Baseline — task should be Ready from D3 install
.\scripts\verify-d4-matrix.ps1 -Phase PostInstall

# Install 0.3.1.0 on top (MajorUpgrade fires)
msiexec /i artifacts\installer\ToastNotification.Agent-0.3.1.0.msi /qn /l*v artifacts\installer\upgrade-0.3.1.0.log

# Verify task re-created after upgrade
.\scripts\verify-d4-matrix.ps1 -Phase PostInstall
```

**Result — task state after upgrade:**
```
[Keith fills in — expect State=Ready, same principal, same action]
```

**Duplicate task check:**
```powershell
Get-ScheduledTask -TaskPath '\Toast2IT\' | Format-Table TaskName, State
```
```
[Keith fills in — expect exactly ONE entry: ToastNotificationAgentLogon]
```

**Installed version after upgrade:**
```
[Keith fills in — should show 0.3.1.0, NOT 0.3.0.0]
```

**Log out / back in — toast fires after upgrade:**
```
[Keith fills in — Yes / No]
```

---

## Test 3 — Multi-User

**Pre-condition:** 0.3.1.0 installed.

**Scheduled task principal check:**
```powershell
.\scripts\verify-d4-matrix.ps1 -Phase MultiUser
```

**Result:**
```
[Keith fills in]
```

**Second local user test:**

```cmd
net user TestUser2 Password123! /add
```

Log out, log in as TestUser2.

**Toast fired for TestUser2:** `[Yes / No]`
**Toast content:** `[Keith describes what appeared]`

---

## Test 4 — GPO: Turn Off App Notifications

**Pre-condition:** 0.3.1.0 installed.

```powershell
# Simulate the policy
.\scripts\verify-d4-matrix.ps1 -Phase GPOBlock

# Log out and back in, then verify:
.\scripts\verify-d4-matrix.ps1 -Phase Check
```

**Expected:** Agent task fires (LastRunTime updates), no toast appears.
**Actual:** `[Keith fills in]`

**agent.log check (should show Register()/Show() returned without throwing):**
```
[Keith pastes last 5-10 lines of agent.log from %LOCALAPPDATA%\Toast2IT\Toast Notification\agent.log]
```

**Cleanup:**
```powershell
.\scripts\verify-d4-matrix.ps1 -Phase GPOUnblock
```

---

## Test 5 — Domain/Intune (if infrastructure available)

**Domain-joined, default enterprise GPO baseline:**

Environment description: `[Keith describes domain/AD setup, any AppLocker or SRP policies]`

Install 0.3.1.0 MSI via RMM or manual push on domain-joined machine.

**Scheduled task created:** `[Yes / No]`
**Task fires at logon:** `[Yes / No]`
**Toast renders:** `[Yes / No]`
**Any GPO conflict observed:** `[Keith describes]`

---

**Intune LOB MSI deployment:**

Environment: `[Keith describes Intune tenant, device compliance policies]`

Published MSI as Win32 LOB app in Intune, deployed to device group.

**MSI installed by Intune (SYSTEM context):** `[Yes / No]`
**Scheduled task created:** `[Yes / No]`
**Task fires at logon for enrolled user:** `[Yes / No]`
**Toast renders:** `[Yes / No]`
**Any MDM policy conflict (e.g., Notifications CSP):** `[Keith describes]`

---

## D4 Close Checklist

- [ ] Test 1A (normal uninstall idempotency): PASS
- [ ] Test 1B (manual task deletion → uninstall): PASS
- [ ] Test 2 (major upgrade 0.3.0.0 → 0.3.1.0): PASS
- [ ] Test 3 (multi-user, BUILTIN\Users fires for all): PASS
- [ ] Test 4 (GPO block — agent runs, toast silent): documented behavior confirmed
- [ ] Test 5 (domain/Intune): [PASS / DEFERRED — no lab infra]
- [ ] FIX-MSIX-002 applied and committed
- [ ] MILESTONES.md D4 marked COMPLETE
