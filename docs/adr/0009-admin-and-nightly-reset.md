# ADR-0009: Admin endpoints in each module, optimistic concurrency, and a transactional nightly demo reset

Status: Accepted. Date: 2026-11-27

## Context

The public demo needs an admin area that anyone can try through one shared account, and it has to heal itself: visitors will edit prices, ship orders and leave the data in odd states. The app is a modular monolith with schema-per-module (ADR-0001, ADR-0003), so admin features must not break module boundaries, and the reset must not reach into tables a module owns by name.

## Decision

**Admin endpoints live in the module that owns the data, not in a separate admin module.** Catalog exposes `/api/catalog/admin/*` and Ordering `/api/ordering/admin/*`, each with its own application handlers (`Catalog.Application/Admin`, `Ordering.Application/Admin`) next to the domain they change. An admin module would need to read and write other modules' tables, or call a wide set of Contracts, which defeats the boundaries the architecture tests enforce. The cost is that "admin" is spread over modules; one authorization policy (`AuthorizationPolicies.Admin` from Identity.Contracts) applied to every `/api/*/admin/*` route keeps the rule in one place.

**Optimistic concurrency for the shared admin.** The demo admin is one account used by many visitors at once. Products carry a `version`; an update sends the version it loaded, and a stale write is rejected with 409 `catalog.concurrent_update` so the editor can reload instead of silently overwriting someone else's change. Stock changes never go below the reserved quantity, so an admin cannot invalidate an order that is mid-checkout. We chose this over pessimistic locks (which a closed browser tab would leave held) and over last-write-wins (which makes a shared demo confusing).

**Ship and deliver only.** Admins can move an order Paid to Shipped and Shipped to Delivered. Cancelling a paid order and refunding it needs the refund flow with Stripe (money state, partial failures, stock release for a committed reservation), which is Milestone 2. A cancel button without refunds would leave paid orders cancelled with the money still taken, which is worse than no button.

**Truncate and re-seed, not per-row cleanup.** Tracking and deleting what visitors created would need a marker on every table and would miss indirect effects (outbox rows, tokens, payments). The reset instead truncates every module schema's tables (taken from the module registry, so a new module is covered; no `CASCADE`, so a forgotten foreign key fails loudly instead of wiping something unexpected) and runs the same seeders as first start: the catalog, the demo admin, and 24 orders from 8 fictional customers so the admin has something to work on.

**One transaction, with defined failure behaviour.** The wipe runs in a single transaction with `SET LOCAL statement_timeout` and `lock_timeout`, so it either wipes everything or nothing, and a stuck lock cannot hang the API. If live traffic causes a lock timeout, deadlock or statement timeout, the transaction rolls back and the wipe is retried with backoff. After commit the re-seed runs and is retried too; in that window the data is empty rather than half-wiped. A nightly run that still fails is retried every `DemoReset:RetryDelay` up to `DemoReset:RetryCount` times, then waits for the next night, and every failure is logged at Error. The same code is available as `dotnet Shop.Api.dll reset-demo` (non-zero exit code on failure). The nightly service (`DemoReset:TimeOfDayUtc`, 02:00 UTC) runs only where `DemoReset:Enabled` is true, which is the production compose file only.

**A global rate limit with explicit exclusions.** `/api/*` is limited per IP (`RateLimiting:ApiPerMinute`, 300 per minute) on top of the stricter per-endpoint limits for sign-in and demo accounts. `/health*` is excluded so monitoring and the deploy check cannot be throttled, and so is the Stripe webhook, which arrives in bursts from Stripe and is protected by its signature and body cap instead (ADR-0008).

## Consequences

- Visitors can freely break the demo; it is back to a known state by the next morning, and `reset-demo` fixes it sooner.
- Everything users create is lost nightly, including accounts and orders. That is the intent for a public demo, and the UI says so.
- During the few seconds of a reset, requests can hit locks or an empty catalog. Acceptable at 02:00 UTC for a demo; a real shop would not do this.
- Admin code is spread over modules, but each part stays testable with its module's own integration tests.
- Known gaps: no audit log of admin changes, and no cancel or refund (Milestone 2). The 300 per minute limit is per IP, so many visitors behind one NAT share it.
- **When to revisit:** real customers (the reset must go, and the admin needs individual accounts and an audit trail), or admin features that span modules (consider a read model or a dedicated admin backend-for-frontend rather than a shared module).
