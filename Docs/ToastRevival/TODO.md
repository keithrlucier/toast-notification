# Toast Notification — Open Items

**Last updated: 2026-05-10 (session 2)**

## Production status

Live at https://toastnotification.com — TOASTWEB1 (54.82.103.160) + TOASTDATA1 (172.26.3.164 private).

All milestones M0A–M8.C complete. M9.A (Mailjet + ClickSend registration flow) complete.
M9.B (pending endpoint pagination — INFO-M2B-002) complete 2026-05-10.
Stripe live: `price_1TVXJYIbddaRrnMlw8K4LrKr`, webhook `we_1TVXJwIbddaRrnMllPMIXamd`.
Free tier: 1–25 devices free, no Stripe required. 26+ requires active subscription.

---

## Engineering backlog

### Medium

- [x] **Onboarding billing step** — Fixed 2026-05-10. Free tier language: "1–25 devices always free, billing starts at device 26+." Continue is primary CTA. Deployed.

- [x] **Velopack update feed** — Fixed 2026-05-10. `https://releases.toastnotification.com/agent/win-x64/` is live with HTTPS. v0.3.1 release package uploaded. Feed verified: `releases.win.json` returns correctly. Agent csproj now carries `<Version>0.3.1</Version>`. To publish future releases: `.\scripts\build-release.ps1 -Version X.Y.Z` then scp `artifacts\releases\*` to server (Anthony handles).

### Low / polish

- [ ] **DiagLog rotation** (INFO-MSIX-004) — Agent DiagLog at `%LOCALAPPDATA%\Toast2IT\Toast Notification\` has no size cap and no rotation. Appends forever. Gate behind `--diag` flag or add rotation before fleet-wide deployment.

- [ ] **INFO-M8C-001** — `HubDeviceConnectedEvent` test uses a 500ms `Task.Delay`. Convert to predicate-poll to eliminate flake risk.

- [ ] **INFO-M7C-003** — Docs sidebar references "Devices → Install agent" admin tab path. Verify this matches the current dashboard nav or update the text.

- [x] **INFO-M2B-002** — Resolved 2026-05-10 (M9.B). `GET /api/notifications/pending` now accepts `?limit=<int>` query param, default 100 (backwards compat for v0.3.x agents), server-clamped to [1, 500]. Wire shape preserved (still array). Agent-side adoption of `limit=500` deferred to next signed agent build (INFO-M9B-001 carry-forward).

- [ ] **Tray icon SVGs** (Diana) — 5 production states (Connected / Reconnecting / Disconnected / Error / Update Available). Placeholders acceptable for testing; required before M9 GA.

- [ ] **Microsoft Store tile assets** — Production-quality Square44, Square150, Wide310x150, StoreLogo tiles. Current listing accepted placeholders. Required before any Store marketing push.

---

## Keith actions

- [ ] **Re-sign MSI** — When next agent build ships, team flags the artifact. Keith signs with Thales token. Current hosted binary is `ToastNotification.Agent-0.3.1.0.msi`.

- [ ] **Windows E2E verification** (M8 D1/D2/D3) — Store install → register → receive notification → interact → verify DB delivery row. Repeat for MSI/RMM and Intune LOB. Requires Keith's lab machine + signed package.

---

## Closed this session (2026-05-10)

- [x] M9.B pending endpoint pagination — `?limit=<int>` query param, default 100, clamp [1, 500], wire shape preserved, integration test (510-row Postgres seed) shipped with the change
- [x] M9.A registration flow — ClickSend SMS verify → Mailjet magic token email → set password → logged in
- [x] Marketing site full redesign — dark theme, honest copy, no fake stats, free tier messaging
- [x] Security posture page `/security` — real architecture, pen-test disclosure, logging policy
- [x] Free tier billing enforcement — devices 1–25 always allowed, 26+ requires Stripe subscription
- [x] Forgot password + reset password pages — self-service flow end to end
- [x] DeployCommand — msiexec with pre-filled CLIENTID on Dashboard and Devices pages
- [x] MSI hosted at `/downloads/ToastNotification.msi`
- [x] Stripe fully configured — secret key, `price_1TVXJYIbddaRrnMlw8K4LrKr`, webhook, success/cancel URLs
- [x] Stripe config panel in superadmin Billing page — full self-service, no SSH required
- [x] Duplicate Stripe prices archived (Keith)
- [x] AWS Windows VM (52.21.249.120) terminated (Keith)
