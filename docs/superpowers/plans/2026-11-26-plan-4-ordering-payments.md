# Plan 4: Ordering and Payments Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A signed-in visitor checks out their cart with a shipping address, pays on Stripe's test-mode Checkout page with card `4242 4242 4242 4242`, and sees the order go Placed → Paid in their order history with a status timeline. Out-of-stock, abandoned and failed payments cancel the order and release the stock on their own. Every demo customer starts with three historical orders.

**Architecture:**
- **The spec's choreography (§6 "The cross-module flow"), built on the existing outbox/inbox:**
  1. Ordering places the order and publishes `OrderPlaced`.
  2. Catalog reserves the stock: `StockReserved` or `StockReservationFailed`.
  3. Payments opens a checkout session at the gateway when stock is reserved.
  4. The Stripe webhook (or the expiry job) publishes `PaymentSucceeded`, `PaymentFailed` or `PaymentExpired`.
  5. Ordering and Catalog react: mark Paid and commit the stock, or cancel and release it.
- **How modules read each other:** only through query interfaces in `{M}.Contracts`.
  - Ordering uses `Basket.Contracts`, `Catalog.Contracts` and `Payments.Contracts`.
  - Payments uses `Ordering.Contracts`.
- **Payment gateways:** `Payments:Provider` picks `Stripe` (production) or `Fake`. The fake one is used in development, in tests, and by anyone who clones the repo without Stripe keys. It sends the visitor to a simulated payment page with "Pay" and "Decline" buttons.
- **Client side:** the client polls `checkout-status` (500 ms, 30 s) as the spec decides.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, `Stripe.net` 53.2.0; Angular 22 + Material.

**Spec:** `docs/superpowers/specs/2026-11-13-ecommerce-portfolio-design.md`. Sections used:
- §6 Ordering, Payments, cross-module flow
- §6 Identity: demo customers get historical orders
- §7 cart, checkout, confirmation, order history and detail
- §8 seed
- §9 secrets
- §10 the checkout integration test

**Plan 4 of 5 for Milestone 1.** Next: Plan 5 (admin pages including order status actions, nightly reset, polish).

**Deliberately out of scope, with the plan that owns it:**
- Admin order list and actions: ship, deliver, cancel. The domain transitions are built and unit-tested here; the endpoints and UI come in Plan 5.
- Refund of a cancelled paid order (Milestone 2, and it needs an admin cancel).
- Discount codes (Milestone 2). `Order` carries `Discount = 0` so totals already have the slot.
- Freeing reservations stuck because a payment session never opened (for example Stripe down for longer than the outbox retries): the nightly reset in Plan 5. It is logged and parked here.

## Global Constraints

- **Everything from Plans 1–3 still holds:**
  - `net10.0`, Nullable, TreatWarningsAsErrors, central package versions.
  - One schema per module, snake_case names; modules talk only through `{M}.Contracts`, and Contracts depend only on SharedKernel (architecture tests).
  - Handlers are `internal sealed`; domain projects have no EF or ASP.NET references.
  - Errors are `Result` values mapped by `ErrorResults.ToProblem`, so the ProblemDetails `type` is the error code.
  - xUnit v3 + Shouldly + Testcontainers `postgres:18-alpine`.
  - Every non-GET `/api` call carries `X-Requested-With: voltlane` (the SPA's interceptor already adds it).
  - Endpoint changes regenerate `web/shop-web/openapi/v1.json` (`UPDATE_OPENAPI_SNAPSHOT=1`) and run `npm run api:types`; both get committed.
- **New packages:** only `Stripe.net` 53.2.0, referenced by `Payments.Infrastructure` and nothing else. No new frontend packages.
- **Integration events** are `sealed record`s deriving from `IntegrationEvent`, one file each under `{M}.Contracts/Events/`. Handlers are registered only through `AddInboxHandlers<TContext>`.
- **Money:** USD; `decimal` with 2 places in the domain. Stripe gets integer cents (`(long)(amount * 100)`).
- **Checkout:**
  - Login required; there is no guest checkout.
  - `POST /api/ordering/checkout` returns `202 Accepted` with `{ orderId, orderNumber }`.
  - The request needs an `Idempotency-Key` header holding a GUID. A repeat key from the same customer returns the same order.
- **Order numbers:** `VL-` followed by a value from the Postgres sequence `ordering.order_number_seq`, which starts at `100001`. Example: `VL-100001`.
- **Order states:** `Placed → Paid → Shipped → Delivered`, plus `Cancelled`, allowed from Placed, or from Paid with `RefundRequired = true`. Cancellation reasons: `OutOfStock`, `PaymentFailed`, `PaymentExpired`, `Admin`.
- **Payment states:** `Pending → Succeeded | Failed | Expired`, and only from Pending. The timeout is `Payments:Timeout`, default `00:30:00`. Stripe's minimum session lifetime is 30 minutes, so the Stripe provider refuses anything shorter.
- **Checkout status values:** `Pending`, `OutOfStock`, `ReadyToPay` (with `paymentUrl`), `Paid`, `Failed` (with `reason`).
- **Stripe safety:**
  - In Production `Payments:Provider` must be `Stripe`, `Stripe:SecretKey` must start with `sk_test_`, and `Stripe:WebhookSecret` must start with `whsec_`. Anything else fails startup.
  - The demo can never charge a real card.
  - Keys come from env `Stripe__SecretKey` and `Stripe__WebhookSecret`, fed by `STRIPE_SECRET_KEY` and `STRIPE_WEBHOOK_SECRET` in the server's `.env`. They are never committed.
- **Public base URL:** `App:PublicBaseUrl` (no trailing slash) builds the Stripe return URLs. Production compose sets `App__PublicBaseUrl: https://${SITE_ADDRESS}`. Development uses `http://localhost:4200`.
- **Error codes** (ProblemDetails `type`):
  - `ordering.empty_basket` (409)
  - `ordering.basket_has_issues` (409): some line is unavailable or short on stock
  - `ordering.price_changed` (409): the `expectedTotal` sent by the client differs from the live total
  - `ordering.missing_idempotency_key` (400)
  - `ordering.order_not_found` (404, also for another customer's order)
  - `ordering.invalid_transition` (409)
  - `payments.payment_not_found` (404)
  - `validation.failed` (400)
- **Rate limits:** `checkout`, 10 per minute per IP (`RateLimiting:CheckoutPerMinute`). The test factory raises it.

## Review Focus

1. **A double-clicked or retried "Place order".**
   - The same `Idempotency-Key` yields one order and the same 202 body. Two concurrent requests with one key also yield one order: the unique index wins and the loser returns the winner's order.
   - Test in Task 4.
2. **Two customers buy the last unit at the same moment.**
   - Exactly one reservation succeeds. The other order becomes `Cancelled` (`OutOfStock`). `StockReserved` never exceeds `StockOnHand`.
   - The xmin conflict is retried by the outbox and is never swallowed.
   - Tests in Task 2 (Catalog) and Task 6 (end to end).
3. **Stripe webhooks arrive twice, for an unknown session, or with a bad signature.**
   - A duplicate event id is a no-op (200).
   - An unknown session gets 200 with a warning log, so Stripe stops retrying.
   - A bad or missing signature gets 400 and changes nothing.
   - `payment_status = unpaid` on `completed` waits for the async event.
   - Tests in Task 5.
4. **The visitor abandons the payment page.**
   - After the timeout the payment is `Expired`, the order is `Cancelled` (`PaymentExpired`) and the reserved stock returns to available.
   - With Stripe, the expiry job asks Stripe first: a session Stripe reports as paid is marked Succeeded, not Expired.
   - Tests in Task 5 (job) and Task 6 (end to end).
5. **The cart changes between viewing it and pressing "Place order"** (price edit, item sold out or archived).
   - The API answers 409 (`price_changed` or `basket_has_issues`) and creates no order.
   - The page explains it and refreshes the cart.
   - Tests in Task 4 (API) and Task 8 (page).

---

## File Structure

```
Directory.Packages.props                                   (+ Stripe.net)
src/BuildingBlocks/Shop.Infrastructure.Common/Outbox/*      (per-message retry schedule)
src/BuildingBlocks/Shop.Infrastructure.Common/Http/CsrfHeader.cs   (exempt paths: the Stripe webhook)
src/BuildingBlocks/Shop.Application.Abstractions/Identity/ICurrentUser.cs   (+ Email)
src/Modules/*/…/Migrations/*_OutboxRetrySchedule.cs         (one per module)
src/Modules/Ordering/Ordering.Contracts/Events/(OrderPlaced.cs), Orders/(IOrderPaymentQuery.cs, OrderPaymentDetails.cs)
src/Modules/Catalog/Catalog.Contracts/Events/(StockReserved.cs, StockReservationFailed.cs)
src/Modules/Payments/Payments.Contracts/Events/(PaymentSucceeded.cs, PaymentFailed.cs, PaymentExpired.cs), Payments/(IPaymentCheckoutQuery.cs)
src/Modules/Identity/Identity.Contracts/Events/DemoCustomerCreated.cs
src/Modules/Basket/Basket.Contracts/(IBasketCheckoutQuery.cs, BasketItem.cs)
src/Modules/Catalog/Catalog.Domain/Reservations/(StockReservation.cs, ReservationLine.cs, ReservationStatus.cs)
src/Modules/Catalog/Catalog.Application/Reservations/(ReserveStockOnOrderPlaced.cs, CommitStockOnPaymentSucceeded.cs, ReleaseStockOnPaymentEnded.cs)
src/Modules/Ordering/Ordering.Domain/Orders/(Order.cs, OrderLine.cs, ShippingAddress.cs, OrderStatus.cs, CancellationReason.cs, StatusChange.cs, OrderErrors.cs)
src/Modules/Ordering/Ordering.Application/(Checkout/, Orders/, Reactions/, Demo/)
src/Modules/Ordering/Ordering.Infrastructure/(Persistence/*, Endpoints/OrderingEndpoints.cs, Reading/OrderPaymentQuery.cs, OrderingModule.cs)
src/Modules/Payments/Payments.Domain/Payments/(Payment.cs, PaymentStatus.cs, PaymentErrors.cs)
src/Modules/Payments/Payments.Application/(Abstractions/IPaymentGateway.cs, StartPayment.cs, ApplyGatewayOutcome.cs, ExpirePayments.cs)
src/Modules/Payments/Payments.Infrastructure/(Gateways/(StripeGateway.cs, FakeGateway.cs, PaymentsOptions.cs), Webhooks/StripeWebhookEndpoint.cs, Jobs/PaymentExpiryService.cs, Persistence/*, PaymentsModule.cs)
tests/Ordering.UnitTests/, tests/Payments.UnitTests/       (new)
tests/Shop.IntegrationTests/(Ordering/*, Payments/*, Checkout/CheckoutFlowTests.cs)
web/shop-web/src/app/api/(ordering-api.service.ts, payments-api.service.ts)
web/shop-web/src/app/pages/(checkout/, checkout-progress/, simulated-pay/, orders/, order-detail/)
deploy/docker-compose.prod.yml, deploy/SERVER_SETUP.md       (Stripe keys + webhook)
docs/adr/0008-payments-stripe-checkout-choreography.md
```

---

### Task 1: Per-message outbox retry backoff

**Why now:** today a failing message is retried on every poll (250 ms), so it parks after about 1.25 s. A Stripe hiccup or an xmin conflict in this plan must survive minutes, not one second.

**Files:**
- Modify: `src/BuildingBlocks/Shop.Infrastructure.Common/Outbox/(OutboxMessage.cs, OutboxProcessor.cs)`, `Persistence/ModuleDbContext.cs`
- Create: `*_OutboxRetrySchedule` migration in each of the five modules (`dotnet ef migrations add OutboxRetrySchedule`, run per module)
- Test: `tests/Shop.Infrastructure.Common.IntegrationTests/OutboxProcessorTests.cs`

**Interfaces (produced):**
- `OutboxMessage.NextAttemptOnUtc: DateTime?`, column `next_attempt_on_utc`. The pending index becomes `(next_attempt_on_utc, occurred_on_utc)` filtered `processed_on_utc IS NULL`.
- `OutboxOptions.MaxAttempts` default becomes `10`. New `OutboxOptions.RetryBaseDelay` (default `00:00:01`) and `RetryMaxDelay` (default `00:05:00`).
- The poll selects only rows with `next_attempt_on_utc IS NULL OR next_attempt_on_utc <= {now}`. `now` comes from `TimeProvider` and is passed as a parameter, so the database clock is never used.
- After failure number `n`, `NextAttemptOnUtc = now + min(RetryBaseDelay * 2^(n-1), RetryMaxDelay)`.

- [ ] **Step 1: Write the failing tests** (they use a settable `TimeProvider` test double):
  - `Failed_message_is_not_retried_before_its_backoff_elapses`: the handler throws once; a second batch at the same time attempts 0 rows; advance the clock 1 s and a batch delivers it. `Attempts == 1`, `ProcessedOnUtc` set.
  - `Backoff_doubles_and_is_capped`: after 3 failures `NextAttemptOnUtc - now == 4 s`; with `RetryMaxDelay = 3 s` it equals `3 s`.
  - `Message_parks_after_MaxAttempts`: never selected again; `Error` keeps the last message.
- [ ] **Step 2:** Run `dotnet test --project tests/Shop.Infrastructure.Common.IntegrationTests`. Expected: the new tests FAIL.
- [ ] **Step 3:** Implement the column, the query filter and the schedule; update the `OutboxProcessor` XML summary. Add the migration to Catalog, Basket, Identity, Ordering and Payments.
- [ ] **Step 4:** Run `dotnet test --solution VoltLane.slnx`. Expected: all PASS, and the migrations apply against a fresh container (`HostTests`).
- [ ] **Step 5:** Commit `feat(outbox): per-message exponential retry backoff`.

---

### Task 2: Integration event contracts and Catalog stock reservations

**Files:**
- Create: the event records listed in File Structure (all modules' `Contracts/Events`).
- Create: `Catalog.Domain/Reservations/*`, `Catalog.Application/Reservations/*`, and the `StockReservation` EF configuration. Migration `StockReservations`.
- Modify: `Catalog.Application.csproj` (reference `Ordering.Contracts` and `Payments.Contracts`), `CatalogModule.cs` (`services.AddInboxHandlers<CatalogDbContext>(Catalog.Application assembly)`), `Shop.ArchitectureTests` if its allowed-reference list needs the new edges.
- Test: `tests/Catalog.UnitTests/Reservations/StockReservationTests.cs`, `tests/Shop.IntegrationTests/Catalog/StockReservationHandlerTests.cs`

**Interfaces (produced):**
```csharp
// Ordering.Contracts.Events
public sealed record OrderPlaced(Guid OrderId, Guid CustomerId, IReadOnlyList<OrderPlacedLine> Lines, decimal Total, string Currency) : IntegrationEvent;
public sealed record OrderPlacedLine(Guid VariantId, int Quantity);
// Catalog.Contracts.Events
public sealed record StockReserved(Guid OrderId) : IntegrationEvent;
public sealed record StockReservationFailed(Guid OrderId, IReadOnlyList<string> Skus) : IntegrationEvent;  // SKUs short or unavailable
// Payments.Contracts.Events
public sealed record PaymentSucceeded(Guid OrderId, Guid PaymentId) : IntegrationEvent;
public sealed record PaymentFailed(Guid OrderId, Guid PaymentId, string Reason) : IntegrationEvent;
public sealed record PaymentExpired(Guid OrderId, Guid PaymentId) : IntegrationEvent;
// Identity.Contracts.Events
public sealed record DemoCustomerCreated(Guid UserId, string Email) : IntegrationEvent;
// Catalog.Domain.Reservations
public enum ReservationStatus { Reserved, Committed, Released }
public sealed class StockReservation : AggregateRoot<Guid>   // Id == OrderId
{ IReadOnlyList<ReservationLine> Lines; ReservationStatus Status; DateTime CreatedAtUtc; DateTime? ClosedAtUtc;
  static StockReservation Create(Guid orderId, IEnumerable<ReservationLine> lines, DateTime nowUtc);
  bool TryCommit(DateTime nowUtc); bool TryRelease(DateTime nowUtc); }   // true only on a Reserved -> X transition
public sealed record ReservationLine(Guid ProductId, Guid VariantId, int Quantity);
```
**Handler rules:**
- **`OrderPlaced`:** load every product owning the lines' variants and call `Product.ReserveStock` for each line.
  - If any line fails (unknown variant, inactive product, insufficient stock), save nothing and publish `StockReservationFailed` with the failing SKUs; an unknown variant uses its id as the SKU.
  - Otherwise save the products, add the `StockReservation` and publish `StockReserved`, all in one save.
  - If a reservation for this order already exists, do nothing.
- **`PaymentSucceeded`:** `TryCommit`, then `Product.CommitStock` per line.
- **`PaymentFailed` and `PaymentExpired`:** `TryRelease`, then `Product.ReleaseStock` per line.
- **Missing reservation, or one no longer `Reserved`:** log at Information and do nothing.
- **Concurrency:** a `DbUpdateConcurrencyException` (xmin) propagates, so the outbox retries it.

- [ ] **Step 1: Write the failing tests.**
  - Unit: `Commit_and_release_only_from_Reserved`, `Create_rejects_empty_or_nonpositive_lines`.
  - Integration: these publish events straight through `IEventBus` in a scope, then read the outbox and the products.
    - `Reserves_all_lines_and_publishes_StockReserved`
    - `Insufficient_stock_on_one_line_reserves_nothing_and_publishes_failure_with_its_sku`
    - `Archived_product_line_fails_reservation`
    - `Duplicate_OrderPlaced_reserves_once`
    - `Commit_moves_reserved_out_of_on_hand`
    - `Release_returns_reserved_to_available`
    - `Concurrent_orders_for_last_unit_reserve_exactly_one`: two `OrderPlaced` for a variant with `Available == 1`, delivered in parallel with retries until neither throws. One `StockReserved` and one `StockReservationFailed`; `StockReserved == 1`.
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement. The reservation table is `catalog.stock_reservations`, with lines as a jsonb column on that row.
- [ ] **Step 4:** Run `dotnet test --solution VoltLane.slnx`. Expected: PASS, architecture tests included.
- [ ] **Step 5:** Commit `feat(catalog): reserve, commit and release stock per order via integration events`.

---

### Task 3: Ordering domain

**Files:**
- Create: `src/Modules/Ordering/Ordering.Domain/Orders/*`
- Create: `tests/Ordering.UnitTests/` (add to `VoltLane.slnx`), `OrderTests.cs`, `ShippingAddressTests.cs`

**Interfaces (produced):**
```csharp
public enum OrderStatus { Placed, Paid, Shipped, Delivered, Cancelled }
public enum CancellationReason { OutOfStock, PaymentFailed, PaymentExpired, Admin }
public sealed record ShippingAddress(string FullName, string Line1, string? Line2, string City, string PostalCode, string Country)
{ static Result<ShippingAddress> Create(...); }   // trims; FullName/Line1/City 1–100, Line2 ≤100, PostalCode 1–20, Country = 2 letters A–Z (upper-cased)
public sealed class OrderLine : Entity<Guid>
{ Guid VariantId; string ProductName; string Sku; string OptionsLabel; string? ThumbnailUrl; Money UnitPrice; int Quantity; Money LineTotal; }
public sealed record NewOrderLine(Guid VariantId, string ProductName, string Sku, string OptionsLabel, string? ThumbnailUrl, decimal UnitPrice, int Quantity);
public sealed record StatusChange(OrderStatus Status, DateTime AtUtc, string? Note);
public sealed class Order : AggregateRoot<Guid>
{
  string OrderNumber; Guid CustomerId; string CustomerEmail; Guid IdempotencyKey;
  ShippingAddress ShippingAddress; IReadOnlyList<OrderLine> Lines; Money Subtotal; Money Discount; Money Total;
  OrderStatus Status; CancellationReason? CancellationReason; bool RefundRequired; DateTime PlacedAtUtc;
  IReadOnlyList<StatusChange> History;
  static Result<Order> Place(string orderNumber, Guid customerId, string customerEmail, Guid idempotencyKey,
                             ShippingAddress address, IReadOnlyCollection<NewOrderLine> lines, DateTime nowUtc);
  Result MarkPaid(DateTime nowUtc); Result Ship(DateTime nowUtc); Result Deliver(DateTime nowUtc);
  Result Cancel(CancellationReason reason, DateTime nowUtc);   // Paid -> Cancelled sets RefundRequired = true
}
```
**Rules:**
- `Place` requires 1–20 lines, each with quantity 1–10, and no duplicate variant.
- `Discount` is `Money.Zero`. `Total = Subtotal - Discount`.
- History starts with `Placed`. Each transition appends a `StatusChange`.
- A disallowed transition returns `OrderErrors.InvalidTransition(from, to)` (`ordering.invalid_transition`).
- Repeating the current target is not idempotent at domain level: `MarkPaid` on `Paid` is an error. Handlers decide what to ignore.
- `Place` raises the domain event `OrderPlacedDomainEvent(Order)`. Task 4's handler turns it into the integration event.

- [ ] **Step 1: Write the failing tests:**
  - `Place_computes_line_totals_subtotal_and_total`: 2 × 199.99 + 1 × 49.50 = 449.48.
  - `Place_rejects_empty_duplicate_or_out_of_range_lines`
  - `Allowed_transitions_follow_the_state_machine`: a theory over every (from, action) pair; exactly the spec's transitions succeed.
  - `Cancel_from_Paid_sets_RefundRequired`
  - `History_records_each_transition_in_order`
  - `ShippingAddress_validates_and_normalizes_country`: `"us"` becomes `"US"`; `"USA"` fails.
- [ ] **Step 2:** Run `dotnet test --project tests/Ordering.UnitTests`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the tests. Expected: PASS.
- [ ] **Step 5:** Commit `feat(ordering): order aggregate, address value object and status state machine`.

---

### Task 4: Checkout endpoint, persistence and order queries

**Files:**
- Create:
  - `Basket.Contracts/(IBasketCheckoutQuery.cs, BasketItem.cs)` and their implementation in `Basket.Infrastructure`.
  - `Basket.Application/Reactions/RemoveOrderedLinesOnOrderPlaced.cs`, with Basket referencing `Ordering.Contracts`.
  - `Ordering.Application/Checkout/(PlaceOrder.cs, PlaceOrderValidator.cs, PublishOrderPlaced.cs)` and `Orders/(GetMyOrders.cs, GetOrder.cs, OrderDtos.cs)`.
  - `Ordering.Infrastructure/Persistence/(OrderConfiguration.cs, OrderRepository.cs)` and migration `OrderModel`, which creates the sequence.
  - `Ordering.Infrastructure/Endpoints/OrderingEndpoints.cs`.
  - `Ordering.Contracts/Orders/(IOrderPaymentQuery.cs, OrderPaymentDetails.cs)` and `Ordering.Infrastructure/Reading/OrderPaymentQuery.cs`.
- Modify:
  - `ICurrentUser` and `CurrentUser`: add `string? Email` from the `email` claim.
  - `RateLimitPolicies` and the host's rate-limiting setup: add `Checkout`.
  - `ShopApiFactory`: `RateLimiting:CheckoutPerMinute = 1000`.
  - The OpenAPI snapshot and the types.
- Test: `tests/Shop.IntegrationTests/Ordering/(CheckoutEndpointTests.cs, OrderQueryTests.cs)`, `tests/Shop.IntegrationTests/Basket/BasketOrderPlacedTests.cs`

**Interfaces (produced):**
```csharp
// Basket.Contracts
public interface IBasketCheckoutQuery { Task<IReadOnlyList<BasketItem>> GetForUserAsync(Guid userId, CancellationToken ct); }
public sealed record BasketItem(Guid VariantId, int Quantity);
// Ordering.Contracts
public interface IOrderPaymentQuery { Task<OrderPaymentDetails?> GetAsync(Guid orderId, CancellationToken ct); }
public sealed record OrderPaymentDetails(Guid OrderId, string OrderNumber, Guid CustomerId, string CustomerEmail,
    decimal Total, string Currency, IReadOnlyList<OrderPaymentLine> Lines);
public sealed record OrderPaymentLine(string Name, int Quantity, decimal UnitPrice);   // Name = "ProductName (OptionsLabel)" or ProductName
```
**Endpoints, under `/api/ordering`, all `RequireAuthorization()`:**
- `POST /checkout`:
  - Body `{ shippingAddress: {fullName, line1, line2?, city, postalCode, country}, expectedTotal: number }`, plus the header `Idempotency-Key`. Policy `checkout`.
  - Returns 202 `{ orderId, orderNumber }` with `Location: /api/ordering/orders/{id}/checkout-status`.
- `GET /orders?page=1&pageSize=10`:
  - Returns `PagedResult<OrderSummaryDto { id, orderNumber, placedAtUtc, status, total, itemCount, firstThumbnailUrl }>`.
  - Only the caller's orders, newest first. `pageSize` is at most 50.
- `GET /orders/{id}`:
  - Returns `OrderDto { id, orderNumber, placedAtUtc, status, cancellationReason?, lines[], shippingAddress, subtotal, discount, total, history[] }`.
  - Another customer's order gets 404 `ordering.order_not_found`.

**The `PlaceOrder` handler, in order:**
1. Check the idempotency key. An existing order for (customer, key) returns it.
2. Read the basket through `IBasketCheckoutQuery`. If it's empty: `empty_basket`.
3. Read snapshots through `ICatalogVariantQuery`. If any item is missing, not purchasable, or `Available < Quantity`: `basket_has_issues`.
4. Compute the total from the live prices. If it differs from `expectedTotal` by more than 0.005: `price_changed`.
5. Take the next order number (`SELECT nextval('ordering.order_number_seq')`) and call `Order.Place`.
6. Save. The domain-event handler publishes `OrderPlaced`, so it lands in the outbox in the same transaction.
7. A unique violation on (customer_id, idempotency_key) reloads and returns the existing order.

The basket reaction deletes only the ordered variant lines from that user's basket. It never touches lines added since.

- [ ] **Step 1: Write the failing tests:**
  - `Checkout_creates_a_Placed_order_and_an_OrderPlaced_outbox_row`
  - `Same_idempotency_key_returns_the_same_order`, both sequentially and in parallel (`Task.WhenAll` of 2)
  - `Missing_or_non_guid_idempotency_key_is_400`
  - `Empty_basket_is_409`
  - `Out_of_stock_line_is_409_basket_has_issues_and_creates_no_order`
  - `Wrong_expected_total_is_409_price_changed`
  - `Invalid_address_is_400_validation_failed`
  - `Anonymous_is_401`
  - `Order_numbers_are_sequential_and_prefixed`
  - `Other_customers_order_is_404`
  - `My_orders_are_paged_newest_first`
  - `OrderPlaced_removes_only_the_ordered_lines_from_the_basket`
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement. Lines go to their own table `ordering.order_lines`. History and address are jsonb columns. `xmin` is the concurrency token on `orders`.
- [ ] **Step 4:** Run `dotnet test --solution VoltLane.slnx`, then regenerate the snapshot and types. Expected: PASS, with no snapshot drift.
- [ ] **Step 5:** Commit `feat(ordering): idempotent checkout, order history queries, basket clean-up on OrderPlaced`.

---

### Task 5: Payments module (gateway, webhook, expiry)

**Files:**
- Create: `Payments.Domain/Payments/*`, `Payments.Application/*`, `Payments.Infrastructure/(Gateways/*, Webhooks/*, Jobs/*, Persistence/*)`, migration `PaymentModel`, and `Payments.Contracts/Payments/IPaymentCheckoutQuery.cs`.
- Modify:
  - `PaymentsModule.cs`.
  - `Payments.Application.csproj`: reference `Ordering.Contracts`.
  - `CsrfHeader.cs`: add `ExemptPaths` containing `/api/payments/stripe/webhook`, plus a test.
  - `appsettings.json`: Payments section, `Provider = "Fake"`.
  - `appsettings.Development.json`: `App:PublicBaseUrl`.
  - `ShopApiFactory`: Provider `Fake`, `Payments:Timeout = 00:00:02`, `App:PublicBaseUrl = http://test.local`.
- Test: `tests/Payments.UnitTests/PaymentTests.cs` (new project), `tests/Shop.IntegrationTests/Payments/(StartPaymentTests.cs, StripeWebhookTests.cs, SimulatedPaymentTests.cs, PaymentExpiryTests.cs, PaymentsStartupValidationTests.cs)`

**Interfaces (produced):**
```csharp
public enum PaymentStatus { Pending, Succeeded, Failed, Expired }
public sealed class Payment : AggregateRoot<Guid>
{ Guid OrderId; Guid CustomerId; Money Amount; PaymentStatus Status; string Provider; string ProviderSessionId; string CheckoutUrl;
  DateTime CreatedAtUtc; DateTime ExpiresAtUtc; DateTime? CompletedAtUtc; string? FailureReason;
  static Payment Start(Guid orderId, Guid customerId, Money amount, string provider, string sessionId, string url, DateTime nowUtc, DateTime expiresAtUtc);
  Result Succeed(DateTime nowUtc); Result Fail(string reason, DateTime nowUtc); Result Expire(DateTime nowUtc); }   // each raises a domain event; only from Pending
// Payments.Application
public interface IPaymentGateway
{ string Name { get; }
  Task<GatewaySession> CreateSessionAsync(GatewaySessionRequest request, CancellationToken ct);
  Task<GatewaySessionState> GetSessionStateAsync(string sessionId, CancellationToken ct);   // Open | Paid | Expired
  Task ExpireSessionAsync(string sessionId, CancellationToken ct); }
public sealed record GatewaySessionRequest(Guid OrderId, string OrderNumber, string CustomerEmail, decimal Total, string Currency,
    IReadOnlyList<OrderPaymentLine> Lines, string SuccessUrl, string CancelUrl, DateTime ExpiresAtUtc);
public sealed record GatewaySession(string SessionId, string Url);
public enum GatewaySessionState { Open, Paid, Expired }
public sealed record ApplyGatewayOutcome(string SessionId, GatewayOutcome Outcome, string? Reason) : ICommand;  // Succeeded | Failed | Expired
// Payments.Contracts
public interface IPaymentCheckoutQuery { Task<PaymentCheckoutInfo?> GetByOrderAsync(Guid orderId, CancellationToken ct); }
public sealed record PaymentCheckoutInfo(Guid PaymentId, string Status, string? CheckoutUrl, DateTime ExpiresAtUtc);  // Status = PaymentStatus name
```
**Behaviour:**
- **On `StockReserved`** (inbox handler `StartPaymentOnStockReserved`):
  - If a payment for the order already exists, do nothing.
  - Otherwise load `IOrderPaymentQuery` and build `SuccessUrl = {PublicBaseUrl}/orders/{orderId}?checkout=success` and `CancelUrl = …?checkout=cancelled`.
  - Call the gateway, then save the `Payment` with `ExpiresAtUtc = now + Timeout`.
  - Stripe calls carry the idempotency key `checkout-session-{orderId}`, so a retried handler gets the same session.
- **Domain-event handlers** publish `PaymentSucceeded`, `PaymentFailed` or `PaymentExpired` in the same save.
- **`ApplyGatewayOutcome`:**
  - Finds the payment by session id; an unknown session is logged and returns success.
  - Applies the transition. A payment that is no longer Pending is a no-op, logged at Information.
- **`StripeGateway`** (Stripe.net):
  - Checkout Session in `payment` mode, with `price_data` line items in cents, `customer_email`, `expires_at`, `client_reference_id = orderId`, and `metadata["order_id"]`.
  - State: `status == "expired"` → Expired; `payment_status == "paid"` → Paid; anything else → Open.
- **`FakeGateway`:** session id `fake_{orderId:N}`, URL `{PublicBaseUrl}/pay/simulated/{orderId}`. It keeps state in the payment row only: `GetSessionStateAsync` returns Open, and `ExpireSessionAsync` is a no-op.
- **Webhook `POST /api/payments/stripe/webhook`** (mapped only when Provider is Stripe; anonymous; not rate-limited; exempt from CSRF):
  - Reads the raw body and verifies `Stripe-Signature` with `EventUtility.ConstructEvent(body, sig, WebhookSecret, throwOnApiVersionMismatch: false)`. A bad or missing signature gets 400.
  - Dedupes on the event id in `payments.processed_webhook_events (event_id text PK, received_on_utc)`, saved in the same transaction as the outcome.
  - Maps events to outcomes:
    - `checkout.session.completed` with `payment_status == "paid"`, and `checkout.session.async_payment_succeeded` → Succeeded
    - `checkout.session.async_payment_failed` → Failed (`"async_payment_failed"`)
    - `checkout.session.expired` → Expired
    - anything else → ignored with 200
- **Simulated payment** `POST /api/payments/simulated/{orderId}` with body `{ succeed: bool }`:
  - Mapped only when Provider is Fake. Requires auth; the caller must own the payment, otherwise 404 `payments.payment_not_found`.
  - Sends `ApplyGatewayOutcome` (Succeeded, or Failed with `"declined"`).
- **`PaymentExpiryService`** (BackgroundService) every `Payments:ExpiryScanInterval` (default 60 s; 1 s in tests):
  - For each Pending payment with `ExpiresAtUtc <= now`, ask `GetSessionStateAsync`:
    - Paid → Succeed.
    - Open → `ExpireSessionAsync`, then Expire.
    - Expired → Expire.
  - Each payment runs in its own scope, and failures are logged per payment.
- **Startup validation:** in Production, `Payments:Provider` must be `Stripe` and the key prefixes must match (see Global Constraints). The Stripe provider also rejects `Timeout < 30 min`.

- [ ] **Step 1: Write the failing tests:**
  - Unit:
    - `Transitions_only_from_Pending`
    - `Each_transition_raises_its_domain_event`
  - Integration (Fake provider):
    - `StockReserved_creates_one_pending_payment_with_simulated_url`, delivered twice: still one row
    - `Simulated_success_publishes_PaymentSucceeded`
    - `Simulated_decline_publishes_PaymentFailed`
    - `Simulated_payment_for_someone_elses_order_is_404`
    - `Pending_payment_past_timeout_expires_and_publishes_PaymentExpired`
  - Integration (a factory subclass with Provider `Stripe`, `sk_test_dummy`, `whsec_test`, `Payments:Timeout = 00:30:00`, and `IPaymentGateway` replaced by a test double; the test signs payloads with the Stripe `t=…,v1=HMAC-SHA256` scheme, and the expiry test backdates `expires_at_utc` in the database):
    - `Signed_completed_paid_event_succeeds_the_payment`
    - `Same_event_id_twice_is_a_no_op`
    - `Bad_signature_is_400_and_changes_nothing`
    - `Unknown_session_is_200`
    - `Completed_unpaid_waits_then_async_succeeded_succeeds`
    - `Expired_event_expires`
    - `Webhook_needs_no_csrf_header`
    - `Expiry_job_marks_paid_when_gateway_reports_paid`
  - Startup validation:
    - `Production_with_live_key_fails_startup`
    - `Production_with_fake_provider_fails_startup`
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement. Add `Stripe.net` 53.2.0 to `Directory.Packages.props`.
- [ ] **Step 4:** Run `dotnet test --solution VoltLane.slnx`, then regenerate the snapshot and types. Expected: PASS.
- [ ] **Step 5:** Commit `feat(payments): payment aggregate, Stripe and simulated gateways, signed idempotent webhook, expiry job`.

---

### Task 6: Ordering reactions, checkout status, and the end-to-end flow test

**Files:**
- Create: `Ordering.Application/Reactions/(CancelOnStockReservationFailed.cs, MarkPaidOnPaymentSucceeded.cs, CancelOnPaymentEnded.cs)`, `Ordering.Application/Orders/GetCheckoutStatus.cs`
- Modify: `OrderingModule.cs` (`AddInboxHandlers<OrderingDbContext>`), `Ordering.Application.csproj` (reference `Catalog.Contracts` and `Payments.Contracts`), the endpoints, the snapshot and the types.
- Test: `tests/Shop.IntegrationTests/Checkout/CheckoutFlowTests.cs`

**Interfaces (produced):**
- `GET /api/ordering/orders/{id}/checkout-status` (auth, owner only) returns `CheckoutStatusDto { state: "Pending"|"OutOfStock"|"ReadyToPay"|"Paid"|"Failed", paymentUrl?: string, reason?: string, expiresAtUtc?: string }`.
- How the state is decided:
  - Cancelled with `OutOfStock` → `OutOfStock`.
  - Any other Cancelled → `Failed` with `reason` set to the reason name.
  - Paid, Shipped or Delivered → `Paid`.
  - Placed with a Pending payment → `ReadyToPay` with `paymentUrl` and `expiresAtUtc`.
  - Placed with no payment yet → `Pending`.
- **Reactions:** each tolerates replays and late arrivals. A transition the order can no longer make is logged at Warning and skipped, never thrown.
  - A `PaymentSucceeded` for a Cancelled order is logged at **Error** with the order id, because it means money arrived for a cancelled order. Refunding it is Milestone 2.

- [ ] **Step 1: Write the failing flow tests.** They use the real host (Fake provider, `Payments:Timeout = 2 s`) and poll `checkout-status` with a 10 s helper timeout.
  - `Happy_path_reaches_ReadyToPay_then_Paid_and_commits_stock`:
    1. Add to the cart and check out; the status becomes `ReadyToPay`.
    2. Simulate success; the status becomes `Paid`.
    3. The order history shows `Placed`, then `Paid`.
    4. The variant's `StockOnHand` dropped by the quantity and `StockReserved` is back to its previous value.
  - `Out_of_stock_at_reservation_ends_OutOfStock_and_order_is_cancelled`: drain the variant's stock between checkout and reservation by reserving it directly through Catalog in the test.
  - `Declined_payment_cancels_the_order_and_releases_stock`
  - `Abandoned_payment_expires_cancels_and_releases_stock`
  - `Last_unit_bought_by_two_customers_one_pays_one_is_out_of_stock`
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `dotnet test --solution VoltLane.slnx` and regenerate the snapshot and types. Expected: PASS. Run the flow tests 3 times in a row to rule out flakiness.
- [ ] **Step 5:** Commit `feat(ordering): react to stock and payment events; checkout status endpoint`.

---

### Task 7: Historical orders for demo customers

**Files:**
- Modify:
  - `Identity.Domain/Users/User.cs`: `CreateDemoCustomer` raises the domain event `DemoCustomerCreatedDomainEvent`.
  - Identity Application: a domain-event handler publishes `DemoCustomerCreated`.
  - `Catalog.Contracts/Variants/ICatalogVariantQuery.cs` and `CatalogVariantQuery.cs`: add `GetSampleAsync`.
- Create: `Ordering.Application/Demo/SeedDemoOrdersOnDemoCustomerCreated.cs`, with Ordering referencing `Identity.Contracts`.
- Test: `tests/Shop.IntegrationTests/Ordering/DemoOrdersTests.cs`, plus a case in `tests/Shop.IntegrationTests/Identity/DemoAccountTests.cs`

**Interfaces (produced):**
- `ICatalogVariantQuery.GetSampleAsync(int count, CancellationToken ct) -> IReadOnlyList<VariantSnapshot>`: the first variant of featured Active products, ordered by product name, deterministic.
- The handler creates **three** orders from `GetSampleAsync(4)`. The address is `Demo Customer, 221 Market Street, San Francisco, 94105, US`.

| Order | Placed | Lines | Final status | History |
|---|---|---|---|---|
| A | 41 days ago | sample[0] ×1 | Delivered | Paid +1 h, Shipped +1 d, Delivered +4 d |
| B | 12 days ago | sample[1] ×1 and sample[2] ×2 | Delivered | same offsets |
| C | 2 days ago | sample[3] ×1 | Shipped | Paid +1 h, Shipped +1 d |

- Add `Order.PlaceHistorical(...)`, with the same parameters as `Place`. It sets `IsHistorical = true` (a new column, default false) and raises **no** domain event, so no `OrderPlaced` is published and no stock is touched. The transition methods then run with backdated times.
- Idempotency key: a deterministic GUID derived from the user id and the order letter (SHA-1 of `"{userId}:{letter}"`, first 16 bytes), so the unique index also protects a replay.
- A duplicate event creates nothing, thanks to the inbox.

- [ ] **Step 1: Write the failing tests:**
  - `Demo_customer_gets_three_historical_orders_within_5_seconds`: sign in as a demo customer and poll `GET /orders`. The statuses are `Delivered`, `Delivered`, `Shipped`, newest first `Shipped`, and each history matches the table.
  - `Historical_orders_do_not_touch_stock_or_publish_OrderPlaced`
  - `Registered_customer_gets_no_historical_orders`
  - `DemoCustomerCreated_is_in_the_outbox_once_even_when_the_email_retry_path_runs`
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `dotnet test --solution VoltLane.slnx`. Expected: PASS.
- [ ] **Step 5:** Commit `feat(ordering): seed three historical orders for each demo customer`.

---

### Task 8: Angular checkout, progress and simulated payment pages

**Files:**
- Create:
  - `web/shop-web/src/app/api/(ordering-api.service.ts, payments-api.service.ts)`
  - `pages/checkout/*`: route `/checkout`, `authGuard`
  - `pages/checkout-progress/*`: route `/orders/:id/progress`, `authGuard`
  - `pages/simulated-pay/*`: route `/pay/simulated/:orderId`, `authGuard`
- Modify:
  - `app.routes.ts`.
  - `pages/cart/cart.page.*`: "Checkout" button enabled and linking to `/checkout`. It is disabled when the cart is empty or `hasIssues`, and the "coming soon" copy goes.
  - `pages/home/demo-panel/*`: test card line.
- Test: `*.spec.ts` next to each page; update the cart and demo-panel specs.

**Behaviour:**
- **`/checkout`:**
  - Material form for the address. Country is a select of `US, GB, DE, NL, FR, TR`, default `US`; demo customers get the demo address pre-filled.
  - Shows the order summary from `BasketService`, and "Place order".
  - On submit: `Idempotency-Key` is one `crypto.randomUUID()` created when the page opens and reused on retry; `expectedTotal` is the basket subtotal.
  - On 202, navigate to `/orders/{id}/progress` with `replaceUrl`.
  - On `price_changed` or `basket_has_issues`: reload the basket and show the inline message "Your cart changed. Please review it before paying." with a link to `/cart`.
  - On `empty_basket`, go to `/cart`.
  - The submit button is disabled while pending.
- **`/orders/:id/progress`:**
  - Shows "Reserving your items…" with a spinner, and polls `checkout-status` every 500 ms for up to 30 s, stopping on destroy.
  - `ReadyToPay`: `window.location.assign(paymentUrl)`, wrapped in an injectable `RedirectService` so tests can spy on it.
  - `OutOfStock`: "Sorry, an item sold out while we were reserving it." plus "Back to cart".
  - `Failed`: "We couldn't start your payment." plus "View order".
  - `Paid`: go to `/orders/:id`.
  - After 30 s: "This is taking longer than usual." plus "Check again", which restarts the poll.
- **`/pay/simulated/:orderId`:**
  - Card titled "Test payment", with "This is a simulated payment page. No real money moves." and the order number and total, loaded from the order.
  - "Pay" and "Decline" call the simulated endpoint, then navigate to `/orders/:id?checkout=success` or `?checkout=cancelled`.
- **Demo panel:** adds "Pay with Stripe's test card 4242 4242 4242 4242, any future date, any CVC."

- [ ] **Step 1: Write the failing specs:**
  - Checkout: sends the same idempotency key on retry; shows the changed-cart message on 409 `price_changed`; redirects on 202.
  - Progress: redirects on `ReadyToPay`; shows the out-of-stock copy; stops polling on destroy (fake timers); shows the timeout copy after 30 s.
  - Simulated pay: Pay sends `{succeed:true}` and navigates with `checkout=success`.
  - Cart: the Checkout button links to `/checkout` and is disabled when `hasIssues`.
- [ ] **Step 2:** Run `CI=true npx ng test --no-watch`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `CI=true npx ng test --no-watch && npx ng lint && npx ng build`. Expected: PASS.
- [ ] **Step 5:** Commit `feat(web): checkout, reservation progress and simulated payment pages`.

---

### Task 9: Angular order history and order detail with status timeline

**Files:**
- Create: `pages/orders/*` (route `/orders`, `authGuard`), `pages/order-detail/*` (route `/orders/:id`, `authGuard`), `shared/order-status-chip/*`
- Modify: `layout/header/*` (account menu gets a "My orders" item), `app.routes.ts`
- Test: specs next to each.

**Behaviour:**
- **`/orders`:**
  - A list of cards: order number, date, status chip, item count, total, first thumbnail. Paginated (Material paginator, 10 per page).
  - Empty state: "No orders yet" plus "Start shopping".
  - Loading skeletons while loading.
- **`/orders/:id`:**
  - Header shows the order number, date and status chip.
  - The **status timeline** is a vertical list of `Placed → Paid → Shipped → Delivered`:
    - reached steps show their time;
    - future steps are greyed;
    - a Cancelled order shows the steps it reached, then a red "Cancelled: <human reason>" step.
  - Lines show thumbnail, name, options, quantity and line total. Then the shipping address and the totals.
  - Reasons in human words: OutOfStock → "an item sold out", PaymentFailed → "payment was declined", PaymentExpired → "payment wasn't completed in time", Admin → "cancelled by the store".
  - `?checkout=success` while the order is `Placed`:
    - Banner "Confirming your payment…", polling `GET /orders/{id}` every 1 s for up to 30 s.
    - Once Paid: a success banner "Thank you! Order VL-… is paid."
    - The poll stops once the status is no longer Placed.
  - `?checkout=cancelled` while `Placed`: banner "Payment not completed." plus a "Pay now" button taken from `checkout-status`'s `paymentUrl`, shown only while it is `ReadyToPay`.
  - Another customer's order, or an unknown id: the not-found state.

- [ ] **Step 1: Write the failing specs:**
  - Timeline marks reached steps and greys future ones.
  - A Cancelled order renders the reason copy.
  - The success banner appears after the poll returns Paid.
  - "Pay now" appears only for `ReadyToPay`.
  - The header menu has "My orders".
  - Empty-state copy.
- [ ] **Step 2:** Run `CI=true npx ng test --no-watch`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `CI=true npx ng test --no-watch && npx ng lint && npx ng build`. Expected: PASS.
- [ ] **Step 5:** Commit `feat(web): order history and order detail with status timeline`.

---

### Task 10: Deploy configuration, docs, PR

**Files:**
- Modify:
  - `deploy/docker-compose.prod.yml`, in the api environment:
    - `Payments__Provider: Stripe`
    - `Stripe__SecretKey: ${STRIPE_SECRET_KEY:?…}`
    - `Stripe__WebhookSecret: ${STRIPE_WEBHOOK_SECRET:?…}`
    - `App__PublicBaseUrl: https://${SITE_ADDRESS}`
  - `deploy/docker-compose.yml`: Fake provider and `App__PublicBaseUrl: http://localhost:8080` (Caddy publishes 8080 locally).
  - `deploy/SERVER_SETUP.md`: a Stripe section. It covers the test-mode secret key, and a webhook endpoint at `https://<YOUR_DOMAIN>/api/payments/stripe/webhook` listening for these four events:
    - `checkout.session.completed`
    - `checkout.session.async_payment_succeeded`
    - `checkout.session.async_payment_failed`
    - `checkout.session.expired`

    It ends with copy-paste-safe commands that append both values to `/opt/voltlane/.env`, in the same style as the JWT key, reading them with `read -rs` so they never land in shell history.
  - `README.md`:
    - The flow diagram.
    - The demo section, including the test card.
    - Roadmap: tick Plan 4.
    - "Running locally" gets a line on the simulated payment page, and on the Stripe CLI for real webhooks: `stripe listen --forward-to localhost:8080/api/payments/stripe/webhook`.
- Create: `docs/adr/0008-payments-stripe-checkout-choreography.md`. It covers:
  - Why Stripe Checkout and not the Payment Element.
  - Polling vs SSE.
  - Webhook idempotency.
  - Expiry with Stripe as the source of truth.
  - The fake gateway and why production refuses it.
  - Basket lines removed on `OrderPlaced` rather than on payment.
  - The known gap: payment success on a cancelled order is logged, and the refund comes in Milestone 2.

- [ ] **Step 1:** Make the changes. Run `docker compose -f deploy/docker-compose.yml config` and `docker compose -f deploy/docker-compose.prod.yml config`; the second, run without the Stripe vars, must fail with the required-variable message.
- [ ] **Step 2:** Run `dotnet test --solution VoltLane.slnx` and the full Angular check. Expected: PASS.
- [ ] **Step 3:** Commit `docs: payments ADR, Stripe setup, checkout in README`. Push the branch and open the PR. **Stop.** The user adds the Stripe keys on the server and configures the webhook in the Stripe dashboard before merging.
- [ ] **Step 4 (after the user merges):** Verify production:
  - Check out with a demo customer and pay with `4242 4242 4242 4242` on Stripe's hosted page in Chrome.
  - The order reaches `Paid` within a few seconds of returning.
  - The Stripe dashboard shows the webhook delivered with 200.
  - `/api/system/info` shows the merge SHA.
  - No console errors.
