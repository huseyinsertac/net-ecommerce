# ADR-0003: One database, one schema per module

Status: Accepted. Date: 2026-11-13

## Context

ADR-0001 commits to a modular monolith whose module boundaries are enforced, not conventional. Data is the boundary that erodes first: one shared set of tables makes it trivial to join across modules, and every such join couples two modules at the storage level where the compiler cannot see it. At the same time a single PostgreSQL instance keeps hosting cheap and local development simple.

## Decision

Use one PostgreSQL database with one schema per module: `catalog`, `basket`, `ordering`, `payments`, `identity`. The schema name equals `IModule.Name`.

- Each module has its own `DbContext` (derived from `ModuleDbContext`) that sets its default schema. Its tables, its outbox and inbox tables and its `__ef_migrations_history` table all live in that schema.
- Each module has its own migrations, kept in `{Module}.Infrastructure/Persistence/Migrations` and applied per context at startup.
- A module's data is owned by that module. No foreign keys across schemas, no joins across schemas, no queries against another module's tables. Other modules obtain data through `{Module}.Contracts` (query interfaces) or react to its integration events, and keep their own copy of whatever they need.
- Identifiers from other modules are stored as plain values (for example a product id in a basket line), never as foreign keys.

## Consequences

- Boundaries are visible in the database itself and cannot be crossed by accident in an EF query, since a `DbContext` only maps its own schema.
- Extraction path: moving a module into its own service means moving its schema to a new database (`pg_dump --schema`), pointing its connection string there and replacing the in-process bus with a broker. No data model change is needed, because nothing outside the schema depends on it.
- Referential integrity across modules is eventual and application-enforced (events, tolerant readers) rather than guaranteed by the database.
- Reports that span modules need a read model or an explicit composition step; there is no ad-hoc cross-module SQL.
- Startup runs one migration per module context, each in its own DI scope, so a failure names the module that caused it.
