# 2026-05-09 - Codex admin/platform/billing closeout

## Scope

- Authenticated dashboard shell redesigned toward corporate enterprise console style.
- Public register / legacy login corrected so tenant owners are `SuperAdmin`.
- PlatformAdmin implemented separately from tenant roles.
- Pricing v2 implemented as one Standard plan: $0.22/device/month, 100-device minimum, 14-day Stripe trial.
- Production deployed to TOASTWEB1.

## Verification

- `dotnet build ToastRevival.sln --no-restore`: passed, 0 warnings, 0 errors.
- `npm run build` in `src/ToastRevival.Dashboard`: passed. Vite chunk-size warning remains.
- `dotnet publish src\ToastRevival.Api --configuration Release --runtime linux-x64 --no-self-contained`: passed.
- Production `toast-api`: `active`.
- `https://toastnotification.com/login`: 200.
- Emitted dashboard script `/static/index-DTLiw4aQ.js`: 200, 723817 bytes.
- Bad login POST `/api/auth/login`: 401.
- Public smoke register returned `Role=SuperAdmin`, `IsPlatformAdmin=false`.
- Public smoke `/api/billing/plan` returned `Standard`, `$0.22`, 100-device minimum, `$22.00`.
- Public smoke `/api/system/tenants`: 403, confirming register does not mint PlatformAdmin.
- Temporary promoted smoke platform admin reached `/api/system/billing-overview`: 200.
- Browser smoke on live dashboard/billing: Platform Admin badge visible, Billing Standard plan visible, no console errors.
- Production DB cleanup confirmed `0` users matching `codex-%@toastnotification.test`.
- Production Keith access confirmed: one matching row for `keith@colosolutions.com` with `Role=SuperAdmin` and `IsPlatformAdmin=true`.

## Remaining operator action

Create the Stripe recurring per-device price and configure production `Stripe__PerDevicePriceId`. Checkout intentionally returns 503 until that value exists.
