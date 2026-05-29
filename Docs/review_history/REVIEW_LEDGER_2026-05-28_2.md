# REVIEW LEDGER — Cold Code Review (2026-05-28)

**Pass date:** 2026-05-28
**Reviewer:** Carl (cold reviewer, no prior context beyond ledger archive)
**Scope:** Net-new code since prior ledger closed clean at `674e8ea` (2026-05-28). Diff covers 0.4.16 → 0.4.18 overlay polish + ops/credential scaffolding + RMM URL realignment. 8 files changed, +328/−40.
**Prior ledger archived to:** `docs/review_history/REVIEW_LEDGER_2026-05-28_1.md`

Read REVIEW_LEDGER.md / latest review_history? Yes
Closed-pass anchors honored? Yes — prior cold pass closed all 4 rows terminal at `674e8ea`. The `AuthContext.tsx:83` INFO-01 anchor is in a file untouched by this scope. M12 overlay surface (`DesktopOverlayService.cs`) is in scope; net-new code is the GDI-text + alpha-mask rewrite for the BgInfo-style ClearType polish.
Files scanned: 8 (`git diff --stat 674e8ea HEAD`)
Files with anchors found and respected: 0 (no anchors in the in-scope changed lines).

---

## Summary

| Severity | Count | Open | Fixed | Rejected |
|----------|-------|------|-------|----------|
| Critical | 0     | 0    | 0     | 0        |
| High     | 0     | 0    | 0     | 0        |
| Medium   | 1     | 0    | 1     | 0        |
| Low      | 2     | 0    | 2     | 0        |
| ANCHOR-CHALLENGE | 0 | 0 | 0 | 0    |
| **Total**| **3** | **0** | **3** | **0** |

Tight scope, small diff, code quality is solid. The GDI/ClearType rewrite is technically sound and Keith signed off on it in 0.4.18 (`a6e6e9a`). All three findings are local to that file or the new ops script — nothing in the API, dashboard, or DB surface needed flagging.

---

## Critical

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|

None found.

---

## High

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|

None found.

---

## Medium

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|
| Agent-M1 | Medium | FIXED-VERIFIED | `src/ToastRevival.Agent/DesktopOverlayService.cs:208-260` | `RenderBitmap` carries THREE stacked `<summary>` XML doc blocks from successive iterations (0.4.11, 0.4.15, 0.4.18). Blocks 1 and 2 are obsolete — block 1 claims `Format32bppPArgb` (current code is `Format32bppArgb`); block 2 claims `Graphics.DrawString` with `AntiAliasGridFit` and a drop-shadow value/label color split (current code is `TextRenderer.DrawText` with pure-white labels and values, no shadow). Block 3 is also factually wrong on the opacity mechanism: it says "applying overall translucency via SourceConstantAlpha on the BLENDFUNCTION (set in PushLayeredBitmap by passing opacityPercent through)" — but `PushLayeredBitmap` (line 457) hardcodes `SourceConstantAlpha = 255`, and the real mechanism is per-pixel alpha written by `ApplyAlphaMask`. | The next engineer to touch this surface (Win11 26201 / Win12 / Wayland-on-Windows / next overlay bug) will read three contradictory specs and trust the wrong one. The Win11 26200 WorkerW saga (0.4.12-0.4.14) shows what stale-by-design assumptions cost on this code path — call it three sessions and a release-cut roll-back. Cheap to collapse: keep the latest summary, prune the prior two into a one-line "// History: 0.4.11 used premultiplied alpha; 0.4.15 used GDI+ DrawString; 0.4.18 switched to GDI ClearType for crispness" remark if you want lineage. Then fix the SourceConstantAlpha line. | High |

---

## Low

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|
| Agent-L1 | Low | FIXED-VERIFIED | `src/ToastRevival.Agent/DesktopOverlayService.cs:395-411` (`ApplyAlphaMask` corner check) | The rounded-rect corner mask uses a binary inside/outside test on integer squared distance (`dx*dx + dy*dy > r2`). Pixels with center inside the radius get `panelAlpha`; pixels outside get `0`. The anti-aliased edge that `FillPath` produced (smooth alpha falloff over ~1-2 pixels) is overwritten with hard pixel steps. | Visible regression vs 0.4.15 corner quality: at `radius = 6 * scale` (6 px on 100% DPI, 12 px on 200%), the four corners read as small staircase steps instead of the smooth curve `SmoothingMode.AntiAlias` originally produced. Text glyph AA is preserved because text pixels straddle the `panelLum`-to-`textLum` range and pick up the luminance-driven gradient — corner pixels stay at panel luminance and miss the gradient. Cheap fix: do a coverage check (`r2 - 1 < d2 <= r2 + 1`) and interpolate `alpha = panelAlpha * coverage`, OR keep the FillPath-produced alpha when it's already < panelAlpha. Won't ship-block; corners are small and not what users look at. | Medium |
| Ops-L1 | Low | FIXED-VERIFIED | `infrastructure/ops/setup-git-credentials.ps1:55-56` | `$PrivatePat` and `$PublicPat` are declared as plain `[string]` parameters. PowerShell records mandatory `[string]` parameter values in `Get-History`, in any active `Start-Transcript` session, and in Windows command-line / process-tree audit logs (Sysmon, Defender for Endpoint, etc.) — the PAT bodies end up persisted across multiple system surfaces beyond the `.git-credentials` file the script intentionally writes. | Defense-in-depth on a dev/release-cut box. Conventional pattern is `[SecureString]` with `ConvertFrom-SecureString -AsPlainText` at the point of use, or `[PSCredential]` so the PAT is requested via `Get-Credential` (still pasted by the operator, but never recorded in shell-side audit logs). Standing rule from prior pass: "NEVER store the VSCE PAT in memory or persistent system" — same hygiene principle applies to GitHub PATs and the same surfaces that catch a VSIX-embedded PAT (Microsoft scanner, repo scanners) also catch transcripts that get accidentally committed. Mitigant: Keith runs it on his own dev box and rotates the PATs anyway. | Medium |

---

## ANCHOR-CHALLENGE

| ID | Severity | Status | File:Line | What's wrong | Why it matters | Confidence |
|----|----------|--------|-----------|--------------|----------------|------------|

None found.

---

## Top fixes (in order)

1. **Agent-M1** — Pruned `DesktopOverlayService.cs:208-260` to one accurate `<summary>` describing the actual three-phase render pipeline (opaque panel + GDI ClearType text + per-pixel alpha via ApplyAlphaMask). Removed the false claim that opacity flows through `SourceConstantAlpha` — the real mechanism is the per-pixel alpha channel built by ApplyAlphaMask, with `SourceConstantAlpha = 255` deliberately hardcoded. Caught a second stale comment at line 284-285 (same claim, body-level) and fixed that too.
2. **Agent-L1** — Replaced the binary corner inside/outside test in `ApplyAlphaMask` with sub-pixel coverage (`Math.Sqrt(dx² + dy²)` → `clamp(radius + 0.5 − d, 0, 1)`). Preserves the smooth AA curve `FillPath` originally produced; corner-edge pixels scale `panelAlpha` by coverage and skip luminance interp (text never reaches into the corner radius — pad = 12·scale, radius = 6·scale).
3. **Ops-L1** — Switched `setup-git-credentials.ps1` PAT params from `[string]` to `[SecureString]`. Added a `ConvertFrom-SecureToPlain` helper using the BSTR/ZeroFreeBSTR pattern (works on Windows PowerShell 5.1 AND PowerShell 7+; `ConvertFrom-SecureString -AsPlainText` is PS7-only). Plaintext is unwrapped only at the point of use into the `.git-credentials` URL lines — PAT body no longer lands in Get-History, Start-Transcript, or process-line audit logs. Updated `.EXAMPLE` and `.NOTES` to show the SecureString invocation pattern.

---

## Notes on what was reviewed and NOT flagged

- **DPI awareness wiring (0.4.17, `29e39d2`):** `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` in the csproj synthesizes the manifest `<dpiAwareness>` fragment at build time; the hand-rolled `app.manifest` correctly omits `<dpiAwareness>` (would conflict, WFAC010) and only declares `<supportedOS>`. The `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` call at `TrayIconService.cs:108` is redundant belt-and-suspenders relative to the manifest path (returns false if the manifest already set it, but that's a no-op). `dpiScale = _form.DeviceDpi / 96f` at `DesktopOverlayService.cs:162` then reads the per-monitor DPI correctly under PerMonitorV2.
- **`AllowUnsafeBlocks=true` in csproj:** justified — `ApplyAlphaMask` uses `byte*` pointer iteration over `LockBits`'d pixel data. Single function, contained, no escape. Bounds are derived from `bmp.Width/Height` and the `Stride` returned by `LockBits` itself; the `try/finally` correctly pairs `UnlockBits`.
- **GDI text trailing-space measurement:** the 0.4.14 "Hostname:COL-L-003" glue bug doesn't recur. `TextRenderer.MeasureText` (GDI) preserves trailing whitespace in measured width, unlike GDI+ `MeasureString` with `GenericTypographic`. Label width includes the `": "` gap; value is drawn at `x + labelSz.Width`. Verified by reading the call shape at lines 332-335.
- **ApplyAlphaMask luminance constant:** `panelLum = 25` vs actual integer Rec.601 luminance of `(24, 24, 28)` which computes to `24` exactly under the same formula used in the loop. Off by one. Functionally harmless — a pixel at the panel base color computes `lum = 24`, `t = 24 - 25 = -1`, takes the `t <= 0` branch → `panelAlpha`, which is correct. Would be slightly more honest as `panelLum = 24`, but no behavior changes. Not flagged.
- **`PushLayeredBitmap` GDI pairing:** unchanged from prior pass — `CreateCompatibleDC`/`DeleteDC`, `GetHbitmap`/`DeleteObject`, `SelectObject` save+restore all correctly paired in finally. No regression.
- **MSI URL change (`infrastructure/rmm/install-toast-agent.ps1:90`):** `/downloads/agent/ToastNotification.Agent-latest.msi` → `/downloads/ToastNotification.msi`. Grep across the workspace confirms the new path is the only one referenced (Dashboard `DeployCommand.tsx`, `InstallAgent.tsx`, `Onboarding.tsx`, RMM script default, README). No stale references in any `.cs`/`.tsx`/`.ts`/`.conf`/`.yml`. Nginx-side support for the old path is outside scope.
- **Banned terms / codename audit (Diana standing rule):** `setup-git-credentials.ps1` and the new `app.manifest` contain no "persona", "audio drama", or "ToastRevival" — the script refers to "Toast Notification" and "Toast" (acceptable short form). Manifest uses `ToastNotification.Agent` assembly name (already canonical).
- **csproj version coherence:** `Version`, `AssemblyVersion`, `FileVersion` all at `0.4.18`; `Package.appxmanifest` at `0.4.18.0`. No drift.
- **Build verification:** `dotnet build src/ToastRevival.Agent` succeeded with 0 warnings, 0 errors against this HEAD. Multiple-summary XML doc blocks do not produce CS1571 in current Roslyn — they silently overwrite, which is exactly the maintainability hazard Agent-M1 flags.
