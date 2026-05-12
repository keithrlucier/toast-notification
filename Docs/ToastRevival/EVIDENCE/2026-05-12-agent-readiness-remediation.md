# 2026-05-12 Agent Readiness Remediation

## Trigger

Reviewed the Cloudflare "Is Your Site Agent-Ready?" PDF export for
`https://toastnotification.com/`. The scan scored 25 and showed that the site
already had `robots.txt` and `sitemap.xml`, but lacked Markdown negotiation,
HTTP discovery `Link` headers, API catalog metadata, Agent Skills discovery, and
returned the React SPA shell for missing `/.well-known/*` resources.

## Changes

- Added `/index.md` as the Markdown homepage representation for agents that send
  `Accept: text/markdown`.
- Added `/.well-known/api-catalog` as a truthful Linkset-style public catalog
  pointing to public documentation and `llms.txt`.
- Added `/.well-known/agent-skills/index.json` and a single no-script
  `toast-notification-product` skill that points agents to public product,
  pricing, security, deployment, and API documentation.
- Added `Content-Signal: search=yes, ai-input=yes, ai-train=no` to
  `robots.txt`.
- Updated `llms.txt` crawler policy to align with the content signal.
- Updated the nginx snapshot to:
  - negotiate `/` to `/index.md` when `Accept: text/markdown` is present;
  - emit homepage `Link` headers for `llms.txt`, `/index.md`, sitemap, API
    catalog, and Agent Skills discovery;
  - serve known `/.well-known/*` files with JSON/Markdown content types;
  - return real 404s for unknown `/.well-known/*` endpoints instead of serving
    the SPA HTML shell.

## Intentionally Not Added

No OAuth/OIDC, MCP, WebMCP, or commerce protocol metadata was added. The product
does not currently expose those public protocols, and publishing placeholder
metadata would create a false integration surface.

## Deployment

Dashboard static bundle was rebuilt with `npm run build`, copied to
`/opt/toast/dashboard/`, and nginx was reloaded on TOASTWEB1.

Production nginx had `/etc/nginx/sites-enabled/toast` as a regular file rather
than a symlink to `/etc/nginx/sites-available/toast`. Updating only
`sites-available/toast` did not affect live routing. The enabled copy was then
updated as well. A temporary backup initially placed under `sites-enabled/`
caused duplicate-server warnings because nginx includes every file in that
directory; the backup was moved to
`/etc/nginx/sites-available/enabled-backups/`, after which `nginx -t` and reload
were clean.

## Live Verification

- `GET /` with `Accept: text/html` returns `200 text/html` and discovery
  `Link` headers.
- `GET /` with `Accept: text/markdown` returns `200 text/markdown`.
- `GET /index.md` returns `200 text/markdown`.
- `GET /.well-known/api-catalog` returns `200 application/linkset+json`.
- `GET /.well-known/agent-skills/index.json` returns `200 application/json`.
- `GET /.well-known/agent-skills/toast-notification-product/SKILL.md` returns
  `200 text/markdown`.
- Unknown discovery paths such as
  `/.well-known/oauth-authorization-server`,
  `/.well-known/openid-configuration`, and
  `/.well-known/mcp/server-cards.json` now return `404` instead of the React SPA
  HTML shell.
- `/robots.txt` includes
  `Content-Signal: search=yes, ai-input=yes, ai-train=no`.
