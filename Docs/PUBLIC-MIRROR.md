# Public Mirror Workflow

Toast Notification is developed in a **private** working repo and mirrored to a
**public** source-available repo for the Roll-Your-Own (self-hosted) tier. This
document is the operational contract for that mirror.

| | |
|---|---|
| **Private repo** (this one) | https://github.com/keithrlucier/toast |
| **Public mirror** | https://github.com/keithrlucier/toast-notification |
| **Sync cadence** | Every release tag (`v0.x.y` SemVer) |
| **Sync mechanism** | `scripts/sync-public-mirror.ps1` |
| **Sync manifest** | `.publicignore` at repo root |

The private repo is where every WIP commit, milestone tracker, internal
handoff doc, evidence file, production IP, SSH key path, and CODEX/team
operational artifact lives. The public mirror is a **sanitized snapshot** of
what self-hosters need to build and run Toast Notification on their own
infrastructure.

---

## Why this workflow exists

The Roll-Your-Own tier (per `Docs/rollyourown.md`) requires a source-available
public repo so that self-hosters can clone, build, audit, and deploy on their
own hardware. The trust model for an MSP deploying a Windows agent to a fleet
of endpoints is "source is open for review" — that's what makes the offering
acceptable for IT operators.

The private repo carries operational content that must not leak publicly:

- Production server IPs (TOASTWEB1 `54.82.103.160`, TOASTDATA1 `172.26.3.164`).
- SSH key file references (`Docs/Assets/*.pem` — already `.gitignore`d as
  files, but path references appear throughout deploy docs).
- Internal milestone planning (`MILESTONES.md`, `STATUS.md`, `TODO.md`).
- Internal review and evidence files (`EVIDENCE/`, `FIX-LIST.md`,
  `TEST-LOG.md`, `CODEX-HANDOFF.md`).
- Cross-AI handoff notes (`CODEX-*.md`, `HANDOFF-*.md`).
- The team-memory and session directory (`.docpro/`).
- Production-specific infrastructure config (`infrastructure/` nginx blocks
  with prod hostnames, etc.).

The sanitization step strips these from the public mirror while preserving
everything a self-hoster actually needs.

---

## What ends up on the public mirror

**Always public:**

- `src/` — all source for `ToastRevival.Api` and `ToastRevival.Dashboard`.
- `tests/` — all test projects.
- `installer/` — WiX installer source (signing certs are external).
- `scripts/build-*.ps1` — build scripts a self-hoster needs to compile their
  own signed agent (Path B in `Docs/rollyourown.md`).
- `docker-compose.yml` and the two `Dockerfile`s — the self-host stack.
- `README-SELF-HOST.md` — the operator-facing README.
- `Docs/rollyourown.md` — open-core strategy doc; public-facing already.
- `Docs/PUBLIC-MIRROR.md` — this file. The public mirror documents its own
  origin honestly.
- `CONTRIBUTORS.md`, `LICENSE`, `.gitignore`, `global.json`, `NuGet.config`,
  `ToastRevival.sln`.
- `.github/workflows/agent-build.yml`, `api-tests.yml`, `codeql.yml` — CI
  that's useful for fork maintainers.

**Never public** (matches `.publicignore`):

- `Docs/ToastRevival/CODEX-HANDOFF.md`, `CODEX-*.md`
- `Docs/ToastRevival/CONTEXT.md`
- `Docs/ToastRevival/DEPLOY.md` (replaced on public side with a sanitized
  `Docs/DEPLOY-SELFHOST.md` if one is needed — but the bulk of self-host
  deploy info already lives in `README-SELF-HOST.md`)
- `Docs/ToastRevival/EVIDENCE/`
- `Docs/ToastRevival/FIX-LIST.md`
- `Docs/ToastRevival/STATUS.md`
- `Docs/ToastRevival/TEST-LOG.md`
- `Docs/ToastRevival/MILESTONES.md`
- `Docs/ToastRevival/TODO.md`
- `Docs/ToastRevival/HANDOFF-*.md`
- `Docs/ToastRevival/DESIGN-SPEC.md`
- `Docs/ToastRevival/CHANGES-*.md` — internal change tracking
- `Docs/Assets/*.pem` — already `.gitignore`d
- `.docpro/` — team memory and session state
- `infrastructure/` — production-specific nginx/systemd; replaced with a
  sanitized `infrastructure/self-host/` subset if needed
- `publish/`, `dist/`, `bin/`, `obj/`, `artifacts/` — build artifacts
- Anything not explicitly in the "Always public" list above is assumed
  private until added to the manifest.

**Sanitized in place:**

The sync script does in-place text substitution on every public file:

| Pattern | Replacement |
|---|---|
| `54.82.103.160` | `<your-web-server-ip>` |
| `172.26.0.161` | `<your-web-private-ip>` |
| `172.26.3.164` | `<your-db-private-ip>` |
| `52.21.249.120` | `<your-build-server-ip>` |
| `34.194.10.242` | `<your-paradise-server-ip>` |
| `Toast_Web_LightsailDefaultKey-us-east-1.pem` | `<your-ssh-key.pem>` |
| `Toast_Data_1_LightsailDefaultKey-us-east-1.pem` | `<your-db-ssh-key.pem>` |
| `toastnotification.com` | preserved (it's the public marketing domain) |
| `releases.toastnotification.com` | preserved (binaries hosted there) |
| `TOASTWEB1` | preserved (it's just a name; only the IP is sensitive) |
| `TOASTDATA1` | preserved (same reason) |

Add new patterns to the `$Substitutions` block in `scripts/sync-public-mirror.ps1`
when you find a new private string in a public file.

---

## Running the sync

### Prereqs (one-time)

1. Create a GitHub PAT with `repo` scope on `keithrlucier/toast-notification`.
   Provide it to the script via `-Pat` parameter or `$env:TOAST_PUBLIC_PAT`.
2. Clone the public repo as a sibling worktree:
   ```powershell
   git clone https://github.com/keithrlucier/toast-notification ../toast-public-mirror
   ```
   The script defaults to `..\toast-public-mirror` relative to the private
   repo root. Override with `-WorktreePath`.

### Per-release sync

After cutting a release tag in the private repo:

```powershell
# From the private repo root, on the release tag commit:
git tag -a v0.4.8 -m "0.4.8 — Server 2025 toast banner fix"
git push origin v0.4.8

# Mirror to public:
.\scripts\sync-public-mirror.ps1 -Tag v0.4.8
```

The script will:

1. Verify `v0.4.8` exists locally.
2. Check out the tag in a detached worktree of the public repo.
3. Wipe the public worktree except `.git/` and `LICENSE` (which is preserved
   if missing on the source side).
4. Copy every file from the private working tree, applying `.publicignore` and
   the `.gitignore` from the private repo.
5. Run the sanitization substitutions over every text file.
6. Commit with the message `Public mirror release v0.4.8\n\n<tag annotation>`.
7. Tag the commit `v0.4.8` on the public side.
8. Push the public branch and tag.

The script is idempotent: re-running with the same tag is a no-op if the
public side already carries that tag. To re-sync (because you found something
that should/shouldn't have been included), bump the patch version or use
`-Force` to retag.

### Dry-run

Always sanity-check the diff before pushing:

```powershell
.\scripts\sync-public-mirror.ps1 -Tag v0.4.8 -DryRun
```

Dry-run runs steps 1–5, then prints the public-side `git status` and
`git diff --stat` without committing or pushing.

---

## Adding a new file to "public"

If you create a new file under `src/`, `tests/`, `installer/`,
`docker-compose.yml`, or any other "Always public" path, it ships
automatically on the next sync — no manifest edit needed.

If you create a new file outside those paths and want it public, add an
**inclusion comment** to `.publicignore` (lines starting with `!`) or move it
under one of the public paths.

If you create a new file outside those paths and it must stay private, add a
matching exclusion to `.publicignore` and call it out in the next code review.

---

## Adding a new sanitization rule

When you commit a new internal hostname, IP, or path reference to a file that
ships publicly:

1. Add the literal to the `$Substitutions` hash table in
   `scripts/sync-public-mirror.ps1`.
2. Add the row to the "Sanitized in place" table in this doc.
3. Run a dry-run sync against the latest tag and grep the public worktree for
   the literal to confirm it was substituted away.

---

## Audit checklist (run once per release before public push)

After dry-run, before the real push, the operator runs this checklist
against the staged public worktree:

- [ ] `git grep -rn "54.82\|172.26\|52.21\|34.194"` in the public worktree
      returns **no hits** outside fenced code blocks that are clearly examples.
- [ ] `git grep -rn "LightsailDefaultKey"` returns no hits.
- [ ] `git grep -rn "CODEX-HANDOFF\|EVIDENCE/\|STATUS\.md\|TEST-LOG"` returns
      no hits in committed text.
- [ ] No files under `Docs/ToastRevival/` exist in the public worktree
      (the whole directory is excluded; only `Docs/rollyourown.md` and
      `Docs/PUBLIC-MIRROR.md` ship at `Docs/`).
- [ ] No `.pem`, `.pfx`, `.p12`, `.key`, `.pvk`, `.spc` files exist.
- [ ] `.docpro/` does not exist in the public worktree.
- [ ] `infrastructure/` either does not exist or contains only the
      `self-host/` subdirectory.

If any check fails, fix the substitution rule or the `.publicignore` and
re-run the dry-run.

---

## Why not auto-mirror every commit?

Two reasons:

1. **Private main is noisy.** WIP commits, in-flight milestone work, and AI
   handoff commits would all end up in public history. Self-hosters reading
   commit logs would see partial-work commits that don't correspond to
   anything in a release.
2. **Sanitization is per-snapshot.** A tagged release is a known
   sanitization checkpoint. Mirroring every commit means the audit checklist
   above has to run on every push, which is a CI burden we haven't built and
   a human burden we won't sustain.

Release-tag cadence keeps the public history clean and the audit human-sized.

---

## Open questions / roadmap

- **`Toast2IT` vs `keithrlucier` org.** `README-SELF-HOST.md` line 40 clones
  from `Toast2IT/toast-notification.git`, while `llms.txt` and the releases
  URL on line 84 reference `keithrlucier/toast-notification`. The canonical
  repo is `keithrlucier/toast-notification`; the `Toast2IT` reference is a
  bug fixed alongside this doc.
- **CI-driven mirror.** A GitHub Action that runs the sync script on every
  release tag push (rather than from a developer workstation) is the right
  long-term shape. Deferred until the manual workflow is proven for a few
  releases.
- **Squash vs. preserve.** Right now every public commit corresponds to a
  release tag, full source snapshot. We do **not** preserve private commit
  history. That's deliberate — private commit history contains AI handoff
  noise and internal-only references. If you need a richer public history,
  add a `--preserve-history-since=<tag>` flag and selectively re-author
  commits without internal references.
