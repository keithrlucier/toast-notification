# M0 D2 - Signed MSIX Build (UNSIGNED produced; signing handoff to Keith)

**Date:** 2026-05-07
**Milestone:** M0 D2 - MSIX package signed with OV cert, installs cleanly on Win10 1809+ / Win11
**Status:** BUILD COMPLETE (UNSIGNED). Signing + install validation = Keith handoff.
**Repo commit:** TBD (this evidence is committed alongside the build artifacts)

---

## What Shipped

A `.msix` package built from the existing `src/ToastRevival.Agent` project via WinAppSDK 1.7's Single-Project MSIX path. One csproj, two output modes - unpackaged (default, drives the WiX MSI) and packaged MSIX (when `-p:WindowsPackageType=MSIX` is passed).

### Files Added
- `src/ToastRevival.Agent/Package.appxmanifest` - 39 lines. Identity, properties, capabilities, visual elements.
- `src/ToastRevival.Agent/Properties/launchSettings.json` - 9 lines. Required by `Microsoft.WindowsAppSDK.SingleProject.targets` debug-profile injection.
- `src/ToastRevival.Agent/Images/Square44x44Logo.png` (44x44, 240 B)
- `src/ToastRevival.Agent/Images/Square150x150Logo.png` (150x150, 914 B)
- `src/ToastRevival.Agent/Images/Wide310x150Logo.png` (310x150, 9.7 KB)
- `src/ToastRevival.Agent/Images/StoreLogo.png` (50x50, 257 B)
- `scripts/generate-msix-tile-assets.ps1` - System.Drawing-based procedural placeholder generator (brand teal #00C9A7 + "Toast Notification" wordmark on the wide tile, "T" mark on squares/store).
- `scripts/build-msix.ps1` - Wrapper around `dotnet build -c Release -p:Platform=x64 -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false`.

### Files Modified
- `src/ToastRevival.Agent/ToastRevival.Agent.csproj` - made `<WindowsPackageType>` conditional (preserves `None` as default), added a conditional `<PropertyGroup Condition="'$(WindowsPackageType)' == 'MSIX'">` block (TargetPlatformVersion, MinVersion, Platforms, RuntimeIdentifier, SelfContained, WindowsAppSDKSelfContained, AppxBundle=Never, GenerateAppxPackageOnBuild=true), added a conditional `<ItemGroup>` for `<AppxManifest>` and `<Content Include="Images\*.png" />`.

---

## Build Output

```
Path: C:\SOURCE\toast\artifacts\installer\msix\ToastNotification.Agent-0.2.0.0.msix
Size: 63.53 MB
Files inside: 458
Build: dotnet build (WinAppSDK 1.7.250310001 SingleProject MSIX targets)
Build time: ~14s first / ~8s incremental
Warnings: 1 (mspdbcmf.exe missing - symbols package skipped, benign; FIX-MSIX-003)
Errors: 0
```

---

## Manifest Identity Surface (read from the produced .msix)

```xml
<Identity Name="Toast2IT.ToastNotification.Agent"
          Publisher="CN=&quot;Toast2IT, LLC&quot;, S=Florida, C=US"
          Version="0.2.0.0"
          ProcessorArchitecture="x64" />
<Properties>
  <DisplayName>Toast Notification</DisplayName>
  <PublisherDisplayName>Toast2IT, LLC</PublisherDisplayName>
  <Logo>Images\StoreLogo.png</Logo>
</Properties>
<Application Id="App"
             Executable="ToastNotification.Agent.exe"
             EntryPoint="Windows.FullTrustApplication">
  <uap:VisualElements DisplayName="Toast Notification"
                      Description="Managed Windows toast notifications for MSP-managed endpoints."
                      BackgroundColor="#0F1117"
                      Square150x150Logo="Images\Square150x150Logo.png"
                      Square44x44Logo="Images\Square44x44Logo.png">
    <uap:DefaultTile Wide310x150Logo="Images\Wide310x150Logo.png" />
  </uap:VisualElements>
</Application>
<Capabilities>
  <rescap:Capability Name="runFullTrust" />
</Capabilities>
```

### Project codename vs. product name audit (M0A standing rule)
- Internal `Identity.Name` keeps "Toast2IT" / "ToastNotification" - reverse-DNS form, never user-visible. ZERO occurrences of "ToastRevival" in any user-visible field.
- All user-visible strings (`DisplayName`, `PublisherDisplayName`, `VisualElements DisplayName`, `Description`, `Executable`) use the product name "Toast Notification" / "Toast2IT, LLC" / "ToastNotification.Agent.exe".

### Cert subject match
The Sectigo OV cert subject (verified on the M0A signed MSI via `Get-AuthenticodeSignature`) is:
```
CN="Toast2IT, LLC", S=Florida, C=US
```
The `Package.Identity.Publisher` attribute matches this string exactly (XML-escaped). A mismatch on signing produces a `0x800B0109` install failure - that's the failure mode this discipline prevents.

---

## What This Closes (Build Side)

- M0 D2 build mechanics: a valid, well-formed, dimensionally-correct, identity-correct, FullTrust-capable MSIX package is produced from a single `dotnet build` invocation.
- MSIX is gitignored (`.gitignore` already contains `*.msix`), so only the source manifest, scripts, and tile assets are committed.

## What This Does NOT Close (Keith Handoff)

D2 deliverable says "installs cleanly on Windows 10 1809+ and Windows 11." The build is complete; the install validation is a Keith-side step:

1. **Sign:** Same Thales hardware token + Sectigo OV cert flow used for the M0A MSI:
   ```powershell
   signtool.exe sign /tr http://timestamp.digicert.com /td sha256 /fd sha256 ^
     /a /n "Toast2IT, LLC" ^
     "C:\SOURCE\toast\artifacts\installer\msix\ToastNotification.Agent-0.2.0.0.msix"
   ```
   (Adjust per Keith's actual signtool invocation.)
2. **Verify signature:** `Get-AuthenticodeSignature ToastNotification.Agent-0.2.0.0.msix` must report Status=Valid, Signer="Toast2IT, LLC", Issuer=Sectigo Public Code Signing CA R36.
3. **Install on Win11 lab machine:** double-click `.msix` (or `Add-AppxPackage`), verify Start Menu tile, launch app, confirm toast fires.
4. **Install on Win10 1809+ machine:** ideally a domain-joined or Intune-managed image (M0 D4 work); if a clean Win10 1809 lab machine isn't available, document that gap in TEST-LOG and defer to D4.

---

## Open Items (filed in FIX-LIST.md)

- `FIX-MSIX-001` - bump `TargetPlatformVersion` to `10.0.22621.0` before M0 D5 Store flight (current 19041 caps `MaxVersionTested` for Store certification).
- `FIX-MSIX-002` - manifest `TargetDeviceFamily MinVersion=10.0.17763.0` is more permissive than `Program.cs OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)`; on Win10 1809 the install succeeds but the runtime exits 2. Decide which gate is canonical before M0 D4 GPO matrix.
- `FIX-MSIX-003` - cosmetic `mspdbcmf.exe` warning; harmless.

---

## Code Sweep

Abish ran a SIGNIFICANT-scope sweep with full blast-radius and 5-perspective review. Verdict: **SHIP WITH NOTES**. Findings logged in this entry's "Open Items" section and `FIX-LIST.md`.

Standing M0A rule reaffirmed: every user-visible string in the manifest was audited line-by-line against the project codename. Zero codename leaks.
