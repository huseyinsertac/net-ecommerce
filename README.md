# VoltLane

A production-style e-commerce backend and storefront: a .NET 10 modular monolith with an Angular
front end, with a CI/CD pipeline that deploys to a single server.

[![CI](https://github.com/huseyinsertac/net-ecommerce/actions/workflows/ci.yml/badge.svg)](https://github.com/huseyinsertac/net-ecommerce/actions/workflows/ci.yml)

**Live demo:** [huseyinsertacnetecommerce.duckdns.org](https://huseyinsertacnetecommerce.duckdns.org) · [API reference](https://huseyinsertacnetecommerce.duckdns.org/scalar) · [health](https://huseyinsertacnetecommerce.duckdns.org/health/ready)

## Demo accounts

No credentials are needed. On the home page:

- **Try as customer** creates a private, temporary account just for you.
- **Try as admin** signs into a shared admin account and opens the admin area at `/admin`. The
  admin can:
  - **Products:** search and filter the list, create a product (pick a category, fill the specs from
    its schema, define up to two option axes and get a variant grid, choose images from the existing
    photos), edit details, specs, status and images, and set each variant's price and stock. Edits
    use optimistic concurrency, so two people saving the same product get a conflict instead of
    overwriting each other, and stock can never go below what is reserved.
  - **Orders:** list, filter and search orders, open one, mark a Paid order as shipped and a
    Shipped order as delivered. Cancel and refund are Milestone 2.

  The shared admin cannot use the cart.

**The demo resets every night at 02:00 UTC.** All data (including accounts and orders you create) is
wiped, then the catalog, the demo admin and 24 sample orders from 8 fictional customers are seeded
again, so feel free to change things.

Each demo customer starts with three historical orders, so the order history is not empty.
To try checkout, add something to the cart and place the order. On Stripe's page, pay with the
test card `4242 4242 4242 4242`, any future expiry date and any CVC. No real money moves: the
site only accepts Stripe test-mode keys.

Normal registration and sign-in also work.

## Screenshots

![Home page](docs/screenshots/home.png)
*Home page with the demo panel and featured products.*

![Product page](docs/screenshots/product.png)
*Product detail with the variant picker and gallery.*

![Cart](docs/screenshots/cart.png)
*Cart with live prices and stock flags.*

![Checkout progress](docs/screenshots/checkout-progress.png)
*Checkout waiting for stock reservation and payment.*

![Order detail](docs/screenshots/order-detail.png)
*Order detail with the status timeline.*

![Admin orders](docs/screenshots/admin-orders.png)
*Admin order list with filters and ship/deliver actions.*

![Admin product editor](docs/screenshots/admin-product.png)
*Admin product editor with specs, variants and images.*

## Features

- **Catalog:** category tree with per-category spec schemas; products with option axes (colour,
  storage, ...), variants and stock.
- **Search and filters:** PostgreSQL full-text search with spec, price and brand filters and
  facet counts, served by a Dapper read side.
- **API contract:** OpenAPI snapshot checked into the repo, TypeScript types generated from it,
  and a CI drift guard that fails if they diverge.
- **Seeded data:** 77 products under fictional brands, with Unsplash photos credited in
  [CREDITS.md](web/shop-web/public/images/CREDITS.md).
- **Storefront:** home page, filterable product listing (filters live in the URL), product
  detail with a variant picker and gallery.
- **Accounts:** register, sign in, or use a one-click demo account. Short-lived JWT access tokens
  (15 minutes, kept in memory only) plus rotating refresh tokens in an HttpOnly, SameSite=Strict
  cookie. Reuse of a refresh token revokes its whole token family, with a 30 second single-use
  grace period so concurrent tabs do not log each other out.
- **Admin area:** product management (create, edit, variants, stock, images) and order handling
  (ship, deliver), behind an admin-only authorization policy on every `/api/*/admin/*` route.
- **Nightly demo reset:** wipes and re-seeds every module schema at 02:00 UTC in one transaction,
  with retries; also available as the CLI command `dotnet Shop.Api.dll reset-demo`.
- **API hardening:** per-IP rate limits (including a global 300 requests per minute on `/api/*`,
  excluding health checks and the Stripe webhook), and a required `X-Requested-With` header on API
  writes.
- **Cart:** works for anonymous and signed-in users, with live prices and stock flags, and is
  merged into your account when you sign in.
- **Checkout and payments:** place an order from the cart, then pay on Stripe's hosted Checkout page
  (test mode only). Stock is reserved first, the order moves through a status timeline to `Paid`
  (or is cancelled and the stock released), and the order history lists every order.

Not built yet (Milestone 2): order cancellation and refunds from the admin.

### Admin and demo reset design

Admin endpoints live in the module that owns the data, and the nightly reset is a transactional
truncate-and-reseed; see [ADR-0009](docs/adr/0009-admin-and-nightly-reset.md).

### Identity design

Own user model with JWT access tokens and a rotating refresh cookie rather than ASP.NET Identity;
the trade-offs (token pruning, CSRF header, rate limits) are in
[ADR-0007](docs/adr/0007-identity-own-user-model-jwt-refresh-cookie.md).

### Catalog design

Search is Postgres full-text plus JSONB specs queried through a SQL read side, rather than an
external search engine. The reasoning is in
[ADR-0006](docs/adr/0006-catalog-search-postgres-fts-jsonb-sql-read-side.md).

## Tech stack

- **Backend:** .NET 10, ASP.NET Core, EF Core, Npgsql
- **Database:** PostgreSQL 18, one schema per module
- **Frontend:** Angular 22 with Angular Material
- **Infrastructure:** Docker, Caddy (automatic HTTPS), GitHub Actions, GHCR, single VPS
- **Testing:** Microsoft.Testing.Platform, Testcontainers, Vitest

## Architecture at a glance

- One deployable **modular monolith**: five business modules inside a single host (`Shop.Api`).
- Each module owns its **own Postgres schema**; no cross-module table access or joins.
- Modules talk only through **Contracts** projects and **integration events**, never each
  other's internals. Architecture tests enforce this.
- Events are delivered reliably with a transactional **outbox** (publish) and **inbox**
  (idempotent consume).
- A small in-house dispatcher handles commands and queries instead of MediatR.

```mermaid
flowchart TB
    Host["Shop.Api (host)"]
    Host --> Catalog & Basket & Ordering & Payments & Identity
    Catalog --> S1[("schema: catalog")]
    Basket --> S2[("schema: basket")]
    Ordering --> S3[("schema: ordering")]
    Payments --> S4[("schema: payments")]
    Identity --> S5[("schema: identity")]
    Basket -.->|"Contracts query: prices"| Catalog
    Ordering -.->|"OrderPlaced"| Catalog
    Ordering -.->|"OrderPlaced"| Basket
    Catalog -.->|"StockReserved / StockReservationFailed"| Payments
    Catalog -.->|"StockReservationFailed"| Ordering
    Payments -.->|"PaymentSucceeded / Failed / Expired"| Ordering
    Payments -.->|"PaymentSucceeded / Failed / Expired"| Catalog
```

Dashed arrows are Contracts-only communication (integration events via outbox/inbox, plus one
Contracts query).

### Checkout flow

Checkout is choreographed with integration events; no module owns the whole flow
([ADR-0008](docs/adr/0008-payments-stripe-checkout-choreography.md)).

```mermaid
flowchart LR
    OP["OrderPlaced"] --> Cat["Catalog reserves stock"]
    OP --> Bas["Basket removes ordered lines"]
    Cat -->|"StockReserved"| Pay["Payments opens a payment session"]
    Cat -->|"StockReservationFailed"| Cancel1["Ordering cancels the order"]
    Pay -->|"customer pays (Stripe webhook)"| PS["PaymentSucceeded"]
    Pay -->|"payment fails"| PF["PaymentFailed"]
    Pay -->|"deadline, Stripe is source of truth"| PE["PaymentExpired"]
    PS --> Paid["Ordering marks Paid, Catalog commits the reservation"]
    PF --> Rel["Ordering cancels, Catalog releases stock"]
    PE --> Rel
```

Decisions are recorded as ADRs:

- [0001 Modular monolith](docs/adr/0001-modular-monolith.md)
- [0002 Layer projects per module](docs/adr/0002-layer-projects-per-module.md)
- [0003 Schema per module](docs/adr/0003-schema-per-module.md)
- [0004 Outbox/inbox choreography](docs/adr/0004-outbox-inbox-choreography.md)
- [0005 Own dispatcher instead of MediatR](docs/adr/0005-own-dispatcher-instead-of-mediatr.md)
- [0006 Catalog search: Postgres FTS, JSONB specs, SQL read side](docs/adr/0006-catalog-search-postgres-fts-jsonb-sql-read-side.md)
- [0007 Identity: own user model, JWT and refresh cookie](docs/adr/0007-identity-own-user-model-jwt-refresh-cookie.md)
- [0008 Payments: Stripe Checkout, webhooks and the fake gateway](docs/adr/0008-payments-stripe-checkout-choreography.md)
- [0009 Admin endpoints per module and the nightly demo reset](docs/adr/0009-admin-and-nightly-reset.md)

## Run locally

Requires Docker.

```bash
cd deploy
cp .env.example .env
docker compose up -d --build
```

Then open http://localhost:8080.

The local stack always uses the **simulated payment page** (the Fake provider), so checkout works
with no Stripe account: you choose whether the payment succeeds or fails. The Fake provider is allowed
here through `Payments__AllowFakeProviderInProduction`, and `docker-compose.prod.yml` never sets it.
To try real Stripe test mode locally, run from source instead (next section).

## Develop from source

```bash
docker compose -f deploy/docker-compose.yml up -d postgres   # PostgreSQL on localhost:5433
dotnet run --project src/Bootstrapper/Shop.Api               # API on http://localhost:5080
cd web/shop-web && npm ci && npx ng serve                    # UI on http://localhost:4200 (proxies /api)
```

(`deploy/.env` must exist first: `cp deploy/.env.example deploy/.env`.)

### Checkout with Stripe test mode

You need a Stripe account in test mode and the [Stripe CLI](https://docs.stripe.com/stripe-cli).

1. Start forwarding webhooks to the API's dev port (5080, from `launchSettings.json`). The CLI prints a
   signing secret (`whsec_...`); leave it running.

   ```bash
   stripe listen --forward-to localhost:5080/api/payments/stripe/webhook
   ```

2. In a second terminal, start the API with the Stripe provider, your test secret key and that signing
   secret (Postgres and the UI as above):

   ```bash
   Payments__Provider=Stripe \
   Stripe__SecretKey=sk_test_... \
   Stripe__WebhookSecret=whsec_... \
   dotnet run --project src/Bootstrapper/Shop.Api
   ```

3. Open http://localhost:4200, place an order and pay with `4242 4242 4242 4242`. Stripe returns you to
   the order page, which turns Paid once the forwarded webhook lands.

## Testing

```bash
dotnet test --solution VoltLane.slnx -c Release
cd web/shop-web && npm ci && npx ng test --no-watch
```

- **Unit tests** for domain logic and building blocks, including `Catalog.UnitTests`, `Identity.UnitTests` and
  `Basket.UnitTests`.
- **Architecture tests** that fail the build if a module reaches into another module's internals.
- **Integration tests** against a real PostgreSQL started by **Testcontainers** (no mocked
  database).
- Frontend unit tests run through the Angular test runner.
- **OpenAPI snapshot:** after an API change, regenerate the snapshot with
  `UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --solution VoltLane.slnx`, then run
  `npm run api:types` in `web/shop-web`. CI fails if either is stale.

`TreatWarningsAsErrors` is on by design, including NuGet audit warnings: a newly published
vulnerability advisory for a dependency fails CI until it is addressed.

## CI/CD

Pull requests and pushes to `main` run backend and frontend CI, and build both Docker images
(without pushing) so a broken Dockerfile fails before merge. Every merge to `main` is tested,
built and deployed automatically: after CI succeeds, the Deploy workflow builds and pushes images
to GHCR, updates the server over SSH, and fails unless `/health/ready` reports `Healthy`.
Server setup: [deploy/SERVER_SETUP.md](deploy/SERVER_SETUP.md).

## Roadmap

- [x] **Plan 1:** Foundation and walking skeleton (modules, CI/CD, first deploy)
- [x] **Plan 2:** Catalog and storefront
- [x] **Plan 3:** Identity and Basket
- [x] **Plan 4:** Ordering and Payments (Stripe test mode)
- [x] **Plan 5:** Admin, nightly demo reset, rate limiting, polish
- [x] **Milestone 1:** a complete, deployed demo shop
- [ ] **Milestone 2:** order cancellation and refunds

## License

[MIT](LICENSE)
