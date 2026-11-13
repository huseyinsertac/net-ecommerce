# ADR-0001: Modular monolith over microservices

Status: Accepted. Date: 2026-11-13

## Context

VoltLane is a mid-sized e-commerce app (catalog, basket, ordering, payments, identity) built and operated by one developer, hosted cheaply, with a goal of showing clear boundaries and real consistency flows. Microservices would add operational cost (service discovery, distributed tracing, per-service deployment, network failure handling) without a scaling or team-size need that justifies it.

## Decision

Build one deployable (`Shop.Api`) composed of five modules: Catalog, Basket, Ordering, Payments, Identity. Boundaries are enforced in code, not by convention:

- Each module owns its own PostgreSQL schema and `DbContext`; there are no cross-schema joins or foreign keys.
- Modules talk to each other only through `{Module}.Contracts` (integration events and query interfaces), never through another module's Domain, Application or Infrastructure.
- Cross-module consistency uses an in-process event bus backed by a transactional outbox and an idempotent inbox, so the same messages could later travel over a real broker.
- Architecture tests and the compiler (project references) fail the build when a boundary is crossed.

## Consequences

- One process, one database, one pipeline: simple to run locally (`docker compose up`), cheap to host, easy to debug.
- Extracting a module into its own service later is a bounded change: its schema is already private, its public surface is already `Contracts`, and its events already go through the outbox. Replace the in-process bus with a broker and move the module behind its own host.
- Discipline is required: all modules share a process and deployment, so a failure or slow module can affect the rest. The architecture tests are the safeguard against gradual erosion into a big ball of mud.
