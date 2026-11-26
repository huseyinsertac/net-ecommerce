# ADR-0007: Identity with an own User model, short-lived JWT and a rotating HttpOnly refresh cookie

Status: Accepted. Date: 2026-11-26

## Context

The shop needs accounts: register, sign in, a current user, an Admin role for catalog management, and a one-click demo for visitors who will not create an account. The SPA and the API share one origin behind Caddy. Whatever we pick must fit the module rules (schema per module, outbox and inbox on every module context) and stay simple to run as one container.

## Decision

**We own the `User` model.** ASP.NET Core Identity's EF stores need a context derived from `IdentityDbContext`, which collides with the `ModuleDbContext` base every module uses (schema, outbox, inbox). So `User` is a plain aggregate in `Identity.Domain` (roles, demo flag, refresh tokens) and EF-free. We still use Identity's `PasswordHasher<T>`: PBKDF2, versioned, and it reports when a hash needs upgrading. It lives in Infrastructure behind an `IPasswordHasher` port, because the domain must not reference `Microsoft.AspNetCore.*`.

**JWT access token plus a rotating refresh cookie.** The access token is an HS256 JWT valid for 15 minutes, held only in memory by the SPA, so there is nothing for an XSS payload to copy out of `localStorage`. The refresh token is 32 random bytes, stored as a SHA-256 hash, valid 7 days, and travels only in the `vl_refresh` cookie: HttpOnly, Secure, SameSite=Strict, Path `/api/identity`. Compared with `localStorage` tokens this keeps the long-lived secret away from scripts; compared with a cookie session it keeps API calls stateless and cross-checkable by the frontend, and SameSite=Strict plus the narrow path keeps CSRF exposure to the refresh/logout endpoints, which return nothing an attacker can read.

**Rotation and reuse detection.** Every refresh issues a new token and retires the old one, all in one token family. Presenting an already-rotated token is treated as theft: the whole family is revoked, so the thief's and the victim's sessions both end.

**30 s single-use reuse grace.** Two browser tabs refreshing together would otherwise look like theft. For `Jwt:RefreshReuseGraceSeconds` (30) a just-rotated token may be presented once more and gets one extra reissue. Trade-offs: a stolen token replayed inside that window succeeds once; and three or more simultaneous tabs can exceed the single grace and be signed out (the SPA mitigates this with one in-flight refresh per tab).

**`SessionVersion` as the concurrency guard.** Token rows have no concurrency token of their own, so every token mutation bumps `User.SessionVersion`, which is the user row's concurrency token. Two parallel rotations, a logout and a rotation, or a theft revocation racing a refresh then cannot both win. Handlers reload and retry up to three times; if the race is still lost they return `identity.concurrent_update` (409, "Please try again.") rather than a 500.

**Identical login failures.** Unknown email, wrong password, and accounts that cannot use a password (demo users) all return the same `identity.invalid_credentials`, and each path verifies against a precomputed dummy hash so the response time does not reveal which case it was.

**Demo accounts.** `POST /api/identity/demo/customer` creates a fresh throwaway customer (`demo-<8 hex>@demo.voltlane.test`, no password); `POST /api/identity/demo/admin` signs in the one seeded admin (`admin@demo.voltlane.test`, seeded idempotently by `IdentitySeeder`; 503 `identity.demo_unavailable` if it is missing). Demo users have no usable password, so `/login` can never sign into them. The `demo.voltlane.test` domain is reserved: `/register` answers it with the same 409 `identity.email_taken` an existing user gets. Because the customer endpoint creates rows anonymously, it is rate limited per client IP (5 per minute), and `/demo/admin` shares that same demo bucket; register and login share a 20 per minute budget, and refresh has its own 120 per minute bucket (`auth-refresh`, `RateLimiting:RefreshPerMinute`) because a signed-in SPA refreshes routinely and several users may share one IP. The IP is the real client address from `X-Forwarded-For` via the forwarded-headers middleware, and limits come from configuration. Rejections are `429` ProblemDetails (`rate_limited`) and create nothing.

## Consequences

- No dependency on an external identity service; the whole flow runs and is tested in one process.
- Tokens are never reachable from JavaScript except the short-lived access token in memory; a page reload costs one silent refresh.
- Rate limiting is in-memory per instance, which is correct for the single-container deployment and not enough for several replicas.
- We maintain password policy, reuse detection and lockout-style protections ourselves. There is no email verification, password reset or MFA.

## When to revisit

- More than one instance, or a need for SSO, MFA, social login or password reset: move authentication to an external provider (Keycloak or Microsoft Entra ID), validate its JWTs here, and keep only a profile projection in the `identity` schema. Rate limits would move to a shared store or the proxy.
- Reuse-grace sign-outs show up in practice: lengthen the grace or allow more than one reissue inside it.
