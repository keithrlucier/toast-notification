# FIX-PROD-001 — Production /register blank-page fix

Date: 2026-05-09
Reporter: Keith ("The register URL - shows a blank white page")
Severity: BLOCKER — every dashboard route on production was rendering as a blank white page; no customer could sign up, log in, or use the product.

## Symptom

`https://toastnotification.com/register` returned the SPA shell HTML (200) but the browser displayed nothing. View-source showed the expected `<div id="root"></div>` plus a script tag pointing at `/assets/index-DelPZakl.js`.

## Diagnosis

Probed the asset URL directly:

```
$ curl -sS -o /dev/null -w "%{http_code}\n" https://toastnotification.com/assets/index-DelPZakl.js
404
$ curl -sS -o /dev/null -w "%{http_code}\n" https://toastnotification.com/assets/index-BiZrMfd-.css
404
```

Both bundles returned 404 even though they were on disk at `/opt/toast/dashboard/assets/`. Inspected `/etc/nginx/sites-enabled/toast`:

```nginx
# Uploaded assets
location /assets/ {
    proxy_pass http://localhost:5216;
    ...
}

# SPA fallback
location / {
    try_files $uri $uri/ /index.html;
}
```

The `/assets/` block was added in M5.C for the asset library — `wwwroot/assets/{tenantId}/{assetId}{ext}` files served by ASP.NET via `UseStaticFiles()`. Vite's default build output directory is also `assets/`, so the SPA bundles (`/dist/assets/index-*.js`) ended up at the same URL prefix.

Result: every SPA bundle request was proxied to ASP.NET, which had no route for it → 404. The SPA could never bootstrap, so React never rendered. Hence the blank page on every route.

This is the same class of bug Carl flagged as a standing rule in a prior project: "Static asset path collision check added to code review checklist — static files must never share a path prefix with SPA route patterns in nginx config." Carrying the lesson into ToastRevival.

## Fix

Edited `src/ToastRevival.Dashboard/vite.config.ts`:

```ts
build: {
  outDir: 'dist',
  // Avoid /assets/ path collision with the nginx upload proxy
  // (M5.C asset library serves /assets/{tenantId}/{file} from the API).
  assetsDir: 'static',
  sourcemap: true,
},
```

Vite now outputs `dist/static/index-*.js` and the generated `index.html` references `/static/...`. nginx's catch-all `try_files` block serves `/static/*` from `/opt/toast/dashboard/static/` directly. The `/assets/` proxy block is unchanged — asset library URLs `/assets/{tenantId}/{assetId}` continue to route to ASP.NET as before.

## Verification

External fetches after deploy:

```
$ curl -sS -o /dev/null -w "%{http_code} (%{size_download} bytes)\n" \
    https://toastnotification.com/static/index-B04HT6PW.js
200 (717816 bytes)

$ curl -sS -o /dev/null -w "%{http_code}\n" \
    https://toastnotification.com/register
200
```

Playwright navigation to `https://toastnotification.com/register` and `https://toastnotification.com/login`:
- Page renders fully (registration form with Organization Name / Admin Email / Password fields; login form with Email / Password fields).
- Console errors: 0.
- Console warnings: 0.

## Standing rule (carry-forward)

Static-file path collision check is now in Code Sweep Step 4 for any frontend deploy:

> If nginx has any `location /<prefix>/ { proxy_pass ... }` blocks, the Vite/build static output prefix MUST NOT match any of them. Default `assets` is unsafe in this project — `/assets/` is owned by the ASP.NET asset library API. Always cross-reference `vite.config.ts build.assetsDir` (or analogous build config) against `/etc/nginx/sites-enabled/*` `location` directives before declaring a deploy clean.
