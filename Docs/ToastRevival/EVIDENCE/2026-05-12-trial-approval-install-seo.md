# 2026-05-12 - Trial Approval Gate, Tenant Install Page, SEO Refresh

## Summary

Public registration now creates a reviewed trial request instead of directly creating a tenant. The request captures company/contact/use-case details and Cloudflare Turnstile verification metadata, then waits for platform-admin approval.

Tenant admins now have a dedicated `/devices/install` surface that exposes the tenant ID, server URL, MSI download URL, and a prefilled `msiexec` command with the tenant enrollment key. This makes the MSI path visible to tenant owners, not only platform admins.

Public content was updated for MSP and software buyers with concrete use cases, deployment language, reviewed-access language, current block pricing, SEO metadata, JSON-LD, prerendered route content, sitemap dates, and `llms.txt`.

## Backend Changes

- Added `TrialRequest` entity, status/use-case enums, EF configuration, and migration `M10TrialApprovalGate`.
- Added `GET /api/auth/register/config` for public registration config.
- Changed `POST /api/auth/register/init` into a pending trial request endpoint with Turnstile verification and duplicate-request checks.
- Disabled legacy direct public registration by default with `Registration:AllowLegacyDirectRegister=false`.
- Added platform-admin review endpoints:
  - `GET /api/system/trial-requests`
  - `POST /api/system/trial-requests/{id}/approve`
  - `POST /api/system/trial-requests/{id}/reject`
- Approval provisions the tenant, tenant-owner user, default templates, audit log row, and password setup email.

## Frontend Changes

- Rebuilt `/register` as a detailed trial request form with company, website, contact, phone, job title, intended use case, notes, and Turnstile widget support.
- Added `/system/trial-requests` for platform-admin review.
- Added `/devices/install` for tenant-admin deployment details and MSI download command.
- Linked Devices to the install surface.
- Updated onboarding install copy so new tenant owners are sent to Install Agent for the current MSI, tenant ID, server URL, and enrollment key.
- Refreshed Home, Pricing, docs, `llms.txt`, route SEO metadata, JSON-LD, prerender SEO content, and sitemap dates.

## Verification

- `dotnet build src\ToastRevival.Api\ToastRevival.Api.csproj`: passed, 0 warnings, 0 errors.
- `npm run build` in `src\ToastRevival.Dashboard`: passed, including TypeScript, Vite build, and SEO prerender. Existing chunk-size warning remains.
- `dotnet test tests\ToastRevival.Api.Tests\ToastRevival.Api.Tests.csproj`: blocked by local Docker/Testcontainers. The test assembly compiled and 21 tests passed; 31 tests failed before app code executed because the shared Postgres fixture could not reach Docker.
- Browser smoke at `http://127.0.0.1:5173`: `/register`, `/`, `/pricing`, `/docs/getting-started`, and unauthenticated `/devices/install` redirect to `/login` verified. Only console warnings observed were existing React Router v7 future-flag warnings.
- Public trial form rendered the company, website, contact, phone, job-title, intended-use-case, and submit controls; the intended-use-case select was operable in-browser.

## Deploy Notes

- Configure production `Turnstile__SiteKey`, `Turnstile__SecretKey`, and keep `Turnstile__Required=true`.
- Apply EF migration `M10TrialApprovalGate` before routing public registration traffic to the new flow.
- Cloudflare Turnstile action expected by the backend is `trial_register`.
