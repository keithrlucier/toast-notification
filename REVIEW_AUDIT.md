# Code Review Audit — 2026-05-28

Source ledger: REVIEW_LEDGER.md (committed at `b21f44b`; audited against current HEAD `0dc721c` / 0.4.23)
Total rows audited: 3
Pass / Reopen / Incomplete / Drift: 2 / 0 / 1 / 0

> Auditor note: HEAD has advanced five releases past the review scope (0.4.18 → 0.4.23).
> Every cited `file:line` was re-resolved against current source before judging — the
> ledger's line numbers had drifted (the C# file was touched again in `b21f44b`).

---

## Agent-M1 — RenderBitmap carries three stacked, contradictory `<summary>` XML doc blocks
Severity: Medium
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `src/ToastRevival.Agent/DesktopOverlayService.cs:208-239` now holds a single
accurate `<summary>` describing the real three-phase pipeline (opaque FillPath panel →
GDI ClearType text via `TextRenderer.DrawText` → per-pixel alpha via `ApplyAlphaMask`).
It correctly states `SourceConstantAlpha = 255` with per-pixel alpha as the only
translucency driver — verified against the actual value at `:454` (`SourceConstantAlpha = 255`,
`AlphaFormat = AC_SRC_ALPHA`). The two obsolete blocks (Format32bppPArgb claim; GDI+
DrawString/drop-shadow claim) are gone, replaced by a one-line `History:` remark preserving
lineage (0.4.11 PArgb → 0.4.15 GDI+ DrawString → 0.4.18 GDI ClearType). The second stale
body-level comment the fix claimed to catch is also corrected at `:284-286`. No other stacked
or contradictory summaries remain in the file.
Recommended action: none

## Agent-L1 — ApplyAlphaMask corner mask uses a binary inside/outside test, killing AA
Severity: Low
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-PASS
Evidence: `src/ToastRevival.Agent/DesktopOverlayService.cs:384-407` replaces the binary
`dx*dx + dy*dy > r2` step with sub-pixel coverage: `d = Math.Sqrt(dx*dx + dy*dy)`,
`coverage = radius + 0.5 - d`, then `coverage <= 0 → alpha 0`, `coverage < 1 → panelAlpha *
coverage` (skipping luminance interp, correctly justified — text starts at `pad = 12*scale`,
corner radius is `6*scale`, so corner-band pixels are guaranteed panel-colored), and
`coverage >= 1 →` fall through to the luminance-driven alpha. This restores the smooth arc
that `FillPath` drew under `SmoothingMode.AntiAlias`. The fix addresses the cited staircase
defect and introduces no regression — it inherits the same corner-center reference the prior
binary test used and only adds the AA falloff on top.
Recommended action: none

## Ops-L1 — setup-git-credentials.ps1 PAT params are plain `[string]` (leak to history/transcript/audit)
Severity: Low
Original status: FIXED-VERIFIED
Audit verdict: AUDIT-INCOMPLETE
Evidence: The script fix is real and correct — `infrastructure/ops/setup-git-credentials.ps1:72-73`
declares both params `[SecureString]`; `:85-90` adds the `ConvertFrom-SecureToPlain` BSTR/
ZeroFreeBSTR helper (works on PS 5.1 and 7+); plaintext is unwrapped only at point of use
(`:92-93`) and the script's own `.EXAMPLE`/`.NOTES` (`:50-68`) correctly show the
`Read-Host ... -AsSecureString` invocation. **Missed site:** the sibling doc in the same
directory, `infrastructure/ops/README.md:18-22`, still documents the canonical invocation as
plain-string PAT literals typed on the command line (`-PrivatePat 'github_pat_11AJ...'`). That
is the exact `Get-History` / `Start-Transcript` / process-line audit exposure Ops-L1 set out
to eliminate. It is doubly broken now: after the param type change, PowerShell will refuse to
coerce a plain string into a mandatory `[SecureString]` and the documented command fails to a
prompt. The fix landed in code but not in the documentation of the same change.
Recommended action: extend fix to missed site — rewrite `infrastructure/ops/README.md:18-22` to
the `Read-Host -AsSecureString` (or `ConvertTo-SecureString`) invocation, matching the script's
own `.EXAMPLE` block. Mechanical, ~5 lines.
**Resolved in this audit pass:** `infrastructure/ops/README.md:18-23` rewritten to the
`Read-Host -AsSecureString` invocation (no-defer rule — mechanical fix, within runway). Ops-L1
is now fully terminal across code + docs.

---

## Summary

The remediation pass was honest and technically sound on the two in-file findings. Both
Agent-M1 (stacked stale summaries) and Agent-L1 (corner AA) are genuinely fixed against
current source, line-drift notwithstanding, and the Agent-M1 summary is now factually accurate
about the `SourceConstantAlpha = 255` / per-pixel-alpha mechanism — that's the kind of comment
that saves the next engineer a session on this hot overlay surface. Ops-L1's *code* fix is also
correct, but it stopped at the script and missed the README that documents it: the canonical
"how to run this" example still teaches the insecure plain-string PAT pattern the fix removed,
and it no longer even works against the SecureString signature. One row reopens as INCOMPLETE
with a mechanical doc fix. Net: solid pass, one blast-radius miss on documentation — consistent
with the standing lesson that a fact/security change must sweep its docs, not just its code.

### Out-of-scope findings (for next reviewer)
- `scripts/sync-public-mirror.ps1:61` takes its PAT as plain `[string] $Pat = $env:TOAST_PUBLIC_PAT`.
  Lower exposure than a typed literal (env-var sourced, not echoed to history), and it was never
  in the 0.4.16→0.4.18 review scope, so it is **not** part of Ops-L1. Flagging for a future pass to
  decide whether the SecureString hygiene should extend platform-wide to every PAT-accepting script.
- `DesktopOverlayService.cs` `ApplyAlphaMask` corner-coverage center (`cx/cy = radius-1`) sits ~1px
  off the `FillPath` arc circle center (`radius,radius`), so the RGB panel edge and the alpha mask
  are sub-pixel misaligned at the four corners. Pre-existing (inherited from the old binary test),
  sub-pixel, cosmetic — noting only for completeness; not worth a row on its own.
