# Plan 5: Admin, Nightly Reset and Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish Milestone 1.
- "Try as admin" opens a real admin area: products (list, create, edit details, specs, price, stock, status) and orders (list, filter, detail, ship, deliver).
- All demo data resets every night at 02:00 UTC.
- Every public API is rate limited.
- The README shows the finished app.

**Architecture:**
- Admin endpoints live in their owning module under `/api/{module}/admin/*`. They are guarded by the `Admin` authorization policy, whose name moves to `Identity.Contracts`. Admin writes go through the existing aggregates, with xmin optimistic concurrency exposed to the client as `version`, because the demo admin is shared and edits can collide.
- The nightly reset is a host-level `DemoDataReset`:
  - It truncates every module schema in one transaction, keeping migration history, and restarts the sequences.
  - It then runs the existing seeders in module order.
  - It runs on a 02:00 UTC timer (`NightlyResetService`) and from the CLI with `dotnet Shop.Api.dll reset-demo`.
- Angular gets an `/admin` area (lazy, `adminGuard`) built from Material tables, forms and a side nav.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, ASP.NET Core rate limiter; Angular 22 + Material. No new packages.

**Spec:** `docs/superpowers/specs/2026-11-13-ecommerce-portfolio-design.md`. Sections this plan covers:
- §6 Ordering "Admin: list/filter orders, advance status"
- §6 Identity, demo admin
- §7 Admin area
- §8 seed and nightly reset, rate limiting
- §11 README
- §12 Milestone 1

**Plan 5 of 5 for Milestone 1.** Next: Milestone 2 (discount codes, compare, dashboard, image upload, admin cancel with Stripe refund, remaining ADRs, tracing).

**Out of scope here, with the plan that owns it:**
- **Admin cancel of an order:** Milestone 2. Cancelling a Paid order needs the refund flow. Cancelling a Placed order needs the open payment session closed first, otherwise the shopper can still pay a cancelled order. Both come with refunds.
- **Image upload:** Milestone 2. New products pick images from the catalog's existing photos.
- **Admin dashboard:** Milestone 2.
- **Discount codes:** Milestone 2.
- **Deleting products:** never. Archive instead; the spec blocks deletion for the demo admin.
- **Adding or removing option axes or variants after creation:** Milestone 2. Variants can be edited (price and stock); the axis set is fixed at creation.

## Global Constraints

- **Everything from Plans 1–4 still holds:**
  - `net10.0`, Nullable, TreatWarningsAsErrors, central package versions.
  - One schema per module; modules talk only through `{M}.Contracts`, and Contracts depend only on SharedKernel.
  - Handlers are `internal sealed`.
  - Errors are `Result` values mapped by `ErrorResults.ToProblem`.
  - Every non-GET `/api` call carries `X-Requested-With: voltlane`.
  - Endpoint changes regenerate `web/shop-web/openapi/v1.json` and run `npm run api:types`; both get committed.
  - Angular: standalone, signals, Material, no NgRx.
  - Files are written UTF-8 without BOM.
- **New packages:** none, backend or frontend.
- **Admin authorization:**
  - Policy name `"Admin"`, as `Identity.Contracts.AuthorizationPolicies.Admin`. Identity's existing registration uses this constant.
  - Every `/api/*/admin/*` endpoint requires it. A Customer gets 403; anonymous gets 401.
- **The demo admin is shared:**
  - Product and order edits are allowed and tolerated until the nightly reset.
  - There is no delete endpoint anywhere.
  - The basket stays read-only for it (Plan 3).
- **Optimistic concurrency:**
  - Admin product reads return `version` (the product row's xmin, a `uint`). Every admin product write sends the `version` it read.
  - A mismatch gets 409 `catalog.concurrent_update` with title "Someone else changed this product. Reload to see their changes."
- **Stock:** `StockOnHand` 0–100000, and never below `StockReserved` (409 `catalog.stock_below_reserved`).
- **Price:** 0.01–100000.00 USD, 2 decimals.
- **Order admin actions:** only `ship` (Paid → Shipped) and `deliver` (Shipped → Delivered). Anything else gets 409 `ordering.invalid_transition`.
- **Nightly reset:**
  - Config `DemoReset:Enabled` (default `false`; production compose `true`) and `DemoReset:TimeOfDayUtc` (default `02:00`).
  - The reset truncates **all** module data (catalog, basket, ordering, payments, identity, including registered users, refresh tokens, outbox and inbox), restarts every sequence in those schemas, then runs all seeders.
  - CLI: `dotnet Shop.Api.dll reset-demo` migrates, resets, seeds, logs, and exits 0.
- **Global rate limit:**
  - Every `/api/*` request is limited per client IP to `RateLimiting:ApiPerMinute` (default `300`). The webhook `/api/payments/stripe/webhook` and `/health*` are excluded.
  - Rejections are 429 ProblemDetails `rate_limited`, as today.
  - The test factory sets `ApiPerMinute = 100000`.
- **Admin seed:** the Ordering seeder adds **24** orders from **8** fictional customers (`alex.morgan@example.com` and 7 more `@example.com` addresses), spread over the last 30 days:
  - 6 Placed (no payment, `IsHistorical = true`), 6 Paid, 6 Shipped, 6 Delivered.
  - `IsHistorical = true` on all of them, so no events or stock changes.
- **Error codes** (ProblemDetails `type`):
  - `catalog.product_not_found` (404)
  - `catalog.concurrent_update` (409)
  - `catalog.stock_below_reserved` (409)
  - `catalog.invalid_product` (400; existing), `catalog.invalid_slug`, `catalog.invalid_specs`
  - `ordering.order_not_found` (404)
  - `ordering.invalid_transition` (409)
  - `validation.failed` (400)

## Review Focus

1. **Two people edit the same product as the shared demo admin.**
   - The second save gets 409 with the reload message.
   - The page shows it and offers "Reload".
   - Nothing is silently overwritten.
   - Tests in Task 2 (API) and Task 7 (page).
2. **An admin sets stock below what is currently reserved by open checkouts.**
   - The API answers 409 `catalog.stock_below_reserved` naming the SKU and the reserved count.
   - Stock never goes negative available.
   - Test in Task 2.
3. **The nightly reset runs while a shopper is mid-checkout, or while outbox processors are mid-batch.**
   - The reset still completes: one transaction, lock waits not errors, and a statement timeout of 60 s.
   - The seeders still produce the full catalog, the demo admin and the admin orders.
   - A reset that fails rolls back to the old data rather than leaving empty tables, is logged at Error, and retries at the next scheduled time.
   - Tests in Task 4.
4. **A customer, or a signed-out visitor, opens `/admin` or calls an admin endpoint.**
   - The API answers 403 or 401.
   - The SPA's `adminGuard` sends customers to `/` and signed-out visitors to `/login?returnUrl=/admin`.
   - Admin links never render for non-admins.
   - Tests in Task 2, Task 3 and Task 5.
5. **One IP hammers the catalog search.**
   - After `ApiPerMinute` requests in a minute, it gets 429 ProblemDetails.
   - Other IPs are unaffected.
   - The Stripe webhook is never limited.
   - Test in Task 1.

---

## File Structure

```
src/Modules/Identity/Identity.Contracts/AuthorizationPolicies.cs
src/Bootstrapper/Shop.Api/(RateLimitingConfiguration.cs, Program.cs, DemoReset/(DemoDataReset.cs, NightlyResetService.cs, DemoResetOptions.cs, ResetCommand.cs))
src/Modules/Payments/Payments.Application/Payments/ResolveOverduePayment.cs   (Processing warning)
src/Modules/Catalog/Catalog.Domain/Products/(Product.cs, ProductVariant.cs)    (admin mutations)
src/Modules/Catalog/Catalog.Application/Admin/(AdminProductDtos.cs, ListAdminProducts.cs, GetAdminProduct.cs, CreateProduct.cs, UpdateProduct.cs, UpdateVariant.cs, ListImageKeys.cs, validators)
src/Modules/Catalog/Catalog.Infrastructure/(Endpoints/CatalogAdminEndpoints.cs, Persistence/ProductRepository.cs)
src/Modules/Ordering/Ordering.Application/Admin/(AdminOrderDtos.cs, ListAdminOrders.cs, GetAdminOrder.cs, ShipOrder.cs, DeliverOrder.cs)
src/Modules/Ordering/Ordering.Infrastructure/(Endpoints/OrderingAdminEndpoints.cs, Seeding/OrderingSeeder.cs)
tests/Shop.IntegrationTests/(RateLimitTests.cs, Catalog/CatalogAdminTests.cs, Ordering/OrderingAdminTests.cs, DemoReset/DemoResetTests.cs)
web/shop-web/src/app/admin/(admin.routes.ts, admin-shell/, admin.guard.ts, orders/, order-detail/, products/, product-edit/, admin-api.service.ts)
docs/adr/0009-admin-and-nightly-reset.md, README.md, deploy/docker-compose.prod.yml, deploy/SERVER_SETUP.md
```

---

### Task 1: Global API rate limit, Processing alert, ADR wording

**Files:**
- Modify:
  - `src/Bootstrapper/Shop.Api/RateLimitingConfiguration.cs`: a global limiter keyed by client IP, with the exclusions listed in Global Constraints.
  - `tests/Shop.IntegrationTests/ShopApiFactory.cs`: `RateLimiting:ApiPerMinute = 100000`.
  - `src/Modules/Payments/Payments.Application/Payments/ResolveOverduePayment.cs`.
  - `docs/adr/0008-payments-stripe-checkout-choreography.md`: say "15-second timeout per attempt (the SDK retries, so a call can take about 45 s)".
- Test: `tests/Shop.IntegrationTests/RateLimitTests.cs` (extend), `tests/Shop.IntegrationTests/Payments/PaymentExpiryTests.cs` (extend)

**Interfaces:**
- `RateLimitingOptions.ApiPerMinute: int`, default 300.
- The limiter is a `GlobalLimiter` built with `PartitionedRateLimiter.Create<HttpContext, string>` and a fixed window of 1 minute, partitioned by client IP. Excluded requests get `RateLimitPartition.GetNoLimiter`.
- A Processing payment whose `ExpiresAtUtc` is more than `Payments:ProcessingAlertAfter` (default `01:00:00`) in the past is logged at **Warning** ("payment {PaymentId} for order {OrderId} still processing at the gateway {Age} after its deadline"), instead of Information. Payments not yet past that threshold still log at Information.

- [ ] **Step 1: Write the failing tests:**
  - `Api_requests_over_the_global_limit_get_429`: a factory subclass with `ApiPerMinute = 5`; 6 GETs to `/api/catalog/categories` give 5×200 then 429 with `type = rate_limited`.
  - `Webhook_and_health_are_not_globally_limited`: the same factory; 10 requests each to `/health` and `POST /api/payments/stripe/webhook` (Stripe-mode subclass, bad signature) never get 429.
  - `Processing_payment_long_past_deadline_logs_a_warning`: backdate `expires_at_utc` by 2 h with the gateway double reporting Processing; the captured logs contain a Warning naming the payment id.
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `dotnet test --solution VoltLane.slnx`. Expected: PASS.
- [ ] **Step 5:** Commit `feat(api): global per-IP API rate limit; warn on long-processing payments`.

---

### Task 2: Catalog admin API (products and variants)

**Files:**
- Create:
  - `Identity.Contracts/AuthorizationPolicies.cs`; Identity's `AuthenticationSetup` uses its `Admin` constant.
  - `Catalog.Application/Admin/*` and `Catalog.Infrastructure/Endpoints/CatalogAdminEndpoints.cs`.
  - `Catalog.Application/Abstractions/IProductRepository.cs` and its implementation, or extend the Task-2-of-Plan-4 reservation repository if one already loads products by id.
- Modify:
  - `Catalog.Domain/Products/Product.cs` and `ProductVariant.cs`: admin mutations.
  - `CatalogErrors`.
  - The OpenAPI snapshot and the types.
- Test: `tests/Catalog.UnitTests/Products/ProductAdminTests.cs`, `tests/Shop.IntegrationTests/Catalog/CatalogAdminTests.cs`

**Interfaces (domain):**
```csharp
public Result UpdateDetails(string name, string brand, string description, bool isFeatured, IReadOnlyList<string> imageKeys); // same rules as Create; ≥1 image key
public Result SetSpecs(Category category, IReadOnlyDictionary<string, SpecValue> specs);   // category.ValidateSpecs
public Result SetStatus(ProductStatus status);                                            // Draft|Active|Archived; Active needs ≥1 variant (always true)
public Result SetVariantPrice(Guid variantId, Money price);                               // 0.01–100000
public Result SetVariantStock(Guid variantId, int stockOnHand);                           // 0–100000, ≥ StockReserved → else CatalogErrors.StockBelowReserved(sku, reserved)
```

**Endpoints** under `/api/catalog/admin`, all `RequireAuthorization(AuthorizationPolicies.Admin)`:
- `GET /products?q=&status=&categorySlug=&page=1&pageSize=20`:
  - Returns `PagedResult<AdminProductSummaryDto { id, slug, name, brand, categoryName, status, isFeatured, variantCount, totalStock, totalReserved, minPrice, thumbnailUrl }>`.
  - Includes every status. Search is case-insensitive `ILIKE` on name/brand/SKU. Ordered by name.
- `GET /products/{id}`:
  - Returns `AdminProductDto { id, slug, name, brand, description, categoryId, categorySlug, status, isFeatured, imageKeys[], specs (name → value as in the public DTO), specSchema[] (the category's definitions), optionAxes[], variants[ { id, sku, options, price, stockOnHand, stockReserved } ], version }`.
- `POST /products`:
  - Body: `{ categorySlug, name, brand, description, isFeatured, imageKeys[], specs{}, optionAxes[ { name, values[] } ], variants[ { sku, options{}, price, stockOnHand } ] }`.
  - Returns 201 with `{ id, slug }` and a Location header. The new product's status is **Draft**.
  - The slug comes from the name (lower-case, non-alphanumerics → `-`, trimmed, max length); on collision, append `-2`, `-3` and so on.
- `PUT /products/{id}`:
  - Body: `{ name, brand, description, isFeatured, imageKeys[], specs{}, status, version }`.
  - Returns 200 with the fresh `AdminProductDto` (new `version`).
- `PUT /products/{id}/variants/{variantId}`:
  - Body: `{ price, stockOnHand, version }`.
  - Returns 200 with the fresh `AdminProductDto`.
- `GET /image-keys`: the distinct image keys used by any product, with `{ key, thumbnailUrl }`, so the admin can pick photos.
- `GET /categories`: returns `AdminCategoryDto[] { id, slug, name, specSchema[ { key, label, type: number|text|bool|enum, unit?, required, allowedValues? } ] }`, leaf categories only, ordered by name. The public category and facet DTOs don't carry the full schema.

**Concurrency:** the handler loads the product, compares `version` to the loaded xmin, and returns `catalog.concurrent_update` on a mismatch. A `DbUpdateConcurrencyException` at save time maps to the same error.

- [ ] **Step 1: Write the failing tests.**
  - Unit:
    - `SetVariantStock_below_reserved_fails_with_sku_and_reserved`
    - `SetVariantPrice_rejects_out_of_range`
    - `UpdateDetails_requires_an_image`
    - `SetSpecs_validates_against_category`
    - `SetStatus_moves_between_all_states`
  - Integration:
    - `Customer_gets_403_and_anonymous_401_on_every_admin_route` (a theory over all seven routes)
    - `List_includes_draft_and_archived_and_filters_by_status_and_query`
    - `Create_makes_a_draft_with_unique_slug`: create "Test Phone" twice; slugs are `test-phone` and `test-phone-2`.
    - `Create_with_invalid_specs_is_400`
    - `Update_with_stale_version_is_409_concurrent_update_and_changes_nothing`
    - `Update_publishes_and_archives`: an archived product disappears from public search; a published one appears.
    - `Variant_stock_below_reserved_is_409`: reserve 3 through Catalog, then set stock to 2.
    - `Variant_price_change_shows_in_public_product_and_basket`
    - `Demo_admin_can_edit` (signs in via `/api/identity/demo/admin`)
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement, then regenerate the snapshot and types.
- [ ] **Step 4:** Run `dotnet test --solution VoltLane.slnx`. Expected: PASS.
- [ ] **Step 5:** Commit `feat(catalog): admin product and variant API with optimistic concurrency`.

---

### Task 3: Ordering admin API (list, detail, ship, deliver)

**Files:**
- Create: `Ordering.Application/Admin/*`, `Ordering.Infrastructure/Endpoints/OrderingAdminEndpoints.cs`
- Modify: `OrderRepository` (admin queries), the OpenAPI snapshot and the types.
- Test: `tests/Shop.IntegrationTests/Ordering/OrderingAdminTests.cs`

**Endpoints** under `/api/ordering/admin`, all `RequireAuthorization(AuthorizationPolicies.Admin)`:
- `GET /orders?status=&q=&page=1&pageSize=20`:
  - Returns `PagedResult<AdminOrderSummaryDto { id, orderNumber, placedAtUtc, status, customerEmail, itemCount, total }>`, newest first.
  - `q` matches the order number (exact or prefix, case-insensitive) or the customer email (case-insensitive contains).
  - `pageSize` is at most 50.
- `GET /orders/{id}`: returns `AdminOrderDto`, which is the customer `OrderDto` plus `customerEmail`, `customerId` and `refundRequired`. Any customer's order is visible.
- `POST /orders/{id}/ship` and `POST /orders/{id}/deliver`:
  - Return 200 with the fresh `AdminOrderDto`.
  - A wrong state gets 409 `ordering.invalid_transition`; an unknown id gets 404.
  - Concurrent clicks: the xmin conflict maps to 409 `ordering.invalid_transition` after a reload shows the new state.

- [ ] **Step 1: Write the failing tests:**
  - `Customer_403_anonymous_401_on_admin_routes`
  - `Lists_all_customers_orders_newest_first_and_filters_by_status`
  - `Search_by_order_number_and_email`
  - `Ship_then_deliver_extends_history`
  - `Ship_a_Placed_order_is_409`
  - `Deliver_a_Paid_order_is_409`
  - `Unknown_order_is_404`
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement, then regenerate the snapshot and types.
- [ ] **Step 4:** Run `dotnet test --solution VoltLane.slnx`. Expected: PASS.
- [ ] **Step 5:** Commit `feat(ordering): admin order list, detail, ship and deliver`.

---

### Task 4: Nightly demo reset, CLI command, admin order seed

**Files:**
- Create: `src/Bootstrapper/Shop.Api/DemoReset/*` and `Ordering.Infrastructure/Seeding/OrderingSeeder.cs`, registered as an `IModuleSeeder`.
- Modify:
  - `Program.cs`: the `reset-demo` argument path, and the hosted service when it is enabled.
  - `deploy/docker-compose.prod.yml`: `DemoReset__Enabled: "true"`.
  - `deploy/SERVER_SETUP.md`: a manual reset command.
- Test: `tests/Shop.IntegrationTests/DemoReset/DemoResetTests.cs`

**Interfaces:**
- `DemoDataReset.ResetAsync(CancellationToken ct) -> Task`:
  1. Open one connection and one transaction, and `SET LOCAL statement_timeout = '60s'`.
  2. For each schema in `ModuleDatabaseRegistry` (via each context's `Schema`), `TRUNCATE` every table except `__ef_migrations_history`, as one `TRUNCATE … RESTART IDENTITY CASCADE` statement. Read the table names from `information_schema.tables`.
  3. For each sequence in those schemas (`information_schema.sequences`), run `ALTER SEQUENCE … RESTART`.
  4. Commit, then run every `IModuleSeeder` in module order, each in its own scope, as `Program.SeedAsync` does. Extract that loop into a shared `ModuleSeeding.RunAllAsync(IServiceProvider, ILogger, CancellationToken)` used by both.
  5. Log the duration at Information.

  Identifiers are quoted. Schema and table names come from the database catalog and the registry, never from input.
- `NightlyResetService : BackgroundService`:
  - It computes the next `TimeOfDayUtc` occurrence from `TimeProvider`, waits with `Task.Delay(…, clock, ct)`, and runs the reset.
  - On an exception it logs at Error and continues to the next day.
  - It is registered only when `DemoReset:Enabled` is true.
- **CLI:** when `args` contains `reset-demo`, the host migrates, calls `DemoDataReset.ResetAsync`, and exits without `app.Run()`.
- **`OrderingSeeder`:**
  - It does nothing if any order exists.
  - Otherwise it creates the admin spread from Global Constraints, using `ICatalogVariantQuery.GetSampleAsync(8)` and `Order.PlaceHistorical` plus the transitions with backdated times, and order numbers from the sequence.
  - It needs the catalog seeded first. `ModuleCatalog` order is Catalog, Basket, Ordering, so Ordering already runs after Catalog; keep it that way and assert it in a test.

- [ ] **Step 1: Write the failing tests** (they use a factory subclass with seeding on):
  - `Reset_wipes_user_data_and_reseeds`:
    1. Register a user, place an order, and edit a product's name.
    2. `ResetAsync`.
    3. The user is gone (login gives 401) and the product name is back to its seed value.
    4. The demo admin can sign in, and there are exactly 24 orders, the first numbered `VL-100001`.
  - `Reset_is_repeatable`: two resets in a row leave the same counts.
  - `Reset_rolls_back_when_a_truncate_fails`: make a table un-truncatable for the test, for example by holding a lock with a 1 s statement timeout injected by a test option. Data is unchanged and the exception propagates.
  - `Next_run_is_the_next_0200_utc`: a unit-style test of the schedule calculation; 01:59 → same day 02:00, and 02:00:01 → next day.
  - `Admin_seed_has_six_of_each_status_and_touches_no_stock`
  - `Seeders_run_catalog_before_ordering`
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement. Document `docker compose -f docker-compose.prod.yml exec api dotnet Shop.Api.dll reset-demo` in SERVER_SETUP.
- [ ] **Step 4:** Run `dotnet test --solution VoltLane.slnx`. Expected: PASS.
- [ ] **Step 5:** Commit `feat: nightly demo reset with CLI command; seeded orders for the admin`.

---

### Task 5: Angular admin shell, guard and orders pages

**Files:**
- Create: `web/shop-web/src/app/admin/(admin.routes.ts, admin.guard.ts, admin-api.service.ts, admin-shell/*, orders/*, order-detail/*)`
- Modify:
  - `app.routes.ts`: `path: 'admin'`, `canActivate: [adminGuard]`, `loadChildren` pointing to `admin.routes`.
  - `layout/header/*`: an "Admin" item in the account menu when `auth.isAdmin()`.
- Test: specs next to each.

**Behaviour:**
- **`adminGuard`:** waits for the startup refresh, as `authGuard` does. Signed out → `/login?returnUrl=/admin…`. Signed in but not admin → `/`.
- **Admin shell:**
  - Material sidenav with "Orders" and "Products", and a top bar reading "Admin · shared demo account".
  - A small info line: "Changes are visible to every visitor and reset nightly at 02:00 UTC."
  - Under 768px the nav collapses to a menu button.
  - `/admin` redirects to `/admin/orders`.
- **`/admin/orders`:**
  - Material table: number, date, customer email, status chip (reuse `shared/order-status-chip`), items, total.
  - A status filter (chips or a select), search with 300 ms debounce, and a paginator (20).
  - Rows link to the detail page. It has loading skeletons and the empty state "No orders match."
- **`/admin/orders/:id`:**
  - Order header, status timeline (reuse the timeline markup by extracting it to `shared/order-timeline`), lines, address and customer email.
  - "Mark as shipped" shows only for Paid and "Mark as delivered" only for Shipped. A button is disabled while its request is pending.
  - On 409 it shows the problem title inline and reloads.
  - `refundRequired` shows a warning banner "Refund needed — handle in the Stripe dashboard."
- **API service:** typed from `schema.ts`.

- [ ] **Step 1: Write the failing specs:**
  - Guard: the three outcomes.
  - The header shows Admin only for admins.
  - The list sends `status` and `q` (debounced) and pages.
  - Detail shows only the valid action, and calls ship then shows the new status.
  - A 409 shows the title.
- [ ] **Step 2:** Run `CI=true npx ng test --no-watch`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `CI=true npx ng test --no-watch && npx ng lint && npx ng build`. Expected: PASS with no new budget warnings.
- [ ] **Step 5:** Commit `feat(web): admin shell, guard, orders list and detail with ship/deliver`.

---

### Task 6: Angular admin products list and editor

**Files:**
- Create: `web/shop-web/src/app/admin/(products/*, product-edit/*, product-create/*, spec-field/*)`
- Test: specs next to each.

**Behaviour:**
- **`/admin/products`:**
  - Table: thumbnail, name, brand, category, status chip (Draft grey, Active green, Archived red), variants, stock (`totalStock` with "· N reserved" when above 0), and price ("from $x").
  - Search, a status filter, a paginator (20), and a "New product" button.
- **`/admin/products/:id` (edit):**
  - **Details card:** name, brand, description, featured toggle, status select, and an image picker (a grid of `GET /image-keys` thumbnails; select 1–4; the order of selection is the display order).
  - **Specs card:** a field per `specSchema` entry. Number gives a number input with the unit suffix; text gives text; bool gives a toggle; enum gives a select of the allowed values. Required markers follow the schema.
  - Save sends `version`.
  - **Variants table:** SKU, options, an editable price, an editable stock, a reserved count (read-only), and a per-row Save that sends `version`.
  - After any save, the page replaces its state with the returned DTO, so the version stays fresh.
  - On 409 `catalog.concurrent_update` it shows the title with a "Reload" button. On `stock_below_reserved` it shows the title next to that row.
  - A "View in shop" link to `/p/{slug}` shows when the status is Active.
- **`/admin/products/new`:**
  1. Pick the category from `GET /api/catalog/admin/categories` (Task 2); its `specSchema` drives the specs form.
  2. Enter details, images and specs.
  3. Define 0–2 option axes (name plus comma-separated values).
  4. The variant grid is generated as the cross product, with SKU (pre-filled `BRAND-NAME-VALUES`, upper-case, editable), price and stock per row.
  5. Create, then navigate to the edit page, where the status is Draft.
- **Errors:** 400 validation shows the field messages from ProblemDetails `errors` when present, and the title otherwise.

- [ ] **Step 1: Write the failing specs:**
  - The list filters and pages.
  - The editor renders spec fields by type.
  - Save sends `version`, and a 409 shows Reload.
  - The variant row save sends price, stock and version; stock below reserved shows inline.
  - Create generates a 2×2 grid for two axes with two values each, and submits the expected body.
- [ ] **Step 2:** Run `CI=true npx ng test --no-watch`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `CI=true npx ng test --no-watch && npx ng lint && npx ng build`. Expected: PASS.
- [ ] **Step 5:** Commit `feat(web): admin product list, editor and create flow`.

---

### Task 7: Polish, docs, ADR, PR

**Files:**
- Modify:
  - `pages/home/demo-panel/*`: the admin copy becomes "Shared admin account — manage products and ship orders. Resets nightly." "Try as admin" navigates to `/admin` after signing in.
  - `README.md`:
    - Demo section: what the admin can do, and the nightly reset at 02:00 UTC.
    - Screenshots: the controller captures them into `docs/screenshots/*.png` before the PR (home, product, cart, checkout progress, order detail, admin orders, admin product editor).
    - The "Not built yet" line now lists only Milestone 2 items.
    - Roadmap: tick Plan 5 and Milestone 1.
  - `deploy/SERVER_SETUP.md`: the reset command (Task 4).
- Create: `docs/adr/0009-admin-and-nightly-reset.md`. It covers:
  - Admin endpoints living in each module rather than a separate admin module.
  - Optimistic concurrency for the shared admin.
  - Why ship and deliver only (cancel and refund are Milestone 2).
  - Truncate-and-reseed vs per-row cleanup.
  - The reset in one transaction, and its failure behaviour.
  - The global rate limit and its exclusions.
- Test: update the demo-panel spec.

- [ ] **Step 1:** Make the changes. Run the full backend and frontend checks. Expected: PASS.
- [ ] **Step 2:** Commit `docs: admin and nightly reset ADR, README for Milestone 1`.
- [ ] **Step 3 (controller):** Run the final review and the fix wave. Run the local stack, then click through the admin flows and capture screenshots in Chrome. Commit the screenshots, push and open the PR. **Stop.** The user merges.
- [ ] **Step 4 (after merge):** Verify production:
  - Demo admin: edit a product's price and see it on the shop page; ship a seeded Paid order.
  - Run `reset-demo` only if the user asks; otherwise check the next morning that the 02:00 reset ran, from the logs or a reverted edit.
  - `/api/system/info` shows the merge SHA, and the console has no errors.
