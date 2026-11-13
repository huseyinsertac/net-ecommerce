# Electronics E-Commerce Portfolio App — Design

Date: 2026-11-13
Status: Draft for approval

## 1. Intent and why

A hosted, medium-sized e-commerce app selling **electronics and tech gadgets**, built as a **portfolio project** to land a **mid-level, backend-heavy .NET role**.

It has two audiences, in this order:

1. **Non-technical hiring manager** — opens the live link, clicks around the storefront, decides whether to forward it. Judges on: looks finished, works end-to-end, no sign-up friction, no cold starts.
2. **Technical lead** — opens the GitHub repo. Judges on: clear module boundaries, real domain logic, consistency between modules, meaningful tests, CI/CD, and documented reasoning for decisions.

Success = the hiring manager can place an order and peek at the admin side within ~2 minutes without help, and the tech lead finds a codebase that a mid-level engineer could defend line by line in an interview.

## 2. Goals, non-goals, anti-goals

**Goals**
- Backend is the main event: modular monolith, clean architecture, rich domain in Catalog and Ordering, one real cross-module consistency flow (outbox).
- Frontend looks polished and complete, built mostly from an off-the-shelf component library.
- Publicly hosted with a demo experience, auto-resetting data, and a CI/CD pipeline.
- Repo is self-explanatory: README with screenshots/diagram, ADRs, one-command local startup.

**Non-goals**
- Reviews/ratings, wishlists, multi-currency, recommendations, multi-language.
- Real payments (Stripe test mode only), real shipping integrations, emails beyond what's needed (none required).
- Mobile app.

**Anti-goals (would make this a failure even if it works)**
- Frontend complexity dominating effort (custom design system, NgRx, elaborate animations).
- Over-engineering that reads as cargo-culting at mid level: microservices, Kafka/RabbitMQ, event sourcing, Kubernetes, Elasticsearch.
- "Clean architecture in name only": anemic domain, pass-through layers, modules reaching into each other's tables.
- Tutorial-clone feel: placeholder "Product 1" data, generic store.
- A demo that is broken, slow to wake up, or polluted by previous visitors.

## 3. Constraints

- Backend: **.NET 10**, ASP.NET Core Web API.
- Frontend: **Angular (latest)**, standalone components, signals.
- Database: **PostgreSQL**.
- Architecture: **modular monolith** + **clean architecture**, layered *projects per module* (Approach A, chosen).
- Hosted publicly, low monthly cost.

## 4. Approaches considered (module internals)

| | Approach | Trade-off |
|---|---|---|
| **A (chosen)** | Each module split into `Domain`, `Application`, `Infrastructure`, `Contracts` projects | Compiler enforces dependency rule; recognizable to reviewers; ~20 projects and some wiring boilerplate |
| B | One project per module, layers as folders | Fewer projects, faster; layering only by convention + architecture tests — some reviewers read it as nominal |
| C | Hybrid: full layers only in logic-heavy modules | Shows pragmatism; inconsistency needs explaining, can read as unfinished |

**Picked A** because for a portfolio, visible and verifiable structure beats leanness; the boilerplate is a one-time cost. Architecture tests are added on top to enforce module-to-module boundaries.

Also rejected at the outer level: microservices (wrong signal at mid level, ops cost), vertical-slice-only (conflicts with the clean-architecture requirement).

## 5. Solution structure

```
src/
  Bootstrapper/
    Shop.Api                    # single host: composes modules, middleware, auth, OpenAPI
  BuildingBlocks/
    Shop.SharedKernel           # Entity, AggregateRoot, ValueObject, DomainEvent, Result, errors
    Shop.Application.Abstractions  # ICommand/IQuery/handlers, dispatcher, pipeline behaviors
    Shop.Infrastructure.Common  # outbox/inbox base, EF conventions, event bus (in-process)
  Modules/
    Catalog/   Catalog.Domain | Catalog.Application | Catalog.Infrastructure | Catalog.Contracts
    Basket/    Basket.Domain  | Basket.Application  | Basket.Infrastructure  | Basket.Contracts
    Ordering/  Ordering.Domain| Ordering.Application| Ordering.Infrastructure| Ordering.Contracts
    Payments/  Payments.Domain| Payments.Application| Payments.Infrastructure| Payments.Contracts
    Identity/  Identity.Domain| Identity.Application| Identity.Infrastructure| Identity.Contracts
tests/
  Shop.ArchitectureTests        # NetArchTest/ArchUnitNET: layer + module boundary rules
  <Module>.UnitTests            # domain + application logic
  Shop.IntegrationTests         # WebApplicationFactory + Testcontainers (real Postgres)
web/
  shop-web                      # Angular app (storefront + admin area)
deploy/
  docker-compose.yml, docker-compose.prod.yml, Caddyfile
docs/
  adr/                          # architecture decision records
```

**Dependency rules (enforced by compiler + architecture tests)**
- `Domain` → SharedKernel only.
- `Application` → its own Domain, Application.Abstractions, other modules' **Contracts** only.
- `Infrastructure` → its own Application/Domain, Infrastructure.Common.
- No module references another module's Domain/Application/Infrastructure. Cross-module = Contracts (integration events + query interfaces) only.
- Endpoints live in each module (minimal APIs registered via a module `IModule` interface); `Shop.Api` just composes.

**Cross-cutting**
- Hand-written lightweight command/query dispatcher (no MediatR — commercial license since 2025) with pipeline behaviors: validation (FluentValidation), logging, transactions/unit of work.
- `Result<T>` for expected failures; mapped to RFC 7807 ProblemDetails at the endpoint edge.
- One PostgreSQL database, **one schema per module** (`catalog`, `basket`, `ordering`, `payments`, `identity`), each module with its own `DbContext` and migrations. No cross-schema joins or FKs.
- Observability: structured logging with Serilog, OpenTelemetry traces, health checks (`/health`). Keep it modest; no hosted APM required.
- OpenAPI/Scalar UI exposed in the hosted demo so the tech lead can poke the API.

## 6. Modules and domain

### Catalog
- **Category** (tree, max 2 levels) with a **spec schema**: list of attribute definitions (name, type: number/text/bool/enum, unit, filterable).
- **Product** (aggregate): name, slug, brand, description, category, images, specs (validated against category schema), status (Draft/Active/Archived).
- **ProductVariant**: SKU, option values (e.g. storage=256GB, color=Black), price (Money), stock on hand, stock reserved.
- Specs stored as **JSONB** with GIN index; filtering by spec ranges/values.
- Search: PostgreSQL full-text search (tsvector on name/brand/description) + filters + sorting + pagination.
- **Stock reservation** owned by Catalog: reserve / release / commit, invariants: cannot reserve more than available.
- Compare: endpoint returning specs for 2–3 products aligned by attribute.
- Publishes: `ProductPriceChanged`, `StockReserved`, `StockReservationFailed`.

### Basket
- Basket per user (logged-in) or per anonymous id (cookie/local id); merge on login.
- Lines reference variant id + quantity; price shown is current Catalog price (read via Catalog.Contracts query), not stored as truth.
- Discount code applied here for preview; authoritative validation at checkout.
- Thin domain; stored in Postgres (no Redis — avoid extra infra).

### Ordering
- **Order** aggregate: lines (snapshotted product name, variant, unit price at checkout), shipping address (value object), totals, applied discount, status.
- State machine: `Placed → Paid → Shipped → Delivered`, plus `Cancelled` (from Placed, or from Paid with refund flag). Invalid transitions rejected by domain.
- **DiscountCode** aggregate: percentage or fixed amount, expiry, usage limit, min order value; usage counted atomically.
- Checkout: re-validate prices and stock, create Order in `Placed` (stock reservation pending), raise `OrderPlaced`.
- Admin: list/filter orders, advance status (ship/deliver/cancel).

### Payments
- Stripe **test mode**, Stripe Checkout / Payment Element with webhook.
- `Payment` aggregate per order: Pending → Succeeded / Failed / Expired.
- Webhook endpoint verifies signature, idempotent (inbox), raises `PaymentSucceeded` / `PaymentFailed`.
- Payment timeout (e.g. 30 min) → `PaymentExpired`.

### Identity
- ASP.NET Core Identity + JWT access tokens (short-lived) + refresh tokens.
- Roles: `Customer`, `Admin`.
- **Demo customer = throwaway account per visitor** (assistant's call): "Try as customer" creates a fresh customer account, pre-seeded with 2–3 historical orders, and logs the visitor in. Visitors never see each other's basket or orders. Throwaway accounts are deleted by the nightly reset.
- **Demo admin = one shared account** (assistant's call): can view everything, change order statuses, create/edit products, variants and stock. Blocked for demo accounts: deleting products, changing passwords. Shared admin edits are tolerated until the nightly reset.
- **Login required to check out; no guest checkout** (assistant's call). One-click demo login removes the friction; this avoids orders without a customer and guest order lookup.

### The cross-module flow (outbox + inbox)

```
Ordering: PlaceOrder ──(outbox)──> OrderPlaced
Catalog:  on OrderPlaced → reserve stock ──> StockReserved | StockReservationFailed
Ordering: on StockReservationFailed → Cancel order (reason: out of stock)
Payments: on StockReserved → create Payment, return Stripe session to client
Payments: webhook → PaymentSucceeded | PaymentFailed ; timer → PaymentExpired
Ordering: on PaymentSucceeded → mark Paid
Catalog:  on PaymentSucceeded → commit reservation
Catalog + Ordering: on PaymentFailed/Expired → release reservation, cancel order
```

- **How the client gets the Stripe session** (assistant's call): `POST /checkout` returns `202 Accepted` with the order id. The Angular checkout page polls `GET /orders/{id}/checkout-status` (every ~500 ms, 30 s timeout), which returns `Pending | OutOfStock | ReadyToPay{stripeUrl} | Failed`. Because the bus is in-process, this normally resolves in under a second. The page shows a "Reserving your items…" state. This keeps the choreography intact; an ADR notes SSE/SignalR as the upgrade path.
- **Admin cancels a Paid order → real Stripe test-mode refund** (assistant's call): Ordering raises `OrderCancelled{wasPaid:true}`. Payments issues the refund and raises `PaymentRefunded`. Catalog returns the committed stock to on-hand. This is a second compensation flow, and it's cheap because the outbox/inbox machinery already exists.
- Each module has `outbox_messages` and `inbox_messages` tables in its own schema; integration events written in the same transaction as state changes.
- Background processor (hosted service, polling with `FOR UPDATE SKIP LOCKED`) dispatches outbox messages to in-process subscribers; inbox guarantees idempotent handling.
- Choreography, not a central saga orchestrator — simpler and defensible at mid level; ADR explains when you'd switch.

## 7. Frontend (Angular)

Principle: **impressive surface, simple internals.**

- Angular latest, standalone components, signals for state, services over HTTP; **no NgRx**.
- Component library: **Angular Material** (builder may choose PrimeNG if it reaches a better look faster) + light custom theme.
- Pages:
  - Landing/home: hero, featured categories, featured products, a visible "Demo accounts" panel with one-click login as customer/admin and the Stripe test card number.
  - Category listing: spec-driven filters (sidebar), sort, pagination, search.
  - Product page: image gallery, variant picker (price/stock update), spec table, add to cart, add to compare.
  - Compare view (2–3 products side by side).
  - Cart, checkout (address → Stripe), order confirmation, order history + order detail with status timeline.
  - Login/register.
  - Admin area: dashboard (few headline numbers: orders today, revenue, low stock), products list/create/edit (incl. variants, stock, image upload to a Docker volume served statically; specs validated against the category schema). **Categories and spec schemas are seed-only and not editable in the UI** (assistant's call: this avoids schema migration of existing products), orders list/detail with status actions, discount codes.
- Responsive (works on phone), loading skeletons, empty states, toast errors mapped from ProblemDetails.
- Typed API client generated from OpenAPI (e.g. NSwag or openapi-generator) — keeps frontend thin and shows contract discipline.
- Product images: realistic stock/royalty-free images stored in repo or object storage, served statically.

## 8. Seed data and demo experience

- 60–100 realistic products across 6–8 categories (phones, laptops, tablets, audio, wearables, cameras, gaming, accessories), with variants and full spec sets. Fictional or generic brand names are acceptable to avoid trademark/image-rights issues (builder's call, see §11).
- Several discount codes (one active and advertised on the landing page, e.g. `WELCOME10`).
- Historical orders for the demo customer and a spread of orders for the admin dashboard.
- **Nightly reset job** (02:00 UTC): truncate module data and re-seed. Implemented as an idempotent seeder runnable from CLI and on schedule.
- Rate limiting on public endpoints (ASP.NET Core rate limiter) to protect the demo.

## 9. Hosting and delivery

- Single small VPS (e.g. Hetzner CX22, ~€5/mo) running Docker Compose: `api`, `web` (static build via Caddy or nginx), `postgres`, `caddy` (TLS via Let's Encrypt). Builder may substitute a managed platform if preferred; must avoid cold starts.
- Custom domain (or provider subdomain) with HTTPS.
- Postgres data on a volume; daily `pg_dump` backup is nice-to-have, not required (demo data is reseeded).
- **GitHub Actions**:
  - On PR/push: build, unit tests, architecture tests, integration tests (Testcontainers), Angular lint/test/build.
  - On main: build & push Docker images (GHCR), deploy via SSH to the VPS (`docker compose pull && up -d`), run migrations on startup.
- Secrets (Stripe keys, JWT signing key, DB password) via GitHub secrets → env file on server; never committed.
- Local dev: `docker compose up` brings up everything; Stripe webhooks locally via Stripe CLI (documented).

## 10. Testing strategy

- **Architecture tests**: layer rules and module isolation (fail the build on violation).
- **Unit tests**: domain invariants (order state machine, discount rules, stock reservation, spec validation) and application handlers.
- **Integration tests**: API-level via `WebApplicationFactory` + Testcontainers Postgres, including the full checkout flow with Stripe faked at the Payments infrastructure boundary, and outbox delivery.
- **Frontend**: light — a few component tests and one Playwright happy-path (browse → add to cart → checkout with test card) optional / nice-to-have.
- Coverage is not a target; meaningful tests on domain and the cross-module flow are.

## 11. Repo presentation

- README: one-line pitch, live demo link + demo credentials, screenshots/GIF, architecture diagram (modules + flow), tech stack, "how to run locally", CI badge, links to ADRs.
- ADRs (short): modular monolith over microservices; projects-per-layer; schema-per-module; outbox/inbox + choreography; no MediatR; Postgres FTS over Elasticsearch; JSONB specs; VPS hosting.
- Conventional commits, small PRs (the history itself is part of the portfolio).

## 12. Milestones (user is actively job hunting → get something live early)

**Milestone 1 — "Live and sendable" (the first priority):**
- All BuildingBlocks; module projects for Catalog, Identity, Basket, Ordering and Payments.
- Catalog: categories, products, variants, specs, search + spec filters, product page, stock reservation.
- Identity: JWT login, one-click throwaway demo customer, shared demo admin.
- Basket, checkout, full outbox/inbox flow, Stripe test-mode payment, order history with status timeline.
- Admin: products and orders (list/edit, status changes).
- Seed data, nightly reset, rate limiting.
- Architecture tests, domain unit tests, checkout integration test.
- CI/CD to the VPS with HTTPS; README with live link, demo credentials, screenshots, architecture diagram, first ADRs.

**Milestone 2 — "Depth":**
- Discount codes; compare view; admin dashboard; image upload.
- Admin cancel of a paid order with Stripe refund + stock return.
- Remaining ADRs, OpenTelemetry tracing, optional Playwright happy-path, demo GIF.

Deploy as early as possible within Milestone 1, starting with the skeleton, so the pipeline and hosting are proven before features pile up.

## 13. Decided vs left to the builder

**Decided with the user**
- .NET + Angular + PostgreSQL; modular monolith + clean architecture.
- Backend-heavy, mid-level target; frontend impressive but not dominant.
- Publicly hosted.
- Domain: electronics and tech gadgets, with variants, category specs + filters, compare, stock reservation, order state machine, discount codes. Reviews/wishlist/multi-currency/recommendations out.
- Module internals: Approach A (layer projects per module).

**Assistant's calls (user may override)**
- .NET 10, latest Angular, standalone + signals, Angular Material.
- Custom dispatcher instead of MediatR; ASP.NET Identity + JWT.
- Stripe test mode; Postgres FTS; JSONB specs; schema-per-module; in-process event bus with outbox/inbox; choreography.
- Single VPS + Docker Compose + Caddy; GitHub Actions + GHCR.
- Demo accounts, read-mostly demo admin, nightly reset, rate limiting.

**Left to the builder**
- Exact library picks (FluentValidation version, NetArchTest vs ArchUnitNET, OpenAPI client generator, Serilog sinks).
- Exact seed products, brand naming, images source.
- UI theme details within "clean, modern electronics store".
- Order of implementation (suggest: building blocks → Catalog → Identity → Basket → Ordering → Payments → admin → deploy).
