# ADR-0006: Catalog search with PostgreSQL full-text search, JSONB specs and a SQL read side

Status: Accepted. Date: 2026-11-14

## Context

The storefront needs product search: free text with partial words ("lapt", "noise canc"), brand, price and stock filters, and category-specific spec filters (RAM range, operating system, "has ANC"). Specs differ per category, so they cannot be fixed columns. The catalog is a few hundred to a few thousand products, and the app runs as a single container next to one PostgreSQL instance.

| | Option | Trade-off |
|---|---|---|
| A | Elasticsearch / OpenSearch | Best relevance tooling and typo tolerance; a second stateful service to run, secure, back up and keep in sync with the database (more outbox plumbing, eventual consistency), far beyond what this catalog size needs |
| B | `LIKE '%term%'` / EF Core `Contains` | No setup; no ranking, no stemming, and sequential scans |
| **C (chosen)** | PostgreSQL full-text search (`tsvector` + GIN) with JSONB specs | One database, transactionally consistent with writes, indexed, ranked; typo tolerance is limited |

## Decision

**No search engine.** Search uses a stored `tsvector` column (name, brand, description) with a GIN index, inside the `catalog` schema.

**Text queries.** `websearch_to_tsquery` handles whole words safely but has no prefix matching, and `to_tsquery` supports prefix (`lapt:*`) but throws on raw user punctuation. So the query text is tokenized in code (letters and digits only, so `'; drop table`, `c++`, emoji and whitespace-only input can never reach the parser as syntax) and a `to_tsquery` string is built from the tokens: whole words use the `english` configuration (stemming, stop words), while the trailing word, which the user may still be typing, is matched as a prefix against both `simple` and `english`, so a stop word such as "the" or "for" at the end does not collapse the query into an empty match. Text with no real words behaves exactly like no text: the query has no text predicate and sorts by `featured`, not `relevance`.

**Specs are JSONB with whitelisted keys.** Each product stores its specs in one `jsonb` column. Each leaf category declares a spec schema (key, label, type, unit, filterable). A spec filter is accepted only if the category is a leaf and the key is declared filterable, and its shape matches the type (range for numbers; value list for enum, text and boolean). Anything else is a 400 with a `spec` field error, never ignored. Because keys come only from the schema, they can be used safely in SQL, and values are always bound as parameters. Enum and boolean filters use containment (`specs @> '{"os":"Linux"}'`), served by a GIN `jsonb_path_ops` index. Number ranges cannot use containment, so they cast the value (`(specs->>'ram_gb')::numeric`) only inside a `CASE WHEN jsonb_typeof(...) = 'number'` guard, so rows where the key is missing or not numeric are skipped instead of raising a cast error.

**CQRS-lite.** Writes (aggregates with variants and stock reservation, optimistic concurrency) go through EF Core. Reads are hand-written SQL via Dapper in `Catalog.Infrastructure/Reading`, returning DTOs directly. Search with facets, counts and dynamic filters is awkward to express in LINQ and is much easier to inspect and tune as SQL. The handlers in `Catalog.Application` only see the `ICatalogReader` abstraction. Only `Active` products are visible to the read side.

**HTTP contract.** The public routes are `GET /api/catalog/categories`, `/categories/{slug}/facets`, `/products` and `/products/{slug}`. Spec filters travel as repeatable `spec=key:value` query parameters (`ram_gb:16..64`, `os:Windows 11,Linux`, `anc:true`). The generated OpenAPI document is committed (`web/shop-web/openapi/v1.json`) and a snapshot test fails when the contract changes without the file being regenerated, so the frontend types never drift.

## Consequences

- No extra infrastructure; search is always consistent with the data it searches, and there is nothing to reindex.
- Search relevance is adequate but basic: no typo tolerance, no synonyms, limited language handling beyond English stemming.
- JSONB specs keep the schema flexible per category at the cost of weaker typing in the database; the category spec schema is the single source of truth and is enforced on write and on query.
- Hand-written SQL needs integration tests against real PostgreSQL (the suite uses Testcontainers), and the read side does not share the EF model.

## When to revisit

- Users routinely mistype product names: add `pg_trgm` and trigram similarity as a fallback when full-text search returns nothing.
- The catalog grows to hundreds of thousands of products, or needs synonyms, faceting at scale, learning-to-rank or multi-language analyzers: move search to a dedicated engine and feed it from the outbox events.
- Range filters on a spec become slow: promote that spec to a typed, indexed column or an expression index.
