# FIX-PROD-002 — Register flow never worked end-to-end (4 stacked bugs)

Date: 2026-05-09
Reporter: Keith — submitted the registration form on production and saw "One or more validation errors occurred." with no detail.
Severity: BLOCKER — register endpoint had been broken from the day it shipped (M1, 2026-05-08). No customer could create an account on production.

## Symptom

Submitting the production register form (`Colo Solutions` / `keith@colosolutions.com` / valid-looking password) returned a 400 with a generic "One or more validation errors occurred." banner. No field-level errors were visible to the user.

Direct API probe (`curl -X POST /api/auth/register` with the same payload the frontend sent) returned:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Email":     ["The Email field is required."],
    "Password":  ["The Password field is required."],
    "Subdomain": ["The Subdomain field is required."]
  }
}
```

Three required fields missing — and the frontend's error display only surfaced the boilerplate `title`.

## Root cause — four independent bugs stacked

| # | Bug | Surface |
|---|---|---|
| 1 | Frontend `RegisterRequest` sent `{ tenantName, adminEmail, adminPassword }` but backend DTO expected `{ TenantName, Email, Password, Subdomain }`. ASP.NET Core JSON binding is case-insensitive but doesn't rename `adminEmail`→`Email`. → Email and Password landed null on the backend. | `src/ToastRevival.Dashboard/src/api/auth.ts` |
| 2 | Backend DTO required a `Subdomain` field. UI never collected one (form has only Organization / Email / Password). Even if bug 1 were fixed, registration would still 400 on missing Subdomain. | `src/ToastRevival.Api/DTOs/AuthDtos.cs` + `Register.tsx` |
| 3 | Backend `AuthResponse` record didn't include `Email`. Frontend `userFromResponse(res)` reads `res.email` to populate the AuthUser. → After login, user's email rendered as empty in the sidebar. | `src/ToastRevival.Api/DTOs/AuthDtos.cs` |
| 4 | Frontend `client.ts` extracted only `body.message ?? body.title` from error responses, ignoring the field-level `errors` map that ASP.NET Core ProblemDetails populates for `[Required]` / `[EmailAddress]` validation failures. → User saw boilerplate, not actionable errors. | `src/ToastRevival.Dashboard/src/api/client.ts` |

None of these would have been caught by Code Sweep — the bugs only surface when the frontend payload is actually sent to the live backend. The team had no automated tests on the register flow (INFO-M1-004 standing) and nobody had walked through register → onboarding → dashboard on the deployed environment until Keith tried it on 2026-05-09.

## Fixes

### Backend (`AuthDtos.cs`, `AuthController.cs`)
- `RegisterRequest`: `Subdomain` made optional (`string? Subdomain = null`). When omitted, the controller derives a slug from `TenantName` (`"Colo Solutions"` → `"colo-solutions"`). On collision, append a 4-char random alphanumeric suffix and retry up to 5 times.
- `AuthResponse`: added `string Email` field, populated from `user.Email!` in both Register and Login.
- Register controller: returns `BadRequest(new { errors = result.Errors.Select(e => e.Description).ToArray() })` on `UserManager.CreateAsync` failure — array shape that the new client.ts handler surfaces verbatim.

### Frontend (`auth.ts`, `AuthContext.tsx`, `client.ts`)
- `RegisterRequest` interface renamed: `{ tenantName, email, password, subdomain? }`.
- `AuthResponse` interface added `refreshToken`, `expiresAt`, and `email` fields to match the backend record.
- `AuthContext.register`: passes `{ tenantName, email, password }` to `authApi.register` (no client-side `subdomain` — backend derives it).
- `client.ts`: error response parser walks `body.errors` first. Handles both Record<string,string[]> (validation) and string[] (controller BadRequest). Falls back to `body.detail`, `body.message`, `body.title` in that order.

## Verification

### Curl
```
$ curl -X POST .../api/auth/register -d '{"tenantName":"Smoke Test"}'
{ "errors": { "Email": [...], "Password": [...] } }   # per-field, no Subdomain error

$ curl -X POST .../api/auth/register -d '{"tenantName":"Smoke Test Co","email":"smoke-test-1@example.test","password":"Smoketest1!"}'
{ "token": "eyJ...", "refreshToken": "...", "expiresAt": "2026-05-09T07:48:26Z",
  "userId": "9a917957-...", "tenantId": "0f261019-...",
  "email": "smoke-test-1@example.test", "role": "Admin" }   # full AuthResponse
```

### Playwright (UI smoke against production)
- Loaded `/register`. Filled `Smoke Test UI` / `smoke-ui-1@example.test` / `TestPassword1!`. Clicked Create account.
- Page navigated to `/` (Dashboard). Sidebar showed `Toast Notification` brand + `smoke-ui-1@example.test`. Empty-state cards rendered correctly. Zero console errors.
- Initial submit with password `weak` was blocked by HTML5 `minLength=8` (form never POSTed) — defense in depth before the API.

### Database hygiene
- Two smoke-test tenants (`Smoke Test Co`, `Smoke Test UI`) and their users + 12 default templates were deleted from production transactionally before declaring the fix done. `SELECT COUNT(*) FROM "Tenants"` → 0; `AspNetUsers` → 0; `NotificationTemplates` → 0.

## Standing rule (carry-forward)

**End-to-end smoke is not optional before any milestone closes that introduces a public surface.** Code Sweep's five-perspective review catches code-level defects but cannot catch payload-shape mismatches between frontend and backend. Every milestone that ships a frontend → backend interaction MUST include:

1. A `curl -X POST` of the exact JSON the frontend sends, against the deployed API.
2. A Playwright UI walkthrough of the happy path on the deployed URL.
3. Verification that the response shape matches what the frontend reads (every field on `AuthResponse` has a corresponding `interface AuthResponse` field — diff the two files in CR).

The register flow had been broken since M1 shipped on 2026-05-08, ~24 hours, undetected. We don't get to ship a marketing site that drives traffic to a register page that doesn't work.
