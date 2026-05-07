# M0 D2 - MSIX Signed (close)

**Date:** 2026-05-07
**Milestone:** M0 D2 - MSIX package signed with OV cert
**Status:** SIGNED + VERIFIED. Install validation on Win11 lab is the next-and-last step.

---

## What Got Signed

`C:\SOURCE\toast\artifacts\installer\msix\ToastNotification.Agent-0.2.0.1.msix` (63.56 MB after signing; was 63.53 MB unsigned — signature blocks added ~30 KB).

## Signature Verification

`Get-AuthenticodeSignature -LiteralPath ...` reports:

```
Status     : Valid
Signer     : CN="Toast2IT, LLC", O="Toast2IT, LLC", S=Florida, C=US
Issuer     : CN=Sectigo Public Code Signing CA R36, O=Sectigo Limited, C=GB
NotAfter   : 04/15/2027 19:59:59
Thumbprint : 19B07B46712C2D87FF6AA99842F7EF6B036FEDA7
Timestamp  : CN=DigiCert SHA256 RSA4096 Timestamp Responder 2025 1, O="DigiCert, Inc.", C=US
FileSizeMB : 63.56
```

Thumbprint matches the M0A signed MSI. Same cert, same token, same Sectigo OV chain.

## Tooling Reality (lesson)

We discovered three things in sequence during this signing attempt:

1. **DigiCert Certificate Utility 2.3.5.2 does not sign MSIX.** First sign attempt returned `0x80091005` and Keith confirmed via DigiCert's docs/forum. The utility handles classic Authenticode formats only (.exe, .dll, .msi). MSIX requires signtool.exe.
2. **The manifest `Package.Identity.Publisher` had been incomplete.** The OV cert subject is `CN, O, S, C` — four RDNs. Our manifest 0.2.0.0 had only `CN, S, C` because the team's prior memory string was a truncated transcription of `Get-AuthenticodeSignature` output on the M0A signed MSI. MSI signing does NOT enforce a Publisher-vs-cert match (which is why M0A signed fine with the truncated string), but MSIX signing DOES. Manifest corrected to include `O="Toast2IT, LLC"` in version 0.2.0.1.
3. **signtool.exe was not on PATH and not where the dev assumed it was.** The Windows SDK was installed but the "Windows SDK Signing Tools for Desktop Apps" sub-component was missing. signtool ended up living in the NuGet packages cache (`%USERPROFILE%\.nuget\packages\microsoft.windows.sdk.buildtools\<ver>\bin\<ver>\x64\signtool.exe`), shipped transitively by the WinAppSDK 1.7 NuGet that the project already depends on.

These three lessons are now codified in:

- `Docs/ToastRevival/CONTEXT.md` -> "Code Signing (MSI and MSIX)" section.
- `scripts/sign-msix.ps1` -> turn-key signing script that searches both signtool locations, runs signtool with the right flags, and verifies the signature.
- `scripts/build-msix.ps1` -> updated post-build reminder text references sign-msix.ps1 and the four-RDN cert subject.

## Working Signing Flow

```powershell
# 1. Plug in Thales token. Unlock via SafeNet tray app.
# 2. Run:
.\scripts\sign-msix.ps1 -Path "artifacts\installer\msix\ToastNotification.Agent-0.2.0.1.msix"
# 3. SafeNet PIN dialog pops. Enter PIN.
# 4. signtool reports successfully signed and Verify Status=Valid.
```

The script invokes:
```
<signtool> sign /a /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 <file>
```

`/a` = let signtool pick the best cert from any available provider (CSP/KSP), including the SafeNet token. No `/n`, no `/sha1`, no cert-store fishing. SafeNet's CryptoAPI surface does the rest.

## What Remains Open for M0 D2 Close

Install validation on the Win11 lab machine:

```powershell
# On the target machine (Keith's lab Win11 box)
Add-AppxPackage "C:\path\to\ToastNotification.Agent-0.2.0.1.msix"
```

Or double-click the .msix in Explorer. Verify:
- Install completes without prompting "untrusted certificate" (Sectigo R36 is a trusted public CA — should chain cleanly on any internet-connected Windows 10/11 machine).
- Start Menu has a "Toast Notification" tile.
- Settings -> Apps shows "Toast Notification" with publisher "Toast2IT, LLC".
- Launching from Start fires a toast.
- Capture screenshot evidence in this EVIDENCE folder.

Win10 1809 install validation is acknowledged-deferred to M0 D4 (no 1809 lab machine on hand).

## Standing Rules Captured

1. **MSIX manifest Publisher must include EVERY RDN from the cert Subject in the cert's order with the cert's quoting.** Authoritative source: cert utility Details tab, or `(Get-AuthenticodeSignature <signed-file>).SignerCertificate.Subject`. NOT the team's prior memory string.
2. **DigiCert Certificate Utility 2.x does not sign MSIX.** Use signtool.exe.
3. **signtool.exe ships transitively via the WinAppSDK 1.7 NuGet** (`Microsoft.Windows.SDK.BuildTools` under `%USERPROFILE%\.nuget\packages\`). After any successful MSIX build, signtool is on disk - no separate Windows SDK signing tools install needed.
4. **Code Sweep Step 4 for MSIX/manifest changes**: enumerate every cert-subject RDN, verify each in the manifest Publisher, build .msix, extract its AppxManifest.xml, re-verify before sign handoff.
