# M0 D2 MSIX Publisher Fix - 0x80091005 on first sign attempt

**Date:** 2026-05-07
**Milestone:** M0 D2 - MSIX package signed with OV cert
**Status:** Root cause identified, manifest corrected, MSIX rebuilt as 0.2.0.1, awaiting re-sign

---

## What Happened

Keith opened the unsigned `ToastNotification.Agent-0.2.0.0.msix` in the DigiCert Certificate Utility and clicked Sign. The cert chain validated cleanly (Sectigo R46 -> R36 -> Toast2IT, LLC, "This certificate is OK"). The sign attempt then failed with:

```
The file C:\SOURCE\toast\artifacts\installer\msix\ToastNotification.Agent-0.2.0.0.msix could not be signed (0x80091005).
```

`0x80091005` = `NTE_BAD_LEN`. In MSIX signing it is almost always a **Publisher-vs-cert-subject DN mismatch**. signtool / SignerSign refuses to attach an Authenticode signature to an MSIX whose `Package.Identity.Publisher` does not match the cert's Subject distinguished name byte-for-byte after canonicalization.

## Root Cause

The Sectigo OV cert subject (read from the DigiCert Utility, Details tab, Subject field) is:

```
CN = Toast2IT, LLC
O  = Toast2IT, LLC
S  = Florida
C  = US
```

Our manifest had only **three** RDNs:

```xml
Publisher="CN=&quot;Toast2IT, LLC&quot;, S=Florida, C=US"
```

The `O=Toast2IT, LLC` (Organization) RDN was **missing**. Because the cert is OV (Organization Validated), the O field IS present in the cert subject. MSIX signing requires every RDN from the cert subject to appear in the manifest Publisher, in the same order, with the same quoting.

Why we missed it: the team's prior memory of the cert subject came from `Get-AuthenticodeSignature` output on the M0A signed MSI, which the team transcribed as `CN="Toast2IT, LLC", S=Florida, C=US`. That transcription was truncated - the actual cert subject has the O field. The MSI sign succeeded with the truncated transcription because **MSI signing does not enforce a Publisher-vs-cert match**. MSIX does.

## Fix

`src/ToastRevival.Agent/Package.appxmanifest` updated:

```xml
<Identity
    Name="Toast2IT.ToastNotification.Agent"
    Publisher="CN=&quot;Toast2IT, LLC&quot;, O=&quot;Toast2IT, LLC&quot;, S=Florida, C=US"
    Version="0.2.0.1"
    ProcessorArchitecture="x64" />
```

Rebuilt: `artifacts/installer/msix/ToastNotification.Agent-0.2.0.1.msix` (63.53 MB, UNSIGNED).

Manifest extracted from new .msix and verified:
```
Name      : Toast2IT.ToastNotification.Agent
Publisher : CN="Toast2IT, LLC", O="Toast2IT, LLC", S=Florida, C=US
Version   : 0.2.0.1
```

Old artifacts deleted to prevent confusion:
- `artifacts/installer/msix/ToastNotification.Agent-0.2.0.0.msix` (old, wrong Publisher)
- `artifacts/installer/msix/ToastRevival.Agent_0.2.0.0_x64_Test/` (associated dev-cert helper)

The 0.2.0.0 build is gone from disk. Only the corrected 0.2.0.1 .msix and its 0.2.0.1 _Test folder remain.

## Standing Rule (now documented in CONTEXT / project context / Carl's persona)

**MSIX `Package.Identity.Publisher` must contain EVERY RDN from the code-signing cert's Subject, in the cert's order, with the cert's exact quoting.** Reading the cert via `Get-AuthenticodeSignature` on a previously-signed MSI is **NOT** authoritative - some display contexts truncate the subject. The authoritative reference for an OV cert subject is the cert utility (DigiCert Utility -> Details tab -> Subject) OR running:

```powershell
Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My |
  Where-Object { $_.Subject -like "*<company>*" } |
  Select-Object Subject
```

(Token-issued certs may not appear in the standard cert stores until the SafeNet client unlocks the token; in that case the cert utility's Details tab is the source of truth.)

## Code Sweep Step 4 Addition (Abish)

When auditing any change to `Package.appxmanifest` Publisher attribute or any property that propagates into manifest Publisher:

1. Read the cert subject from the cert utility / Details tab (or `Get-ChildItem Cert:\...My`).
2. Enumerate every RDN: typically CN, OU, O, L, S, C.
3. Verify each RDN appears in the manifest Publisher in the same order, with the same quoting (any value containing a comma needs `&quot;...&quot;` quotes).
4. Build the .msix.
5. Extract `AppxManifest.xml` from the produced .msix and re-verify the Publisher line - the build pipeline can normalize whitespace and quoting in unexpected ways.
6. If any of (1)-(5) disagree, do NOT hand off for signing.

## Updated build-msix.ps1 Reminder

The post-build reminder text in `scripts/build-msix.ps1` was updated to spell out all four RDNs and the failure modes for both DigiCert Utility (0x80091005) and signtool (0x800B0109) so the next person to read it sees the lesson.

---

## Next Step (Keith)

Sign `artifacts/installer/msix/ToastNotification.Agent-0.2.0.1.msix` via the DigiCert Certificate Utility (same flow you tried). With the Publisher now matching the cert subject across all four RDNs, the sign should succeed. If 0x80091005 recurs, paste the cert utility "Subject" field text again and we re-verify - but the manifest Publisher in 0.2.0.1 is verified to match what the Details tab showed.
