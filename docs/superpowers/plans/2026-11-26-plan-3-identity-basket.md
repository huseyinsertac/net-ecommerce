# Plan 3 — Identity & Basket Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Visitors can sign in with one click as a throwaway demo customer or as the shared demo admin, or register and log in normally. Anyone, signed in or not, can add product variants to a cart. The cart shows live prices and stock and follows the visitor into their account when they sign in.

**Architecture:**
- **Identity:** owns a small `User` aggregate with roles and a demo flag, and a `RefreshToken` entity. Passwords are hashed with ASP.NET Core Identity's `PasswordHasher<T>`. Access tokens are short-lived JWTs. Refresh tokens are opaque, stored hashed, rotated on every use, and revoked as a whole family when one is reused. The refresh token travels only in an HttpOnly cookie.
- **Basket:** owns a `Basket` aggregate keyed either by a user id or by an anonymous id kept in an HttpOnly cookie. It reads live variant data (price, stock, name, image) through a query interface in `Catalog.Contracts` that Catalog implements. Prices are never stored in the basket.
- **Angular:** gets an auth service, a bearer/refresh interceptor, login and register pages, a demo-accounts panel, a basket service, add-to-cart and a cart page.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, `Microsoft.Extensions.Identity.Core` (`PasswordHasher<T>`), `Microsoft.AspNetCore.Authentication.JwtBearer`, ASP.NET Core rate limiter; Angular 22 + Material.

**Spec:** `docs/superpowers/specs/2026-11-13-ecommerce-portfolio-design.md` (§6 Identity, §6 Basket, §7 Frontend: home demo panel, cart, login/register; §12 Milestone 1)

**Plan 3 of 5 for Milestone 1.** Next: Plan 4 (Ordering & Payments), Plan 5 (Admin, nightly reset, rate limiting everywhere, polish).

**Deliberately out of scope here, with the plan that owns it:**
- Pre-seeded historical orders for demo customers (Plan 4, since orders don't exist yet).
- Checkout. The cart's "Checkout" button stays disabled with "coming soon".
- Admin pages (Plan 5).
- Nightly deletion of demo users and stale baskets (Plan 5).
- Discount codes (Milestone 2).

## Global Constraints

- **Everything from Plans 1–2 still holds:**
  - `net10.0`, Nullable, TreatWarningsAsErrors, central package versions in `Directory.Packages.props`.
  - One schema per module (`identity`, `basket`), snake_case names, `{schema}.__ef_migrations_history`.
  - Modules talk only through `{M}.Contracts`; the architecture tests enforce this.
  - Handlers are `internal sealed`. Domain projects reference no EF, ASP.NET or `Microsoft.AspNetCore.*` namespaces. `PasswordHasher<T>` lives in `Microsoft.AspNetCore.Identity`, so it stays in Infrastructure.
  - Endpoints live in the module, under `/api/{module}`.
  - Errors are `Result` values mapped by `ErrorResults.ToProblem`, so the ProblemDetails `type` is the error code.
  - xUnit v3 + Shouldly + Testcontainers `postgres:18-alpine`.
  - Test commands: `dotnet test --project <path>` / `dotnet test --solution VoltLane.slnx`.
  - Angular: standalone, signals, Material, no NgRx; `CI=true npx ng test --no-watch`.
- **OpenAPI contract:**
  - Every task that adds or changes endpoints regenerates `web/shop-web/openapi/v1.json` (`UPDATE_OPENAPI_SNAPSHOT=1`), then `npm run api:types`, and commits both. The CI drift guard must stay green.
- **New packages:** only `Microsoft.Extensions.Identity.Core`, `Microsoft.AspNetCore.Authentication.JwtBearer` and `Microsoft.AspNetCore.Mvc.Testing` (already present). No new frontend packages.
- **Roles:** exactly `Customer` and `Admin`, carried in the JWT as `role` claims.
- **Tokens:**
  - **Access token:** JWT HS256, 15 minutes. Issuer `voltlane`, audience `voltlane-web`. Claims: `sub` (user id), `email`, `name` (display name), `role` (0..n), `demo` (`"true"` or `"false"`).
  - **Refresh token:** 32 random bytes, base64url, stored as a SHA-256 hash, valid 7 days. It travels only in the cookie `vl_refresh`: HttpOnly, Secure (except in Development), SameSite=Strict, Path=`/api/identity`.
- **Signing key:** config `Jwt:SigningKey`, at least 32 bytes. In Production a missing or short key fails startup. In production it comes from env `Jwt__SigningKey`, fed from the server's `.env` (`JWT_SIGNING_KEY`). Development and tests use a fixed non-secret key in `appsettings.Development.json` / the test factory.
- **Anonymous basket cookie:** `vl_basket`, a GUID. HttpOnly, Secure (except Development), SameSite=Lax, Path=`/api/basket`, 30 days. The API issues it on the first basket write. A missing or invalid value means "no basket"; it never causes a 500.
- **Basket limits:** quantity per line 1–10, at most 20 lines.
- **Error codes** (ProblemDetails `type`):
  - `identity.email_taken` (409)
  - `identity.invalid_credentials` (401, identical for unknown email and wrong password)
  - `identity.invalid_refresh_token` (401)
  - `identity.weak_password` (400)
  - `validation.failed` (400)
  - `basket.variant_unavailable` (404, unknown variant or product not Active)
  - `basket.insufficient_stock` (409)
  - `basket.too_many_lines` (409)
  - `basket.invalid_quantity` (400)
  - `basket.concurrent_update` (409)
- **Password rules:** at least 8 characters, with at least one letter and one digit. Email is trimmed, at most 254 chars, and unique case-insensitively, compared on a normalized upper-invariant copy.
- **Demo identities:**
  - Demo customers: `demo-<8 hex>@demo.voltlane.test`, display name `Demo Customer`, `IsDemo = true`, role Customer, no usable password.
  - Demo admin: `admin@demo.voltlane.test`, display name `Demo Admin`, roles Admin and Customer, `IsDemo = true`, no usable password. It is seeded once by `IdentitySeeder`.
  - Neither can log in through `/login`. Only the demo endpoints sign them in.

## Review Focus

1. **A refresh token is replayed after it was rotated**, for example when it is stolen or two tabs refresh at once. The replay fails, and the whole token family is revoked, so the attacker's and the victim's sessions both end. The test lives in Task 3.
   - The frontend must also not log a single user out just because two tabs refreshed together. Requests share one in-flight refresh per tab (Task 9).
2. **The demo-customer button is hammered (account spam):**
   - `POST /api/identity/demo/customer` is rate-limited per client IP (5 per minute). Excess requests get 429 with a ProblemDetails body, and no user rows are created. Test in Task 4.
   - The client IP comes from `X-Forwarded-For` through the forwarded-headers middleware that is already configured.
3. **A cart line whose variant later disappears, goes inactive, runs out or changes price:**
   - `GET /api/basket` never fails because of it. The line comes back flagged (`unavailable`, or `available` less than `quantity`), always with the live price.
   - The subtotal counts only purchasable lines.
   - Test in Task 7.
4. **Signing in while holding an anonymous cart that has the same variants as the account's cart.**
   - Quantities merge, capped at 10 per line and 20 lines.
   - The anonymous basket is deleted and its cookie expired.
   - Running the merge twice is a no-op.
   - Test in Task 7.
5. **A garbage, tampered or someone else's `vl_basket` cookie, and an expired access token on a basket call.**
   - A non-GUID or unknown cookie behaves like an empty cart.
   - A signed-in user's basket is always resolved from the token, never from the cookie.
   - An expired token gets 401. The frontend refreshes once and retries; if the refresh fails, it signs out and falls back to the anonymous cart without a toast storm.
   - Tests in Task 7 (API) and Task 9 (interceptor).

---

## File Structure

```
Directory.Packages.props                                    (+ 2 packages)
src/Bootstrapper/Shop.Api/Program.cs                        (UseAuthentication/UseAuthorization, UseRateLimiter)
src/Bootstrapper/Shop.Api/OpenApiConfiguration.cs           (Bearer security scheme)
src/Bootstrapper/Shop.Api/appsettings.json, appsettings.Development.json (Jwt section)
src/BuildingBlocks/Shop.Infrastructure.Common/Http/CurrentUser.cs   (ICurrentUser over HttpContext claims)
src/BuildingBlocks/Shop.Application.Abstractions/Identity/ICurrentUser.cs
src/Modules/Identity/Identity.Domain/Users/(User.cs, RefreshToken.cs, Roles.cs, IdentityErrors.cs, Email.cs)
src/Modules/Identity/Identity.Application/
  Abstractions/(IUserRepository.cs, IPasswordHasher.cs, ITokenIssuer.cs, IRefreshTokenFactory.cs)
  Auth/(Register.cs, Login.cs, Refresh.cs, Logout.cs, GetMe.cs, AuthDtos.cs, validators)
  Demo/(StartDemoCustomer.cs, StartDemoAdmin.cs)
src/Modules/Identity/Identity.Infrastructure/
  Persistence/(IdentityDbContext.cs, Configurations/*, Migrations/, UserRepository.cs)
  Security/(JwtOptions.cs, JwtTokenIssuer.cs, RefreshTokenFactory.cs, AspNetPasswordHasher.cs, AuthenticationSetup.cs)
  Endpoints/(IdentityEndpoints.cs, RefreshCookie.cs)
  Seeding/IdentitySeeder.cs
  IdentityModule.cs
src/Modules/Catalog/Catalog.Contracts/Variants/(ICatalogVariantQuery.cs, VariantSnapshot.cs)
src/Modules/Catalog/Catalog.Infrastructure/Reading/CatalogVariantQuery.cs
src/Modules/Basket/Basket.Domain/(Basket.cs, BasketLine.cs, BasketOwner.cs, BasketErrors.cs)
src/Modules/Basket/Basket.Application/(Abstractions/IBasketRepository.cs, GetBasket.cs, SetBasketItem.cs, RemoveBasketItem.cs, ClearBasket.cs, MergeBaskets.cs, BasketDtos.cs, BasketView.cs)
src/Modules/Basket/Basket.Infrastructure/(Persistence/*, Endpoints/(BasketEndpoints.cs, BasketCookie.cs), BasketModule.cs)
tests/Identity.UnitTests/, tests/Basket.UnitTests/           (new)
tests/Shop.IntegrationTests/Identity/*, Basket/*, Catalog/CatalogVariantQueryTests.cs, AuthTestClient.cs
web/shop-web/src/app/core/auth/(auth.service.ts, auth.interceptor.ts, auth.guard.ts)
web/shop-web/src/app/core/basket/basket.service.ts
web/shop-web/src/app/api/(identity-api.service.ts, basket-api.service.ts)
web/shop-web/src/app/pages/(login/, register/, cart/), pages/home/demo-panel/
web/shop-web/src/app/layout/header/*                        (account menu, live cart badge)
deploy/docker-compose.prod.yml, deploy/SERVER_SETUP.md       (JWT key)
docs/adr/0007-identity-own-user-model-jwt-refresh-cookie.md
```

---

### Task 1: Identity domain

**Files:**
- Create: `src/Modules/Identity/Identity.Domain/Users/*`
- Create: `tests/Identity.UnitTests/` (xUnit v3 + Shouldly; add to `VoltLane.slnx`), `tests/Identity.UnitTests/UserTests.cs`

**Interfaces (produced):**
```csharp
public static class Roles { public const string Customer = "Customer"; public const string Admin = "Admin"; }
public sealed record Email   // value object
{ string Value; string Normalized; static Result<Email> Create(string? raw); }   // trims; ≤254; must contain exactly one '@' with non-empty parts; Normalized = Value.ToUpperInvariant()
public sealed class RefreshToken : Entity<Guid>
{ Guid UserId; string TokenHash; Guid FamilyId; DateTime ExpiresAtUtc; DateTime CreatedAtUtc; DateTime? RevokedAtUtc; Guid? ReplacedById;
  bool IsActive(DateTime nowUtc); }
public sealed class User : AggregateRoot<Guid>
{ Email Email; string DisplayName; string? PasswordHash; bool IsDemo; IReadOnlyList<string> Roles; DateTime CreatedAtUtc;
  IReadOnlyList<RefreshToken> RefreshTokens;
  static Result<User> Register(Email email, string displayName, string passwordHash, DateTime nowUtc);   // role Customer
  static User CreateDemoCustomer(Email email, DateTime nowUtc);                                          // PasswordHash null, IsDemo
  static User CreateDemoAdmin(Email email, DateTime nowUtc);                                             // roles Admin+Customer, IsDemo
  bool CanLogInWithPassword { get; }                                                                     // PasswordHash != null && !IsDemo
  RefreshToken IssueRefreshToken(string tokenHash, DateTime nowUtc, TimeSpan lifetime, Guid? familyId = null);
  Result<RefreshToken> Rotate(string presentedHash, string newHash, DateTime nowUtc, TimeSpan lifetime);
  void RevokeFamily(Guid familyId, DateTime nowUtc);
  void RevokeAll(DateTime nowUtc); }
public static class IdentityErrors { /* codes from Global Constraints */ }
```
**Rules:**
- **DisplayName:** trimmed and 1–80 chars. Otherwise `validation`-type error `identity.invalid_display_name`.
- **`Rotate`:**
  - **Matching token, active:** revoke it (`RevokedAtUtc = now`, `ReplacedById = new.Id`), issue a new token in the **same family**, and return it.
  - **Matching token, already revoked (reuse):** call `RevokeFamily` for its family and return `identity.invalid_refresh_token`.
  - **Matching token, expired, or no match:** return `identity.invalid_refresh_token`.
- **Pruning:** `IssueRefreshToken` prunes tokens that expired more than 1 day ago, so the collection stays small.
- **Ids:** `Guid.CreateVersion7()`.

- [ ] **Step 1: Write the failing tests:**
  - Email: `Email_Create_TrimsAndNormalizes`; `Email_Create_Invalid_Fails` (theory: `""`, `"a"`, `"a@"`, `"@b"`, `"a@b@c"`, a 255-char string).
  - Register and demo users: `Register_AssignsCustomerRole`, `DemoCustomer_CannotLogInWithPassword`, `DemoAdmin_HasAdminAndCustomerRoles`.
  - Refresh tokens:
    - `Rotate_ActiveToken_ReturnsNewTokenInSameFamily_AndRevokesOld`
    - `Rotate_RevokedToken_RevokesWholeFamily_AndFails`, which asserts every token in the family has `RevokedAtUtc` set and the error code is `identity.invalid_refresh_token`
    - `Rotate_ExpiredToken_Fails`
    - `Rotate_UnknownHash_Fails`
    - `RevokeAll_RevokesEveryActiveToken`
- [ ] **Step 2:** Run `dotnet test --project tests/Identity.UnitTests`. Expected: build FAILS.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Re-run. Expected: PASS. `dotnet test --project tests/Shop.ArchitectureTests` still PASSES.
- [ ] **Step 5: Commit** `feat(identity): user aggregate with roles, demo flag and rotating refresh tokens`.

---

### Task 2: Identity persistence, password hashing, JWT issuing and host authentication

**Files:**
- Modify: `Directory.Packages.props` (+ `Microsoft.Extensions.Identity.Core` 10.0.x and `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.x, using the current 10.0 patch)
- Create: `Identity.Infrastructure/Persistence/{Configurations/UserConfiguration.cs, UserRepository.cs}`, migration `IdentityModel`
- Create: `Identity.Infrastructure/Security/*`
- Create: `Identity.Application/Abstractions/*`
- Create: `Shop.Application.Abstractions/Identity/ICurrentUser.cs`, `Shop.Infrastructure.Common/Http/CurrentUser.cs`
- Modify: `Program.cs`, `appsettings*.json`, `tests/Shop.IntegrationTests/ShopApiFactory.cs` (sets `Jwt:SigningKey` to a fixed 48-char test key)
- Test: `tests/Shop.IntegrationTests/Identity/IdentityPersistenceTests.cs`, `JwtSetupTests.cs`

**Interfaces (produced):**
```csharp
// Identity.Application.Abstractions
public interface IUserRepository { Task<User?> GetByIdAsync(Guid id, CancellationToken ct); Task<User?> GetByNormalizedEmailAsync(string normalized, CancellationToken ct);
  Task<User?> GetByRefreshTokenHashAsync(string tokenHash, CancellationToken ct); void Add(User user); Task SaveChangesAsync(CancellationToken ct); }
public interface IPasswordHasher { string Hash(User user, string password); bool Verify(User user, string hash, string password); }
public interface ITokenIssuer { AccessToken Issue(User user, DateTime nowUtc); }   public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);
public interface IRefreshTokenFactory { (string Raw, string Hash) Create(); string Hash(string raw); }
// Shop.Application.Abstractions.Identity
public interface ICurrentUser { bool IsAuthenticated { get; } Guid? UserId { get; } bool IsInRole(string role); }
// Identity.Infrastructure.Security
public sealed class JwtOptions { string Issuer = "voltlane"; string Audience = "voltlane-web"; string SigningKey; int AccessTokenMinutes = 15; int RefreshTokenDays = 7; }
public static class AuthenticationSetup { static IServiceCollection AddShopAuthentication(this IServiceCollection s, IConfiguration c, IHostEnvironment env); }
```
**Decisions:**
- **Model mapping:**
  - Table `identity.users` (`email`, `email_normalized` unique, `display_name`, `password_hash` nullable, `is_demo`, `roles` text[], `created_at_utc`, `xmin` concurrency).
  - Table `identity.refresh_tokens` (`token_hash` unique index, `family_id` index, `user_id` FK within the schema, cascade delete).
- **`AspNetPasswordHasher`:** wraps `Microsoft.AspNetCore.Identity.PasswordHasher<User>`. When `VerifyHashedPassword` returns `SuccessRehashNeeded`, it counts as true.
- **`AddShopAuthentication`:**
  - Binds `JwtOptions` with `ValidateOnStart`. Production requires `SigningKey` to be ≥ 32 bytes (UTF-8) and fails startup with a clear message otherwise.
  - Configures JwtBearer: validate issuer, audience, lifetime and signing key; `ClockSkew = 30s`; `MapInboundClaims = false`; `RoleClaimType = "role"`; `NameClaimType = "name"`.
  - Adds authorization policy `"Admin"` (requires role `Admin`).
  - It is called from `IdentityModule.Register`; the host only adds `app.UseAuthentication(); app.UseAuthorization();` before the module endpoints.
- **`CurrentUser`:** reads `sub` and `role` from `IHttpContextAccessor`. Registered scoped in `Program.cs` together with `AddHttpContextAccessor()`.
- **Signing key in OpenAPI:** add a Bearer `http` security scheme in `OpenApiConfiguration`. Endpoints that call `RequireAuthorization` get a security requirement through an operation transformer that checks the endpoint metadata for `IAuthorizeData`.

- [ ] **Step 1: Write the failing tests:**
  ```csharp
  [Fact] User_RoundTrips_WithRolesAndRefreshTokens()                 // save, new scope, load by normalized email
  [Fact] DuplicateNormalizedEmail_ViolatesUniqueIndex()               // "A@x.test" vs "a@x.test" → DbUpdateException
  [Fact] PasswordHasher_VerifiesOwnHash_RejectsWrongPassword()
  [Fact] IssuedAccessToken_ValidatesAgainstConfiguredParameters()    // decode with JwtSecurityTokenHandler / JsonWebTokenHandler: iss, aud, sub, role, demo, exp≈15min
  [Fact] Production_WithShortSigningKey_FailsStartup()               // factory variant: Jwt:SigningKey="short" → host start throws OptionsValidationException
  ```
- [ ] **Step 2:** Run `dotnet test --project tests/Shop.IntegrationTests --filter-class *Identity*` (or the whole project). Expected: FAIL.
- [ ] **Step 3:** Implement, then add the migration: `dotnet ef migrations add IdentityModel -p src/Modules/Identity/Identity.Infrastructure -s src/Bootstrapper/Shop.Api -o Persistence/Migrations`.
- [ ] **Step 4:** Re-run. Expected: PASS. Also run the architecture tests (PASS) and `dotnet build VoltLane.slnx` (0 warnings).
- [ ] **Step 5: Commit** `feat(identity): persistence, password hashing, JWT issuing and host authentication`.

---

### Task 3: Auth endpoints — register, login, refresh, logout, me

**Files:**
- Create: `Identity.Application/Auth/*`, `Identity.Infrastructure/Endpoints/{IdentityEndpoints.cs, RefreshCookie.cs}`
- Modify: `IdentityModule.cs`, the `web/shop-web/openapi/v1.json` snapshot, `web/shop-web/src/app/api/schema.ts`
- Test: `tests/Shop.IntegrationTests/Identity/AuthEndpointTests.cs`, `tests/Shop.IntegrationTests/AuthTestClient.cs` (helper: register + login + attach bearer; cookie-aware `HttpClient` via `CookieContainerHandler` / `WebApplicationFactoryClientOptions { HandleCookies = true }`)

**Interfaces (produced):**
```csharp
public sealed record AuthResponse(string AccessToken, DateTime ExpiresAtUtc, UserDto User);
public sealed record UserDto(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles, bool IsDemo);
public sealed record AuthResult(AuthResponse Response, string RefreshTokenRaw, DateTime RefreshExpiresAtUtc);   // application → endpoint; the raw token never goes in a body
commands: Register(string Email, string Password, string DisplayName) : ICommand<AuthResult>;  Login(string Email, string Password) : ICommand<AuthResult>;
          Refresh(string? RefreshTokenRaw) : ICommand<AuthResult>;  Logout(string? RefreshTokenRaw) : ICommand;   query: GetMe(Guid UserId) : IQuery<UserDto>
```
**Routes** (all under `/api/identity`, tag `Identity`):

| Method + path | Auth | Success | Notes |
|---|---|---|---|
| `POST /register` | anon | 201 `AuthResponse` + `Set-Cookie vl_refresh` | weak password → 400 `identity.weak_password`; dup email → 409 `identity.email_taken` |
| `POST /login` | anon | 200 `AuthResponse` + cookie | unknown email, wrong password, demo user, user without password → **the same** 401 `identity.invalid_credentials`. Always runs a hash verification (against a fixed dummy hash when the user is unknown) so response time does not reveal whether an email exists. |
| `POST /refresh` | anon (cookie) | 200 `AuthResponse` + rotated cookie | missing/invalid/expired/reused → 401 `identity.invalid_refresh_token` + cookie cleared |
| `POST /logout` | anon (cookie) | 204 + cookie cleared | revokes the presented token's family; succeeds even with no or invalid cookie |
| `GET /me` | bearer | 200 `UserDto` | 401 without token |

- **`RefreshCookie`:** `Append(HttpResponse, raw, expires)` / `Clear(HttpResponse)` / `Read(HttpRequest)`, with the exact attributes from Global Constraints. `Secure` follows `!env.IsDevelopment()`.
- **Validators (FluentValidation):** email required; password required with length 8–128; display name 1–80. Password strength (letter plus digit) is checked in the handler so it returns `identity.weak_password`.

- [ ] **Step 1: Write the failing tests:**
  ```csharp
  [Fact] Register_ReturnsTokens_AndSetsHttpOnlyStrictRefreshCookie()  // asserts Set-Cookie has HttpOnly, SameSite=Strict, Path=/api/identity
  [Fact] Register_DuplicateEmailDifferentCase_409EmailTaken()
  [Fact] Register_WeakPassword_400()                                    // "password" (no digit)
  [Fact] Login_WrongPassword_And_UnknownEmail_SameProblem401()        // identical type + title
  [Fact] Me_WithBearer_ReturnsUser_WithoutBearer_401()
  [Fact] Refresh_RotatesCookie_OldCookieThenRejected()
  [Fact] Refresh_ReplayOfRotatedToken_RevokesFamily_NewTokenAlsoDead() // Review Focus 1
  [Fact] Logout_RevokesRefresh_SubsequentRefresh401()
  [Fact] RefreshWithoutCookie_401_NoServerError()
  ```
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement. Then regenerate the snapshot (`UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --project tests/Shop.IntegrationTests --filter-class *OpenApiSnapshot*`, then rerun without the variable) and `npm run api:types` in `web/shop-web`.
- [ ] **Step 4:** Run `dotnet test --solution VoltLane.slnx`. Expected: all PASS.
- [ ] **Step 5: Commit** `feat(identity): register, login, refresh rotation with reuse detection, logout, me`.

---

### Task 4: Demo accounts, identity seeder, rate limiting on auth, ADR

**Files:**
- Create: `Identity.Application/Demo/*`, `Identity.Infrastructure/Seeding/IdentitySeeder.cs`
- Create: `docs/adr/0007-identity-own-user-model-jwt-refresh-cookie.md`
- Modify: `IdentityEndpoints.cs`, `IdentityModule.cs`, `Program.cs` (`app.UseRateLimiter()` after routing, before endpoints), snapshot and `schema.ts`
- Test: `tests/Shop.IntegrationTests/Identity/DemoAccountTests.cs`, `IdentitySeedTests.cs`

**Behaviour:**
- **`POST /api/identity/demo/customer`** (anon):
  - Creates `User.CreateDemoCustomer` with email `demo-<8 random hex>@demo.voltlane.test`. On a unique-index collision it retries up to 3 times.
  - Returns 201 `AuthResponse` and sets the refresh cookie.
- **`POST /api/identity/demo/admin`** (anon): signs in the seeded admin and returns 200 `AuthResponse` with the cookie. If the admin isn't seeded, it returns 503 with ProblemDetails `identity.demo_unavailable`.
- **`IdentitySeeder : IModuleSeeder`** (`Module = "identity"`): inserts the demo admin if no user with that normalized email exists. It is idempotent.
- **Rate limiting:**
  - **Policies:** register a partitioned fixed-window limiter named `"auth"`, keyed by `HttpContext.Connection.RemoteIpAddress`.
    - `demo/customer`: 5 requests per minute.
    - `login`, `register`, `refresh`: 20 requests per minute, sharing one policy `"auth-strict"`.
  - **Rejection:** set `RejectionStatusCode = 429` and an `OnRejected` that writes ProblemDetails `{ type: "rate_limited", title: "Too many requests. Please wait a minute." }`.
  - **Configuration:** limits come from config `RateLimiting:DemoCustomerPerMinute` and `RateLimiting:AuthPerMinute`, so the test factory can lower or raise them.
- **ADR-0007:** about one page, covering:
  - **Why we own the `User` model:** ASP.NET Identity's EF stores require an `IdentityDbContext` base, which conflicts with the module's `ModuleDbContext` base (schema, outbox and inbox). We still use Identity's `PasswordHasher`, which is PBKDF2, versioned and rehash-aware.
  - **Why JWT plus a rotating HttpOnly refresh cookie** rather than localStorage tokens or cookie sessions.
  - **Reuse detection.**
  - **Demo accounts.**
  - **What would change at scale:** an external identity provider such as Keycloak or Entra.

- [ ] **Step 1: Write the failing tests:**
  ```csharp
  [Fact] DemoCustomer_CreatesFreshUserEachTime_IsDemoCustomerRoleOnly()
  [Fact] DemoCustomer_CannotLogInWithPassword()                       // /login with its email + any password → 401 invalid_credentials
  [Fact] DemoAdmin_ReturnsAdminRole_SameUserEachTime()
  [Fact] DemoCustomer_SixthCallWithinMinute_429_NoUserCreated()      // dedicated factory with DemoCustomerPerMinute=5; count users before/after
  [Fact] Seeder_RunTwice_SingleAdmin()
  ```
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. Regenerate the snapshot and types. The shared `ShopApiFactory` raises both limits to 1000 so other tests never hit 429.
- [ ] **Step 4:** Run `dotnet test --solution VoltLane.slnx`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(identity): one-click demo customer and admin, seeder, auth rate limits, ADR-0007`.

---

### Task 5: Catalog variant query for other modules

**Files:**
- Create: `src/Modules/Catalog/Catalog.Contracts/Variants/{ICatalogVariantQuery.cs, VariantSnapshot.cs}`
- Create: `src/Modules/Catalog/Catalog.Infrastructure/Reading/CatalogVariantQuery.cs`; register it in `CatalogModule`
- Test: `tests/Shop.IntegrationTests/Catalog/CatalogVariantQueryTests.cs` (own dataset via `CatalogTestData`)

**Interfaces (produced):**
```csharp
namespace Catalog.Contracts.Variants;
public interface ICatalogVariantQuery { Task<IReadOnlyDictionary<Guid, VariantSnapshot>> GetAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken ct); }
public sealed record VariantSnapshot(Guid VariantId, Guid ProductId, string ProductSlug, string ProductName, string Brand, string Sku,
    string OptionsLabel, decimal UnitPrice, int Available, string? ThumbnailUrl, bool IsPurchasable);
```
- **`OptionsLabel`:** the variant's option values in the product's axis order, joined with `" · "` (e.g. `"256 GB · Black"`). It is empty for a single-variant product with no axes, and computed in Catalog, because only Catalog knows the axis order.
- **Query:** one Dapper query using `= ANY(@ids)`.
- **Unknown ids:** absent from the dictionary.
- **`IsPurchasable`:** true only when the product status is `Active`. Non-active variants are still returned so the basket can flag them.
- **`ThumbnailUrl`:** `/images/products/<first image key>-400.webp`.
- **Input cap:** at most 100 ids. More throws `ArgumentException`; callers never exceed 20.

- [ ] **Step 1: Write the failing tests:** `GetAsync_ReturnsLivePriceAvailableAndThumbnail`, `GetAsync_UnknownIds_Omitted`, `GetAsync_DraftProduct_ReturnedNotPurchasable`, `GetAsync_EmptyInput_EmptyResult_NoQuery`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the integration tests and the architecture tests. Expected: PASS. `Catalog.Contracts` must still depend only on SharedKernel and BCL types.
- [ ] **Step 5: Commit** `feat(catalog): variant snapshot query exposed through Catalog.Contracts`.

---

### Task 6: Basket domain

**Files:**
- Create: `src/Modules/Basket/Basket.Domain/*`
- Create: `tests/Basket.UnitTests/` (add to `VoltLane.slnx`), `BasketTests.cs`

**Interfaces (produced):**
```csharp
public sealed record BasketOwner   // exactly one of the two is set
{ Guid? UserId; Guid? AnonymousId; static BasketOwner ForUser(Guid id); static BasketOwner ForAnonymous(Guid id); }
public sealed class BasketLine : Entity<Guid> { Guid VariantId; int Quantity; DateTime AddedAtUtc; }
public sealed class Basket : AggregateRoot<Guid>
{ const int MaxQuantityPerLine = 10; const int MaxLines = 20;
  BasketOwner Owner; IReadOnlyList<BasketLine> Lines; DateTime UpdatedAtUtc;
  static Basket Create(BasketOwner owner, DateTime nowUtc);
  Result SetQuantity(Guid variantId, int quantity, DateTime nowUtc);   // 0 removes; 1..10 sets/adds; else basket.invalid_quantity; new line beyond 20 → basket.too_many_lines
  void Remove(Guid variantId, DateTime nowUtc);                       // no-op if absent
  void Clear(DateTime nowUtc);
  void MergeFrom(Basket other, DateTime nowUtc); }                    // sums quantities capped at 10; ignores lines beyond 20 (keeps existing first, then other's in AddedAt order)
```
- [ ] **Step 1: Write the failing tests:**
  - **SetQuantity:**
    - `SetQuantity_NewVariant_AddsLine`
    - `SetQuantity_Existing_Replaces`
    - `SetQuantity_Zero_Removes`
    - `SetQuantity_Eleven_InvalidQuantity`
    - `SetQuantity_Negative_InvalidQuantity`
    - `SetQuantity_TwentyFirstLine_TooManyLines`
    - `SetQuantity_ExistingLineWhenFull_Allowed`
  - **MergeFrom:**
    - `MergeFrom_SumsAndCaps`, where 7 + 6 gives 10
    - `MergeFrom_RespectsMaxLines`
    - `MergeFrom_Self_Throws`, since merging a basket into itself is a programming error
  - **Owner:** `Owner_ExactlyOneIdentifier`
- [ ] **Step 2:** Run `dotnet test --project tests/Basket.UnitTests`. Expected: build FAILS.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the unit tests and the architecture tests. Expected: PASS.
- [ ] **Step 5: Commit** `feat(basket): basket aggregate with line limits and merge`.

---

### Task 7: Basket persistence, application and endpoints

**Files:**
- Create: `Basket.Infrastructure/Persistence/{BasketConfiguration.cs, BasketRepository.cs}` and migration `BasketModel`
- Create: `Basket.Application/*`, `Basket.Infrastructure/Endpoints/{BasketEndpoints.cs, BasketCookie.cs}`
- Modify: `BasketModule.cs`, the snapshot, `schema.ts`
- Test: `tests/Shop.IntegrationTests/Basket/BasketEndpointTests.cs`

**Interfaces (produced):**
```csharp
public sealed record BasketDto(IReadOnlyList<BasketLineDto> Lines, int ItemCount, decimal Subtotal, bool HasIssues);
public sealed record BasketLineDto(Guid VariantId, string? ProductSlug, string? ProductName, string? Brand, string? Sku, string? OptionsLabel,
    string? ThumbnailUrl, decimal? UnitPrice, int Quantity, int Available, decimal? LineTotal, BasketLineIssue? Issue);
public enum BasketLineIssue { Unavailable, InsufficientStock }   // JSON camelCase strings: "unavailable" | "insufficientStock"
```
- **Persistence:**
  - Tables `basket.baskets` (`user_id` unique where not null, `anonymous_id` unique where not null, `updated_at_utc`, `xmin`) and `basket.basket_lines` (`variant_id` unique per basket).
  - A `DbUpdateConcurrencyException` maps to `basket.concurrent_update` (409).
- **Owner resolution** happens at the endpoint edge, in `BasketCookie.ResolveOwner(HttpContext, ICurrentUser)`:
  - When `ICurrentUser.IsAuthenticated`, the owner is `ForUser(sub)` and the cookie is ignored.
  - Otherwise the owner is `ForAnonymous(cookie guid)`. If the cookie is missing or invalid, reads use "no basket" and writes mint a new GUID and set the cookie.
- **View building** (`BasketView.Build(Basket?, IReadOnlyDictionary<Guid, VariantSnapshot>)`, in Application, pure and unit-testable):
  - **Line totals:** `LineTotal = UnitPrice × Quantity`, but only when there is no issue.
  - **Unavailable:** a missing snapshot or `!IsPurchasable` gives `Issue = Unavailable`, with only `VariantId` and `Quantity` guaranteed.
  - **InsufficientStock:** set when `Available < Quantity`.
  - **Totals:** `Subtotal` sums only lines without an issue. `ItemCount` sums all quantities. `HasIssues` is true when any line has an issue.
  - **OptionsLabel:** copied from `VariantSnapshot.OptionsLabel`.
  - **`basket.insufficient_stock` (409) on PUT:** its ProblemDetails title is `Only {available} left.` (or `Out of stock.` when 0), so the frontend can show it verbatim.

**Routes** (`/api/basket`, tag `Basket`, anonymous or bearer):

| Method + path | Body | Success | Errors |
|---|---|---|---|
| `GET /` | — | 200 `BasketDto` (empty when there is no basket) | — |
| `PUT /items/{variantId}` | `{ "quantity": int }` | 200 `BasketDto` | 404 `basket.variant_unavailable`; 409 `basket.insufficient_stock` when quantity > Available; 400 invalid quantity; 409 too many lines |
| `DELETE /items/{variantId}` | — | 200 `BasketDto` | — |
| `DELETE /` | — | 204 | — |
| `POST /merge` | — | 200 `BasketDto` | bearer required (401). Merges the `vl_basket` cookie's basket into the user's, deletes the anonymous one, expires the cookie. No cookie or no anonymous basket → returns the user's basket unchanged. |

- [ ] **Step 1: Write the failing tests:**
  ```csharp
  [Fact] AnonymousPut_SetsBasketCookie_GetReturnsLineWithLivePrice()
  [Fact] Put_QuantityAboveAvailable_409InsufficientStock()
  [Fact] Put_DraftProductVariant_404VariantUnavailable()
  [Fact] Get_AfterVariantGoesOutOfStockAndPriceChanges_FlagsLine_UsesLivePrice_NoError()   // Review Focus 3 (mutate via CatalogDbContext in test)
  [Fact] Get_GarbageCookie_EmptyBasket_200()                          // Review Focus 5
  [Fact] AuthenticatedRequest_IgnoresAnonymousCookie()                // Review Focus 5
  [Fact] Merge_SumsCapsDeletesAnonymous_ExpiresCookie_SecondMergeNoOp() // Review Focus 4
  [Fact] Merge_WithoutBearer_401()
  [Fact] TwentyFirstDistinctVariant_409TooManyLines()
  ```
  Also add unit tests for `BasketView.Build` in `tests/Basket.UnitTests/BasketViewTests.cs`: `Unavailable_LineExcludedFromSubtotal`, `InsufficientStock_Flagged`, `MissingSnapshot_Unavailable_NoPrice`. Add `GetAsync_OptionsLabel_InAxisOrder` to Task 5's tests.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement and add the migration. Regenerate the snapshot and types.
- [ ] **Step 4:** Run `dotnet test --solution VoltLane.slnx`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(basket): persistent anonymous and user baskets with live prices and merge`.

---

### Task 8: Deployment wiring for the signing key

**Files:**
- Modify: `deploy/docker-compose.prod.yml` (api: `Jwt__SigningKey: ${JWT_SIGNING_KEY:?JWT_SIGNING_KEY is required}`)
- Modify: `deploy/docker-compose.yml` (local: a fixed dev key, labelled non-secret in a comment)
- Modify: `deploy/SERVER_SETUP.md` (new step: `echo "JWT_SIGNING_KEY=$(openssl rand -base64 48)" >> /opt/voltlane/.env`)
- Modify: `.github/workflows/deploy.yml` only if the remote script has to export the variable. Check how `POSTGRES_PASSWORD` reaches compose and mirror it.

- [ ] **Step 1:** Run `cd deploy && docker compose config`. Expected: renders, with the api env containing `Jwt__SigningKey`. Then run `docker compose -f docker-compose.prod.yml config` with `JWT_SIGNING_KEY` unset. Expected: fails with the "required" message.
- [ ] **Step 2:** Run `docker compose up -d --build` and curl `POST /api/identity/demo/customer`. Expected: 201, `set-cookie: vl_refresh=…; HttpOnly`. Then `GET /api/identity/me` with the bearer. Expected: 200. Then `docker compose down -v`.
- [ ] **Step 3: Commit** `chore(deploy): JWT signing key from server environment`.
- [ ] **Step 4: USER ACTION before merging this plan's PR.** On the server, run the SERVER_SETUP command. The controller stops and asks the user; nobody else touches production.

---

### Task 9: Frontend — authentication

**Files:**
- Create: `src/app/api/identity-api.service.ts`, `src/app/core/auth/{auth.service.ts, auth.interceptor.ts, auth.guard.ts}`
- Create: `src/app/pages/{login,register}/*`, `src/app/pages/home/demo-panel/*`
- Modify: `app.config.ts` (interceptor order `[authInterceptor, problemInterceptor]`), `app.routes.ts` (`login`, `register`), `layout/header/*` (account menu), `pages/home/*` (demo panel under the hero)

**Interfaces (produced):**
```ts
export type CurrentUser = components['schemas']['UserDto'];
@Injectable({ providedIn: 'root' }) export class AuthService {
  readonly user: Signal<CurrentUser | null>;  readonly isAdmin: Signal<boolean>;  readonly ready: Signal<boolean>;  // ready = initial silent refresh finished
  accessToken(): string | null;
  login(email: string, password: string): Promise<void>;  register(email: string, password: string, displayName: string): Promise<void>;
  startDemoCustomer(): Promise<void>;  startDemoAdmin(): Promise<void>;  logout(): Promise<void>;
  refresh(): Promise<boolean>;   // single-flight: concurrent callers share one in-flight request
  readonly signedIn: Observable<CurrentUser>; }  // emits after every successful sign-in (Task 10 merges the basket on it)
export const authInterceptor: HttpInterceptorFn;   // adds Bearer to /api/* except /api/identity/{login,register,refresh,logout,demo/*}; on 401 → refresh() once → retry; refresh fails → clear session, rethrow
export const authGuard: CanActivateFn;            // waits for ready(); not signed in → /login?returnUrl=…
```
- **Session storage:**
  - The access token lives only in memory. On app start, `AuthService` calls `refresh()` once, provided through `provideAppInitializer`, so a reload keeps the session.
  - If that refresh fails, the app stays signed out quietly. The interceptor must not toast that 401: it is a 4xx, so `problemInterceptor` already ignores it.
- **Pages:**
  - **Login:** a Material form (email, password) with inline errors from ProblemDetails, a link to register, and on success a redirect to `returnUrl` or `/`.
  - **Register:** display name, email and password, with the hint "At least 8 characters, a letter and a digit."
  - Both carry a note that one-click demo accounts are available on the home page.
- **Header account menu:**
  - **Signed out:** a "Sign in" button.
  - **Signed in:** an avatar button showing the display name's first letter. Its menu lists the display name and email, a "Demo account" chip when `isDemo`, an "Admin" chip when the user is an admin, and "Sign out".
- **Demo panel (home):**
  - A Material card titled "Try it out — no sign-up needed" with two buttons: "Try as customer" and "Try as admin".
  - Short copy: customer accounts are private and temporary, and admin access is shared and reset nightly.
  - While a request is in flight both buttons are disabled and show a spinner. A 429 shows "Too many demo sign-ins from your network — please wait a minute."

- [ ] **Step 1: Write the failing tests:**
  - **Interceptor:**
    - `adds bearer to /api/catalog`
    - `does not add bearer to /api/identity/refresh`
    - `401 → refresh → retries once with new token`
    - `two parallel 401s trigger one refresh` (Review Focus 1)
    - `refresh fails → session cleared, original error rethrown`
  - **Auth service:** `login sets user and token`, `logout clears user even if request fails`.
  - **Demo panel:** `clicking Try as customer calls startDemoCustomer and navigates home`, `429 shows rate-limit message`.
  - **Header:** `shows Sign in when signed out, display name initial when signed in`.
  - **Guard:** `redirects to /login with returnUrl`.
- [ ] **Step 2:** Run `CI=true npx ng test --no-watch`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run tests, lint and build. Expected: PASS.
- [ ] **Step 5: Commit** `feat(web): sign-in, registration, one-click demo accounts and account menu`.

---

### Task 10: Frontend — basket

**Files:**
- Create: `src/app/api/basket-api.service.ts`, `src/app/core/basket/basket.service.ts`, `src/app/pages/cart/*`
- Modify: `pages/product-detail/*` (enable "Add to cart"), `layout/header/*` (live badge, cart link), `app.routes.ts` (`cart`)

**Interfaces (produced):**
```ts
export type Basket = components['schemas']['BasketDto'];
@Injectable({ providedIn: 'root' }) export class BasketService {
  readonly basket: Signal<Basket | null>;  readonly count: Signal<number>;   // count = itemCount, 0 when null
  load(): Promise<void>;  setQuantity(variantId: string, quantity: number): Promise<void>;  remove(variantId: string): Promise<void>;
  clear(): Promise<void>;  mergeAfterSignIn(): Promise<void>; }   // subscribes to AuthService.signedIn; on sign-out reloads (anonymous cart)
```
- **Product detail:**
  - "Add to cart" is enabled when a variant is selected and `available > 0`. It adds 1 to the current quantity in the cart for that variant, capped at 10.
  - On success it shows a snackbar "Added to cart" with a "View cart" action.
  - A 409 `basket.insufficient_stock` shows the ProblemDetails title (`Only N left.`) in the snackbar.
  - When the variant is out of stock, the button reads "Out of stock" and is disabled.
- **Cart page `/cart`, title "Cart · VoltLane":**
  - **Lines:** each row shows a thumbnail, name, options, unit price, a quantity stepper (1–10, plus a delete button) and the line total.
  - **Flagged lines:** these show a warning row instead of a total: "No longer available — remove it" or "Only N left — lower the quantity".
  - **Summary card:** subtotal and a disabled "Checkout — coming soon" button with the caption "Checkout arrives with payments." When `hasIssues` is true, it adds "Some items need attention."
  - **States:** an empty state "Your cart is empty" with a "Start shopping" button, and a skeleton while loading.
  - **Responsive:** the table turns into stacked cards below 600px.
- **Header:** the cart icon links to `/cart`, and its badge equals `count()`; it stays hidden at 0, as it is today.

- [ ] **Step 1: Write the failing tests:**
  - **BasketService:**
    - `count reflects itemCount`
    - `mergeAfterSignIn posts merge then sets basket`
    - `sign-out reloads anonymous basket`
  - **Product detail:**
    - `Add to cart enabled for in-stock variant and calls setQuantity(current+1)`
    - `out-of-stock variant shows disabled Out of stock`
  - **Cart page:**
    - `renders lines and subtotal`
    - `flagged unavailable line shows remove hint and no line total`
    - `stepper calls setQuantity`
    - `empty state`
- [ ] **Step 2:** Run tests. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run tests, lint and build. Expected: PASS. Then do the manual check against compose. With a browser (Claude in Chrome when available), go through: add as anonymous → sign in as demo customer → cart merged → sign out → anonymous cart empty → sign in again → cart persists. Record what was checked.
- [ ] **Step 5: Commit** `feat(web): add to cart, cart page, live cart badge and merge on sign-in`.

---

### Task 11: Ship it

**Files:**
- Modify: `README.md`:
  - "Demo accounts" section: one-click buttons on the home page, no credentials needed.
  - Feature list: accounts, cart.
  - Link to ADR-0007.
  - Roadmap: tick Plan 3.
- Modify: `.github/workflows/ci.yml` only if a new test project is not picked up by `--solution`.

- [ ] **Step 1:** Run the full local verification:
  - `dotnet build VoltLane.slnx` (0 warnings)
  - `dotnet test --solution VoltLane.slnx`
  - In `web/shop-web`: `npm ci && npx ng lint && CI=true npx ng test --no-watch && npx ng build`
  - Compose smoke test: demo customer → `/me` → add to cart → `GET /api/basket`

  Expected: all pass.
- [ ] **Step 2:** Update the README, then commit `docs: demo accounts, cart, identity ADR, roadmap`.
- [ ] **Step 3:** Push the branch and open a PR to `main`:
  - Body: a summary, the test evidence, a **pre-merge checklist item** "`JWT_SIGNING_KEY` added to the server `.env`" (Task 8 Step 4), and the attribution line.
  - Wait for CI to go green.
- [ ] **Step 4: STOP.** The user adds the server key, then merges.
- [ ] **Step 5:** After the merge, verify production at https://204.168.194.99.sslip.io:
  - `POST /api/identity/demo/customer` returns 201 with a `Secure; HttpOnly; SameSite=Strict` cookie.
  - `/api/identity/me` with the bearer returns 200.
  - `PUT /api/basket/items/<variant>` with the bearer returns 200; `GET` shows the line.
  - `/api/system/info` shows the merge SHA.
  - In a browser: the demo buttons, add to cart, the cart page and the badge work, with no console errors.
