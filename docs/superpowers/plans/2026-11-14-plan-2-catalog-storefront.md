# Plan 2 — Catalog & Storefront Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the live demo look and behave like a real electronics shop. Visitors can browse categories, search, filter by specs, sort and page through about 80 realistic products with real photos, and open a product page with a gallery, a variant picker and a spec table. Behind that sits a Catalog module with a real domain: spec schemas per category, variants and stock reservation.

**Architecture:** The Catalog module gets a rich domain (`Category` with a spec schema, and a `Product` aggregate with variants and stock rules). Writes go through EF Core: specs are JSONB, variants are a child table, and `xmin` provides optimistic concurrency. Reads go through a thin SQL read side (Dapper) behind an Application interface, using PostgreSQL full-text search and JSONB filters. Seed data is a validated embedded JSON file that runs on startup into an empty database. Angular uses generated OpenAPI types, and all listing state lives in the URL.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, Dapper, FluentValidation, xUnit v3 + Shouldly + Testcontainers; Angular 22 (standalone, signals, `httpResource`), Angular Material, Vitest, openapi-typescript; sharp-cli for image conversion.

**Spec:** `docs/superpowers/specs/2026-11-13-ecommerce-portfolio-design.md` (§6 Catalog, §7 Frontend, §8 Seed data, §12 Milestone 1)

**Plan 2 of 5 for Milestone 1.** Next: Plan 3 (Identity & Basket), Plan 4 (Ordering & Payments), Plan 5 (Admin, reset, rate limiting, polish).

## Global Constraints

- Everything from Plan 1 still holds:
  - `net10.0`, Nullable, TreatWarningsAsErrors, central package versions in `Directory.Packages.props`.
  - Schema `catalog`, snake_case names.
  - Modules talk only through `{M}.Contracts`.
  - xUnit v3 + Shouldly; Testcontainers `postgres:18-alpine`.
  - Test commands: `dotnet test --project <path>` / `dotnet test --solution VoltLane.slnx` (Microsoft.Testing.Platform).
  - Angular: standalone, signals, Angular Material, no NgRx. Run `npx ng test --no-watch` with `CI=true`.
- **Layering:** Catalog domain types live in `Catalog.Domain` with no EF/ASP.NET references. Query and command handlers in `Catalog.Application` are `internal sealed`. SQL and EF live only in `Catalog.Infrastructure` (the architecture tests from Plan 1 enforce this).
- **No new infrastructure:** search is PostgreSQL FTS only. Dapper is the only new backend runtime package; openapi-typescript is the only new frontend dev tool.
- **Currency:** single currency USD. API amounts are JSON numbers with 2 decimals, and the frontend formats them with `Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })`.
- **Product brands are fictional.** Never use a real consumer-electronics brand (Apple, Samsung, Sony, Bose, Dell, Lenovo, Google, Xiaomi, etc.). Component descriptors inside specs are fine (e.g. "Intel Core Ultra 7", "Wi-Fi 7").
- **Photos:**
  - Unsplash-licensed photos only. Each must have no visible logo or brand mark.
  - Every photo is listed in `web/shop-web/public/images/CREDITS.md`, with photographer and Unsplash URL.
  - Stored as WebP: `<key>.webp` (max 1200 px on the long edge, quality 80) and `<key>-400.webp` (400 px, quality 75).
- **Image URLs:** `/images/products/<key>.webp` (`-400.webp` for thumbnails) and `/images/categories/<slug>.webp`, served by the web container.
- **Public routes** (all `GET`, anonymous):
  - `/api/catalog/categories`
  - `/api/catalog/categories/{slug}/facets`
  - `/api/catalog/products`
  - `/api/catalog/products/{slug}`
- **Error codes** (ProblemDetails `type`):
  - `catalog.category_not_found` (404)
  - `catalog.product_not_found` (404)
  - `validation.failed` (400, field errors)
  - `catalog.spec_*` (400; see Task 2)
  - `catalog.insufficient_stock` (409)
- **Visibility:** only `Active` products are visible publicly, everywhere (search, facets, counts, detail).

## Review Focus

1. **Hostile or messy search text** (`'; drop table`, `c++`, `   `, emoji, 300 chars): no 500. Punctuation is ignored, and text with no real words behaves like no search → test in Task 5.
2. **Partial words** (`lapt`, `noise canc`): match by prefix → test in Task 5.
3. **Invalid spec filters** (unknown key, non-filterable key, a range on an enum, a spec filter without a category, a malformed `spec=` value): 400 with a field error. They are never silently ignored and never cause a 500 → tests in Tasks 5 and 6.
4. **Two buyers reserving the last unit at once:** exactly one succeeds, and available stock never goes negative → test in Task 4.
5. **Draft and archived products** never appear in search, facets, category counts or detail → test in Task 5.

---

## File Structure

```
src/BuildingBlocks/Shop.Application.Abstractions/Paging/PagedResult.cs
src/BuildingBlocks/Shop.Infrastructure.Common/Modules/IModuleSeeder.cs
src/Bootstrapper/Shop.Api/Program.cs                         (seed hook)
src/Modules/Catalog/Catalog.Domain/
  Categories/(Category.cs, SpecDefinition.cs, SpecType.cs, SpecValue.cs, CatalogErrors.cs)
  Products/(Product.cs, ProductVariant.cs, OptionAxis.cs, NewVariant.cs, ProductStatus.cs)
src/Modules/Catalog/Catalog.Application/
  Abstractions/ICatalogReader.cs
  Categories/(GetCategoryTree.cs, GetCategoryFacets.cs, CategoryDtos.cs)
  Products/(SearchProducts.cs, SearchProductsValidator.cs, GetProductBySlug.cs, ProductDtos.cs, SpecFilter.cs, ProductSort.cs)
src/Modules/Catalog/Catalog.Infrastructure/
  Persistence/(CatalogDbContext.cs, Configurations/*.cs, Converters/*.cs, Migrations/)
  Reading/(CatalogReader.cs, SearchSql.cs, TsQueryBuilder.cs)
  Endpoints/(CatalogEndpoints.cs, SpecFilterParser.cs)
  Seeding/(CatalogSeeder.cs, catalog-seed.json, SeedModel.cs)
  CatalogModule.cs
tests/Catalog.UnitTests/                                     (new; domain + TsQueryBuilder + SpecFilterParser)
tests/Shop.IntegrationTests/Catalog/(CatalogPersistenceTests.cs, CatalogSearchTests.cs, CatalogEndpointTests.cs, CatalogSeedTests.cs, CatalogTestData.cs)
tests/Shop.IntegrationTests/OpenApiSnapshotTests.cs
web/shop-web/openapi/v1.json                                 (committed API snapshot)
web/shop-web/public/images/(products/*.webp, categories/*.webp, CREDITS.md)
web/shop-web/scripts/images/(sources.json, build-images.mjs)
web/shop-web/src/app/
  api/(schema.ts [generated], catalog-api.service.ts)
  core/(problem-interceptor.ts, money.pipe.ts)
  shared/(product-card/, stock-badge/stock-label.ts, not-found/)
  pages/home/ (updated), pages/product-list/(product-list.page.ts, filter-panel/, listing-query.ts), pages/product-detail/(product-detail.page.ts, gallery/, variant-picker/, variant-logic.ts)
docs/adr/0006-catalog-search-postgres-fts-jsonb-sql-read-side.md
```

---

### Task 1: Building-block hardening, paging and seed hook

These items were carried over from the Plan 1 final review, plus two small building blocks that Plan 2 needs.

**Files:**
- Modify: `src/BuildingBlocks/Shop.Infrastructure.Common/Persistence/ModuleDbContext.cs`, `Outbox/PendingIntegrationEvents.cs`, `Persistence/ModuleDbContextRegistration.cs`, `Outbox/OutboxProcessor.cs`
- Create: `src/BuildingBlocks/Shop.Application.Abstractions/Paging/PagedResult.cs`, `src/BuildingBlocks/Shop.Infrastructure.Common/Modules/IModuleSeeder.cs`
- Modify: `src/Bootstrapper/Shop.Api/Program.cs`, `appsettings.json`
- Test: `tests/Shop.Infrastructure.Common.IntegrationTests/*`, `tests/Shop.IntegrationTests/HostTests.cs`, `ShopApiFactory.cs`

**Interfaces (produced):**
```csharp
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{ public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize); }
public interface IModuleSeeder { string Module { get; } Task SeedAsync(CancellationToken ct); }   // resolved from its own DI scope
```
- **Drain check:** `ModuleDbContext.DrainPublishedIntegrationEvents` throws `InvalidOperationException("Integration event type '<FullName>' is not registered; pass its Contracts assembly to AddEventing.")` when `IntegrationEventTypeRegistry.Resolve(type.FullName)` returns null. Inject the registry through the existing constructor chain. Each `{M}DbContext` passes it through.
- **Pending events:**
  - `PendingIntegrationEvents` is cleared when domain-event dispatch throws (so a retried save in the same scope cannot drain stale events).
  - It implements `IDisposable` and logs a warning (`ILogger<PendingIntegrationEvents>`) when disposed with undrained events.
- **Outbox options:** `OutboxOptions` is bound from configuration section `Outbox` (keep the current defaults).
- **Seeding hook:** `Program.cs` runs every registered `IModuleSeeder` after migrations, each in its own scope, when `Database:SeedOnStartup` is true. It defaults to `true` in `appsettings.json`. `ShopApiFactory` sets it to `false`.

- [ ] **Step 1: Write the failing tests**
```csharp
[Fact] SaveChanges_PublishingUnregisteredEventType_Throws()          // message contains "not registered"
[Fact] DomainDispatchThrows_PendingEventsCleared_RetrySavesNoStaleEvent()
[Fact] PendingEvents_DisposedWithUndrained_LogsWarning()             // via a capturing ILogger
[Fact] OutboxOptions_BoundFromConfiguration()                         // Outbox:BatchSize=7 → options.BatchSize == 7
[Fact] FreshDatabase_AllModuleSchemasMigrated()                       // EXTEND: also asserts {schema}.__ef_migrations_history exists for all 5
[Fact] Startup_RunsRegisteredSeeders_WhenEnabled()                   // factory variant with SeedOnStartup=true + test seeder → called once
```
- [ ] **Step 2:** Run `dotnet test --project tests/Shop.Infrastructure.Common.IntegrationTests` and `--project tests/Shop.IntegrationTests`. Expected: the new tests FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Re-run both projects. Expected: all PASS.
- [ ] **Step 5: Commit** `feat(building-blocks): fail fast on unregistered events, paging, module seeders`.

---

### Task 2: Catalog domain — categories and spec schemas

**Files:**
- Create: `src/Modules/Catalog/Catalog.Domain/Categories/*`
- Create: `tests/Catalog.UnitTests/` (xUnit v3 + Shouldly; add it to `VoltLane.slnx`), `tests/Catalog.UnitTests/Categories/CategoryTests.cs`

**Interfaces (produced):**
```csharp
public enum SpecType { Number, Text, Boolean, Enum }
public sealed record SpecDefinition(string Key, string Label, SpecType Type, string? Unit, bool Filterable, IReadOnlyList<string> Options);
public abstract record SpecValue;  public sealed record NumberValue(decimal Value) : SpecValue;
public sealed record TextValue(string Value) : SpecValue;   public sealed record BoolValue(bool Value) : SpecValue;   // Enum values are TextValue
public sealed class Category : AggregateRoot<Guid>
{ string Slug; string Name; string Icon; Guid? ParentId; int SortOrder; IReadOnlyList<SpecDefinition> SpecSchema;
  static Result<Category> Create(string slug, string name, string icon, Guid? parentId, int sortOrder, IReadOnlyList<SpecDefinition> specSchema);
  Result ValidateSpecs(IReadOnlyDictionary<string, SpecValue> values);
  SpecDefinition? FindSpec(string key); }
public static class CatalogErrors { /* static Error members, codes below */ }
```
**Rules:**
- **Slug:** matches `^[a-z0-9]+(-[a-z0-9]+)*$` and is ≤ 80 chars.
- **Spec keys:** match `^[a-z][a-z0-9_]*$` and are unique. `Enum` needs ≥ 1 option, and no option may contain `,` (the URL filter uses `,` as a separator). Non-`Enum` types need an empty `Options`.
- **`ValidateSpecs`:**
  - every defined key is required (`catalog.spec_missing`)
  - no unknown keys (`catalog.spec_unknown`)
  - the type must match (`catalog.spec_type_mismatch`)
  - an Enum value must be one of its options (`catalog.spec_invalid_option`)
- **Error type:** all of these return `ErrorType.Validation` with a message naming the key.
- **Ids:** `Guid.CreateVersion7()`, assigned in the factory.

- [ ] **Step 1: Write the failing tests:** `Create_ValidCategory_Succeeds`, `Create_BadSlug_Fails` (theory: `"Laptops"`, `"lap tops"`, `"-x"`, `""`), `Create_DuplicateSpecKey_Fails`, `Create_EnumWithoutOptions_Fails`, `Create_EnumOptionWithComma_Fails`, `ValidateSpecs_AllPresentAndTyped_Succeeds`, and `ValidateSpecs_{Missing,Unknown,TypeMismatch,InvalidOption}_FailsWithCode` (each asserts `Error.Code`).
- [ ] **Step 2:** Run `dotnet test --project tests/Catalog.UnitTests`. Expected: build FAILS.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Re-run. Expected: PASS. `dotnet test --project tests/Shop.ArchitectureTests` still PASSES.
- [ ] **Step 5: Commit** `feat(catalog): category aggregate with validated spec schema`.

---

### Task 3: Catalog domain — products, variants, stock reservation

**Files:**
- Create: `src/Modules/Catalog/Catalog.Domain/Products/*`
- Test: `tests/Catalog.UnitTests/Products/ProductTests.cs`, `StockTests.cs`

**Interfaces:**
- Consumes: `Category`, `SpecValue`, `CatalogErrors` (Task 2); `Money`, `Result`, `AggregateRoot` (SharedKernel)
- Produces:
```csharp
public enum ProductStatus { Draft, Active, Archived }
public sealed record OptionAxis(string Key, string Label);
public sealed record NewVariant(string Sku, IReadOnlyDictionary<string, string> Options, Money Price, int StockOnHand);
public sealed class ProductVariant : Entity<Guid>
{ string Sku; IReadOnlyDictionary<string,string> Options; Money Price; int StockOnHand; int StockReserved; int Available => StockOnHand - StockReserved; }
public sealed class Product : AggregateRoot<Guid>
{ string Slug; string Name; string Brand; string Description; Guid CategoryId; IReadOnlyList<string> ImageKeys;
  IReadOnlyDictionary<string, SpecValue> Specs; IReadOnlyList<OptionAxis> OptionAxes; IReadOnlyList<ProductVariant> Variants;
  ProductStatus Status; bool IsFeatured; DateTime CreatedAtUtc;
  static Result<Product> Create(Category category, string slug, string name, string brand, string description,
      IReadOnlyList<string> imageKeys, IReadOnlyDictionary<string, SpecValue> specs, IReadOnlyList<OptionAxis> optionAxes,
      IReadOnlyList<NewVariant> variants, bool isFeatured, DateTime createdAtUtc);   // starts Draft
  Result Publish(); Result Archive();
  Result ReserveStock(Guid variantId, int quantity); Result ReleaseStock(Guid variantId, int quantity); Result CommitStock(Guid variantId, int quantity); }
```
**Rules:**
- **Create:**
  - Name is 1–200 chars, and Brand and Description are non-empty.
  - Specs are validated with `category.ValidateSpecs`.
  - At least 1 image key and at least 1 variant.
  - Each variant's option keys equal exactly the axis keys, and no two variants share an option combination.
  - SKUs are non-empty, unique within the product, uppercase `^[A-Z0-9-]+$`.
  - Price > 0; stock ≥ 0.
- **Status:** `Publish` moves Draft or Archived → Active. `Archive` moves Active → Archived. Any other transition fails with `catalog.invalid_status_transition` (Conflict).
- **Stock:**
  - `quantity ≤ 0` → `catalog.invalid_quantity` (Validation).
  - Unknown variant → `catalog.variant_not_found` (NotFound).
  - Reserve fails when quantity > Available → `catalog.insufficient_stock` (Conflict).
  - Release fails when quantity > StockReserved → `catalog.release_exceeds_reserved` (Conflict).
  - Commit fails when quantity > StockReserved (same code); on success it lowers both StockOnHand and StockReserved.
- **Domain events:** none in this plan (integration events arrive in Plan 4).

- [ ] **Step 1: Write the failing tests:**
  - **Create:** `Create_Valid_StartsDraft`, `Create_SpecsInvalid_PropagatesSpecError`, `Create_NoVariants_Fails`, `Create_VariantMissingAxis_Fails`, `Create_DuplicateOptionCombination_Fails`, `Create_DuplicateSku_Fails`, `Create_ZeroPrice_Fails`, `Create_NoImages_Fails`
  - **Status:** `Publish_FromDraft_Active`, `Archive_FromDraft_Fails`
  - **Stock:** `Reserve_WithinAvailable_IncreasesReserved`, `Reserve_MoreThanAvailable_InsufficientStock`, `Reserve_ZeroQuantity_Invalid`, `Release_MoreThanReserved_Fails`, `Commit_ReducesOnHandAndReserved`, `Reserve_UnknownVariant_NotFound`
- [ ] **Step 2:** Run `dotnet test --project tests/Catalog.UnitTests`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Re-run. Expected: PASS.
- [ ] **Step 5: Commit** `feat(catalog): product aggregate with variants and stock reservation rules`.

---

### Task 4: Catalog persistence

**Files:**
- Modify: `src/Modules/Catalog/Catalog.Infrastructure/Persistence/CatalogDbContext.cs`
- Create: `Persistence/Configurations/{CategoryConfiguration,ProductConfiguration,ProductVariantConfiguration}.cs`, `Persistence/Converters/{SpecValuesJsonConverter,JsonListConverter}.cs`, migration `CatalogModel` (`dotnet tool run dotnet-ef migrations add CatalogModel -p src/Modules/Catalog/Catalog.Infrastructure -s src/Bootstrapper/Shop.Api -c CatalogDbContext -o Persistence/Migrations`)
- Test: `tests/Shop.IntegrationTests/Catalog/CatalogPersistenceTests.cs`, `CatalogTestData.cs`

**Mapping (decided):**
- **`categories`:**
  - `slug` unique
  - `spec_schema jsonb` (list of SpecDefinition, serialized with camelCase)
  - `parent_id` nullable self-reference (FK within the schema is allowed)
- **`products`:**
  - `slug` unique and `category_id` FK
  - `specs jsonb`: object with native JSON types (`{"ram_gb":16,"anc":true,"os":"Windows 11"}`)
  - `image_keys text[]`, `option_axes jsonb`
  - `status` as text (`Draft`, `Active`, `Archived`), `is_featured`, `created_at_utc`
  - shadow property `SearchVector` (`NpgsqlTsVector`) as a stored generated column:
    `setweight(to_tsvector('english', coalesce(name,'')), 'A') || setweight(to_tsvector('english', coalesce(brand,'')), 'B') || setweight(to_tsvector('english', coalesce(description,'')), 'C')`
  - indexes: GIN on `search_vector`; GIN `jsonb_path_ops` on `specs`; btree on `(status, category_id)`
- **`product_variants`:**
  - FK `product_id` with cascade
  - `sku` unique across the whole table, `options jsonb`
  - `price numeric(12,2)`, `stock_on_hand int`, `stock_reserved int`
  - `CHECK (stock_reserved >= 0 AND stock_reserved <= stock_on_hand AND stock_on_hand >= 0)`
  - PostgreSQL `xmin` mapped as the concurrency token (`IsRowVersion()` on a `uint` shadow property)
  - index on `price`

  Reservations update only the variant row, so the token belongs on the variant, not the product.
- **Value conversions:** `Money` maps to the `price` column (USD implied). Value comparers must be set for the JSON-converted collections, so EF detects changes.

- [ ] **Step 1: Write the failing tests:**
  - `Category_RoundTrips_WithSpecSchema`
  - `Product_RoundTrips_SpecsVariantsAxesImages`
  - `SpecsStoredAsNativeJsonTypes`: raw SQL `SELECT jsonb_typeof(specs->'ram_gb')` → `number`
  - `DuplicateSku_AcrossProducts_Rejected`: DbUpdateException
  - `ConcurrentReserveOfLastUnit_OneSucceeds_OtherGetsConcurrencyConflict`: two scopes load the same product, both reserve 1 of a variant with stock 1, and both save → exactly one save succeeds; the other throws `DbUpdateConcurrencyException`; DB `stock_reserved == 1` [Review Focus 4]
  - `CheckConstraint_PreventsReservedAboveOnHand`: raw `UPDATE` violating it throws a PostgresException with SqlState `23514`
- [ ] **Step 2:** Run `dotnet test --project tests/Shop.IntegrationTests`. Expected: FAIL.
- [ ] **Step 3:** Implement the configurations, converters and migration.
- [ ] **Step 4:** Re-run. Expected: PASS, and the existing tests still pass.
- [ ] **Step 5: Commit** `feat(catalog): persistence with JSONB specs, FTS vector and stock concurrency guard`.

---

### Task 5: Catalog read side and queries

**Files:**
- Create: `Catalog.Application/Abstractions/ICatalogReader.cs`, `Categories/*`, `Products/*`
- Create: `Catalog.Infrastructure/Reading/{CatalogReader,SearchSql,TsQueryBuilder}.cs`
- Modify: `CatalogModule.cs` (register the reader as scoped, using the module's `CatalogDbContext` connection)
- Test: `tests/Catalog.UnitTests/TsQueryBuilderTests.cs`, `tests/Shop.IntegrationTests/Catalog/CatalogSearchTests.cs`

**Interfaces (produced):**
```csharp
public enum ProductSort { Featured, Relevance, PriceAsc, PriceDesc, Newest }
public sealed record SpecFilter(string Key, decimal? Min, decimal? Max, IReadOnlyList<string> Values);
public sealed record SearchProducts(string? Category, string? Text, IReadOnlyList<string> Brands, decimal? MinPrice, decimal? MaxPrice,
    bool InStockOnly, bool FeaturedOnly, IReadOnlyList<SpecFilter> Specs, ProductSort Sort, int Page, int PageSize)
    : IQuery<PagedResult<ProductSummaryDto>>;
public sealed record GetProductBySlug(string Slug) : IQuery<ProductDetailDto>;
public sealed record GetCategoryTree : IQuery<IReadOnlyList<CategoryNodeDto>>;
public sealed record GetCategoryFacets(string Slug) : IQuery<CategoryFacetsDto>;

public sealed record ProductSummaryDto(Guid Id, string Slug, string Name, string Brand, string CategorySlug, string ImageUrl,
    decimal PriceFrom, decimal PriceTo, bool InStock, bool IsFeatured);
public sealed record ProductImageDto(string Url, string ThumbUrl);
public sealed record CategoryRefDto(string Slug, string Name);
public sealed record SpecRowDto(string Key, string Label, string Value);          // Value formatted: "16 GB", "Yes"/"No", "Windows 11"
public sealed record OptionAxisDto(string Key, string Label, IReadOnlyList<string> Values);   // values in first-seen variant order
public sealed record VariantDto(Guid Id, string Sku, IReadOnlyDictionary<string, string> Options, decimal Price, int Available);
public sealed record ProductDetailDto(Guid Id, string Slug, string Name, string Brand, string Description, IReadOnlyList<CategoryRefDto> Breadcrumb,
    IReadOnlyList<ProductImageDto> Images, IReadOnlyList<SpecRowDto> Specs, IReadOnlyList<OptionAxisDto> Options, IReadOnlyList<VariantDto> Variants);
public sealed record CategoryNodeDto(string Slug, string Name, string Icon, string ImageUrl, int ProductCount, IReadOnlyList<CategoryNodeDto> Children);
public sealed record FacetOptionDto(string Value, int Count);
public sealed record SpecFacetDto(string Key, string Label, string Type, string? Unit, decimal? Min, decimal? Max, IReadOnlyList<FacetOptionDto> Options); // Type: "number"|"enum"|"boolean"
public sealed record CategoryFacetsDto(string Slug, string Name, IReadOnlyList<FacetOptionDto> Brands, decimal MinPrice, decimal MaxPrice, IReadOnlyList<SpecFacetDto> Specs);

public interface ICatalogReader {
  Task<PagedResult<ProductSummaryDto>> SearchAsync(ProductSearchCriteria criteria, CancellationToken ct);
  Task<ProductDetailDto?> GetProductAsync(string slug, CancellationToken ct);
  Task<IReadOnlyList<CategoryNodeDto>> GetCategoryTreeAsync(CancellationToken ct);
  Task<CategoryFacetsDto?> GetFacetsAsync(string slug, CancellationToken ct);
  Task<CategoryInfo?> GetCategoryAsync(string slug, CancellationToken ct); }        // Id, IsLeaf, SpecSchema, descendant ids incl. self
public sealed record CategoryInfo(Guid Id, string Slug, bool IsLeaf, IReadOnlyList<SpecDefinition> SpecSchema, IReadOnlyList<Guid> CategoryIds);
public sealed record ProductSearchCriteria(IReadOnlyList<Guid>? CategoryIds, string? TsQuery, IReadOnlyList<string> Brands, decimal? MinPrice,
    decimal? MaxPrice, bool InStockOnly, bool FeaturedOnly, IReadOnlyList<(SpecDefinition Def, SpecFilter Filter)> Specs, ProductSort Sort, int Page, int PageSize);
public static class TsQueryBuilder { public static string? Build(string? text); }   // in Catalog.Infrastructure/Reading, but pure → unit tested
```

**Validation:**
- **`SearchProductsValidator`** (FluentValidation): Page ≥ 1; PageSize 1–48; Text ≤ 200 chars; MinPrice/MaxPrice ≥ 0 and Min ≤ Max; Brands ≤ 20; Specs ≤ 20.
- **The handler adds:**
  - When `Category` is set and unknown → `catalog.category_not_found`.
  - When Specs are non-empty and Category is null → `ValidationError {"spec": ["Spec filters require a category."]}`.
  - When the category is not a leaf → `{"spec": ["Spec filters require a leaf category."]}`.
  - An unknown or non-filterable key → `{"spec": ["Unknown or non-filterable spec 'x'."]}`.
  - Min/Max on a non-Number spec, or Values on a Number spec → `{"spec": ["Spec 'x' does not support ranges."]}` (or `"…values."`).
  - Boolean values must be `true` or `false`.
- **Defaults:** PageSize defaults to 24. Sort defaults to `Relevance` when there is text, otherwise `Featured`.

**TsQueryBuilder algorithm:**
```
tokens = Regex.Split(text.ToLowerInvariant(), "[^\\p{L}\\p{N}]+").Where(t => t.Length > 0).Take(8)
if none → null
return string.Join(" & ", tokens.Select((t, i) => i == last ? t + ":*" : t))
```
The SQL uses `to_tsquery('english', @tsquery)`, always as a parameter and never concatenated.

**Search SQL semantics:**
- **Base filter:** `status = 'Active'`, plus `category_id = ANY(@ids)` when a category is given (a parent includes its descendants).
- **Text:** `search_vector @@ query`.
- **Brands:** case-insensitive `brand = ANY`.
- **Spec filters:** `(specs->>@key)::numeric BETWEEN` for ranges; `specs->>@key = ANY(@values)` for Enum/Text; `(specs->>@key)::boolean = @b` for Boolean. Each key and value is a parameter. Spec keys are whitelisted by the handler before they reach SQL.
- **Price and stock:** a product matches when **some** variant satisfies the price range, and also `stock_on_hand - stock_reserved > 0` when InStockOnly.
- **Summary values:** `PriceFrom`/`PriceTo` are the min/max over all of the product's variants. `InStock` = any variant available > 0. `ImageUrl` is the first image's thumbnail.
- **Sort** (always ending with the tiebreak `name, id` so paging is stable):
  - Featured: `is_featured DESC, name`
  - Relevance: `ts_rank DESC`
  - PriceAsc/PriceDesc: by PriceFrom
  - Newest: `created_at_utc DESC`
- **Paging:** `TotalCount` comes from the same filtered set. A page past the end returns an empty `Items` and the real `TotalCount`.
- **Facets** cover active products in the category subtree:
  - brand counts
  - min/max variant price
  - for a leaf category, one entry per filterable spec: Number → min/max; Enum → option counts in schema order, with zero counts included; Boolean → counts for `true`/`false`
  - for a parent category, `Specs` is empty
- **Category tree:** `ProductCount` = active products in the subtree. Ordered by SortOrder.
- **Detail:** 404 unless Active. The breadcrumb is root → leaf. Specs follow schema order. Formatting:
  - numbers use invariant culture with trailing zeros trimmed, plus `" " + unit` when a unit exists
  - booleans are `Yes`/`No`

`CatalogTestData` builds a small fixed dataset through the domain factories (2 parent + 3 leaf categories, about 12 products including 1 Draft and 1 Archived), so these tests don't depend on the seed file.

- [ ] **Step 1: Write the failing tests**
```csharp
// TsQueryBuilderTests
[Theory] ("noise canc", "noise & canc:*"), ("lapt", "lapt:*"), ("'; drop table x", "drop & table & x:*"), ("c++", "c:*"), ("   ", null), ("🔋💥", null)
[Fact]   Build_LongInput_TakesFirst8Tokens()
// CatalogSearchTests (against CatalogTestData)
[Fact] Search_NoFilters_ReturnsOnlyActive_FeaturedFirst()                 // [Review Focus 5] draft/archived slugs absent
[Fact] Search_PrefixText_FindsLaptops()                                    // "lapt" → laptop products  [Review Focus 2]
[Fact] Search_HostileText_NoErrorAndSaneResult()                           // "'; drop table products; --" → 200-equivalent success [Review Focus 1]
[Fact] Search_ParentCategory_IncludesDescendants()
[Fact] Search_BrandFilter_CaseInsensitive()
[Fact] Search_PriceRange_MatchesAnyVariant()
[Fact] Search_InStockOnly_ExcludesFullyReservedOrZeroStock()
[Fact] Search_SpecNumberRange_And_EnumValues_And_Boolean()
[Fact] Search_SortPriceAsc_StableAcrossPages()                             // concatenated pages == full sorted list, no duplicates
[Fact] Search_PageBeyondEnd_EmptyItemsRealTotal()
[Fact] Search_SpecWithoutCategory_ValidationError()                         // [Review Focus 3] errors["spec"]
[Fact] Search_UnknownOrNonFilterableSpec_ValidationError()                  // [Review Focus 3]
[Fact] Search_RangeOnEnumSpec_ValidationError()                             // [Review Focus 3]
[Fact] Search_UnknownCategory_NotFound()
[Fact] Facets_LeafCategory_BrandsPriceAndSpecFacets_ExcludeDraft()          // [Review Focus 5]
[Fact] CategoryTree_CountsActiveOnly_InSortOrder()                          // [Review Focus 5]
[Fact] ProductDetail_Draft_NotFound()                                        // [Review Focus 5]
[Fact] ProductDetail_FormatsSpecsAndOrdersAxes()                            // "16 GB", "Yes"
```
- [ ] **Step 2:** Run `dotnet test --project tests/Catalog.UnitTests` and `--project tests/Shop.IntegrationTests`. Expected: FAIL.
- [ ] **Step 3:** Implement, using Dapper for search and facets. EF no-tracking queries are fine for the detail and tree reads.
- [ ] **Step 4:** Re-run. Expected: PASS.
- [ ] **Step 5: Commit** `feat(catalog): SQL read side with full-text search, spec filters, facets and detail queries`.

---

### Task 6: Catalog HTTP endpoints, OpenAPI contract and ADR

**Files:**
- Create: `Catalog.Infrastructure/Endpoints/{CatalogEndpoints,SpecFilterParser}.cs`
- Modify: `CatalogModule.MapEndpoints`
- Create: `tests/Shop.IntegrationTests/Catalog/CatalogEndpointTests.cs`, `tests/Shop.IntegrationTests/OpenApiSnapshotTests.cs`, `web/shop-web/openapi/v1.json`, `docs/adr/0006-catalog-search-postgres-fts-jsonb-sql-read-side.md`
- Test: `tests/Catalog.UnitTests/SpecFilterParserTests.cs`

**Interfaces:**
- Consumes: the Task 5 queries and DTOs; `IDispatcher`; `ErrorResults.ToProblem` (Shop.Api).

  Infrastructure can't reference Shop.Api, so move `ErrorResults` to `Shop.Infrastructure.Common/Http/ErrorResults.cs` (namespace `Shop.Infrastructure.Common.Http`) and update its usages and tests.
- Produces the HTTP contract:
```
GET /api/catalog/categories                         → 200 CategoryNodeDto[]
GET /api/catalog/categories/{slug}/facets           → 200 CategoryFacetsDto | 404
GET /api/catalog/products?category=&q=&brand=&brand=&minPrice=&maxPrice=&inStock=true&featured=true&spec=ram_gb:16..64&spec=os:Windows 11,Linux&spec=anc:true&sort=priceAsc&page=1&pageSize=24
                                                    → 200 PagedResult<ProductSummaryDto> | 400 | 404
GET /api/catalog/products/{slug}                    → 200 ProductDetailDto | 404
public static class SpecFilterParser { public static bool TryParse(string raw, out SpecFilter filter, out string error); }
```
**Spec filter grammar:** `key:min..max`, `key:min..` and `key:..max` are ranges (invariant-culture decimals); `key:v1,v2` is a value list (trimmed, empty entries dropped); `key:true` is a value list of one item.

Any malformed `spec` value (no `:`, empty key, `..` with both sides empty, a non-numeric range bound) → 400 ValidationError with `errors["spec"]` naming the raw value.

The `sort` values are camelCase versions of `ProductSort` (`featured|relevance|priceAsc|priceDesc|newest`). An unknown sort → 400 `errors["sort"]`.

Every endpoint declares `.Produces<T>()` and `.ProducesProblem()`, and is tagged `"Catalog"`, so the OpenAPI document carries complete schemas. JSON is camelCase.

**OpenAPI snapshot test:** the test fetches `/openapi/v1.json` from the factory, removes `servers`, and compares it (normalized, indented) to `web/shop-web/openapi/v1.json`.
- When env `UPDATE_OPENAPI_SNAPSHOT=1` is set, it rewrites the file and passes.
- Otherwise a mismatch fails with "API contract changed: run with UPDATE_OPENAPI_SNAPSHOT=1 and regenerate frontend types".

**ADR-0006** covers why there's no Elasticsearch, `websearch` vs `to_tsquery` prefix matching, JSONB plus whitelisted keys, why reads use SQL while writes use EF (CQRS-lite), and when to revisit (pg_trgm typo tolerance, a dedicated search engine at scale).

- [ ] **Step 1: Write the failing tests**
```csharp
// SpecFilterParserTests
[Theory] ("ram_gb:16..64", min 16 max 64), ("ram_gb:16..", min 16), ("ram_gb:..64", max 64), ("os:Windows 11,Linux", values 2), ("anc:true", values ["true"])
[Theory] ("ram_gb", ":16", "ram_gb:..", "ram_gb:a..b") → false with error
// CatalogEndpointTests (seeded via CatalogTestData)
[Fact] GetCategories_200_Tree()
[Fact] GetProducts_QueryStringBinding_AllFilters()        // brand repeated, spec repeated, sort=priceAsc
[Fact] GetProducts_MalformedSpec_400_WithSpecError()       // [Review Focus 3] problem+json, errors.spec
[Fact] GetProducts_UnknownSort_400()
[Fact] GetProducts_PageSize100_400()
[Fact] GetProduct_UnknownSlug_404_ProblemType()           // type == "catalog.product_not_found"
[Fact] GetFacets_UnknownCategory_404()
// OpenApiSnapshotTests
[Fact] OpenApiDocument_MatchesCommittedSnapshot()
```
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement. Then generate the snapshot by running `UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --project tests/Shop.IntegrationTests --filter-method "*OpenApiDocument_MatchesCommittedSnapshot"` (or the MTP equivalent filter).
- [ ] **Step 4:** Re-run without the variable. Expected: PASS. Then run the full solution tests: PASS.
- [ ] **Step 5:** Write ADR-0006.
- [ ] **Step 6: Commit** `feat(catalog): public catalog API with spec filter syntax and OpenAPI snapshot`.

---

### Task 7: Product and category photos

**Files:**
- Create: `web/shop-web/scripts/images/sources.json`, `web/shop-web/scripts/images/build-images.mjs`, `web/shop-web/public/images/products/*.webp`, `web/shop-web/public/images/categories/*.webp`, `web/shop-web/public/images/CREDITS.md`
- Modify: `web/shop-web/Caddyfile.web` (`/images/*` → `Cache-Control: public, max-age=604800`)

**Interfaces (produced):**
- An image key list: lowercase kebab keys such as `phone-dark-01` and `laptop-silver-02`.
- `sources.json` has entries `{ key, unsplashId, photographer, unsplashUrl, category }`.
- Category images are named by slug: `phones, laptops, tablets, audio, headphones, earbuds, speakers, wearables, cameras, gaming, accessories, chargers-power, cables-hubs`.

**How to get the photos:**
1. Find candidates through Unsplash's public search JSON (`https://unsplash.com/napi/search/photos?query=<term>&per_page=30`; no key needed). Download them from `https://images.unsplash.com/photo-<id>?w=1600&fm=jpg&q=85`.
2. **Look at every candidate** (open the downloaded file with the image Read tool). Reject any that show a readable logo or brand mark (bitten apple, wordmarks), people's faces, or clutter.
3. Target at least 3 distinct photos per leaf category (≥ 33 product photos) plus 13 category photos.
4. `build-images.mjs` converts the downloaded sources (kept outside the repo, in a scratch folder) to the two WebP sizes using `npx sharp-cli`. It is re-runnable.

If Unsplash is unreachable, report BLOCKED. Do not substitute other sources or generated images.

- [ ] **Step 1:** Collect, review and record the candidates in `sources.json`.
- [ ] **Step 2:** Run `node scripts/images/build-images.mjs <scratch-dir>`. Expected: for every key, `<key>.webp` (long edge ≤ 1200) and `<key>-400.webp` exist; the total is ≤ 12 MB.
- [ ] **Step 3:** Generate `CREDITS.md` from `sources.json`: one table with key, photographer and link, plus a line about the Unsplash License.
- [ ] **Step 4: Verify.** `npx ng build` succeeds and the images appear in `dist/shop-web/browser/images/`. Run `docker run --rm -e SITE_ADDRESS=:80 -v "$PWD/Caddyfile.web:/etc/caddy/Caddyfile" caddy:2-alpine caddy validate --config /etc/caddy/Caddyfile` (from `web/shop-web`). Expected: valid.
- [ ] **Step 5: Commit** `feat(web): curated Unsplash product and category photos with credits`.

---

### Task 8: Seed data and seeder

**Files:**
- Create: `Catalog.Infrastructure/Seeding/{CatalogSeeder,SeedModel}.cs`, `Seeding/catalog-seed.json` (EmbeddedResource)
- Modify: `CatalogModule.Register` (`services.AddScoped<IModuleSeeder, CatalogSeeder>()`)
- Test: `tests/Shop.IntegrationTests/Catalog/CatalogSeedTests.cs`

**Interfaces:**
- Consumes: `IModuleSeeder` (Task 1); `Category.Create` and `Product.Create`/`Publish` (Tasks 2–3); the image keys from Task 7's `sources.json`.
- Produces: `CatalogSeeder.SeedAsync`. It is a no-op if any category exists; otherwise it inserts everything in one transaction. Products are created through the domain factories, so an invalid seed fails loudly.

**Seed content (decided):**
- **Category tree** (two levels):
  - Phones, Laptops, Tablets, Wearables, Cameras and Gaming are top-level leaves.
  - Audio has the children Headphones, Earbuds and Speakers.
  - Accessories has the children Chargers & Power and Cables & Hubs.
  - That gives 11 leaf categories with spec schemas; parents have empty schemas. Icons are Material Symbols names.
- **Spec schemas:** each leaf has 4–7 specs, of which ≥ 3 are filterable, including ≥ 1 Number and ≥ 1 Enum or Boolean. Values must be realistic: laptops have screen size, RAM, CPU class, GPU, battery hours, OS and weight; phones have screen, refresh rate, battery, camera, 5G and water resistance; and so on.
- **Products:**
  - 75–90 products, 6–9 per leaf, under 8–12 fictional brands that are reused across categories.
  - Each has 1–3 option axes (e.g. phones: storage and color; headphones: color) and 1–8 variants, ~250 variants in total.
  - Prices are realistic USD ending in .99 (or .00 for premium items). Price spreads between variants are realistic.
  - Descriptions are 2–4 sentences of genuine product copy, with no lorem ipsum.
  - 8 are featured, and 2 are left as Draft (not published).
  - Each product uses 1–3 image keys from its category's photo pool.
- **Stock:** ≥ 10% of variants have stock 0, ≥ 10% have 1–5 (so the low-stock badge appears), and the rest have 6–120.

- [ ] **Step 1: Write the failing tests**
```csharp
[Fact] Seed_FreshDatabase_CreatesExpectedShape()   // 13 categories (11 leaves), 75–90 products, 8 featured, 2 draft, ≥10% zero-stock and ≥10% low-stock variants
[Fact] Seed_RunTwice_IsNoOp()                      // counts unchanged
[Fact] Seed_AllImageKeysExistInWebAssets()         // every referenced key has public/images/products/{key}.webp and -400.webp (path from repo root)
[Fact] Seed_AllCategoryImagesExist()
[Fact] Seed_NoRealBrandNames()                     // brand list ∩ a denylist (Apple, Samsung, Sony, Bose, Dell, Lenovo, Google, Xiaomi, HP, Asus, Acer, LG, Huawei, OnePlus, JBL, Logitech, Anker, Canon, Nikon, Fujifilm, GoPro, Garmin, Fitbit, Microsoft, Nintendo, Razer) is empty
[Fact] Seed_EveryLeafHasSearchableFacets()         // for each leaf, facets endpoint returns ≥3 spec facets with data
```
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Write the seed JSON and the seeder.
- [ ] **Step 4:** Re-run. Expected: PASS. Then run `cd deploy && docker compose up -d --build`, `curl -s "http://localhost:8080/api/catalog/products?category=laptops&pageSize=3"` and `curl -s "http://localhost:8080/api/catalog/categories"`. Expected: real data. Then `docker compose down -v`.
- [ ] **Step 5: Commit** `feat(catalog): realistic seed catalog with validated specs, variants and stock`.

---

### Task 9: Frontend foundation — typed API client, routing, shared pieces

**Files:**
- Modify: `web/shop-web/package.json` (dev dep `openapi-typescript`; script `"api:types": "openapi-typescript openapi/v1.json -o src/app/api/schema.ts"`)
- Create: `src/app/api/schema.ts` (generated), `src/app/api/catalog-api.service.ts`
- Create: `src/app/core/problem-interceptor.ts`, `src/app/core/money.pipe.ts`
- Create: `src/app/shared/stock-badge/{stock-label.ts,stock-badge.component.ts}`, `src/app/shared/product-card/*`, `src/app/shared/not-found/*`
- Modify: `app.routes.ts`, `app.config.ts`, `layout/header/*` (search enabled)

**Interfaces (produced):**
```ts
export type ProductSummary = components['schemas']['ProductSummaryDto'];   // likewise ProductDetail, CategoryNode, CategoryFacets, PagedResult*
export interface ProductQuery { category?: string; q?: string; brands: string[]; minPrice?: number; maxPrice?: number; inStock: boolean;
  featured?: boolean; specs: string[] /* raw "key:..." */; sort?: string; page: number; pageSize: number; }
@Injectable({ providedIn: 'root' }) export class CatalogApi {
  categories(): HttpResourceRef<CategoryNode[] | undefined>;
  facets(slug: Signal<string | undefined>): HttpResourceRef<CategoryFacets | undefined>;
  products(query: Signal<ProductQuery>): HttpResourceRef<PagedProducts | undefined>;
  product(slug: Signal<string>): HttpResourceRef<ProductDetail | undefined>; }
export function stockLabel(available: number): 'Out of stock' | `Only ${number} left` | 'In stock';   // 0 → Out; 1–5 → Only N left; >5 → In stock
@Pipe({ name: 'money' }) export class MoneyPipe   // USD, en-US
```
- **Routes:** `''` → Home; `c/:slug` → ProductListPage; `search` → ProductListPage; `p/:slug` → ProductDetailPage (Tasks 10–11 fill these in; use lazy `loadComponent` with placeholder pages here); `**` → NotFound.
- **Header search:** an enabled input plus submit. Navigates to `/search?q=<trimmed>`; whitespace-only input is ignored. It reflects the current `q` when on `/search`.
- **ProblemInterceptor:** shows a `MatSnackBar` with the ProblemDetails `title` for status ≥ 500 and status 0 (network errors). It does **not** toast 4xx, because pages handle 404 and 400 themselves.
- **Product card:** thumbnail (`loading="lazy"`, width/height set), brand, name, "From $X" when PriceFrom ≠ PriceTo, the stock badge, and a link to `/p/:slug`.

- [ ] **Step 1: Write the failing tests:**
  - `stockLabel` (0, 1, 5, 6)
  - `MoneyPipe` (1299 → `$1,299.00`)
  - header search navigates on submit and ignores blanks
  - ProblemInterceptor toasts on 500 and not on 404
  - product card shows "From" only for a price range
- [ ] **Step 2:** `CI=true npx ng test --no-watch`. Expected: FAIL.
- [ ] **Step 3:** `npm run api:types`, then implement.
- [ ] **Step 4:** `CI=true npx ng test --no-watch && npx ng lint && npx ng build`. Expected: all pass.
- [ ] **Step 5: Commit** `feat(web): typed catalog API client, routes, search box and shared product UI`.

---

### Task 10: Home and product listing pages

**Files:**
- Create: `src/app/pages/product-list/{product-list.page.ts,html,scss}`, `filter-panel/*`, `listing-query.ts`
- Modify: `src/app/pages/home/*`

**Interfaces:**
- Consumes: `CatalogApi`, `ProductCard`, `MoneyPipe` (Task 9)
- Produces:
```ts
export interface ListingState { category?: string; q?: string; brands: string[]; minPrice?: number; maxPrice?: number; inStock: boolean;
  specs: Record<string, { min?: number; max?: number; values?: string[] }>; sort?: string; page: number; }
export function fromRoute(slug: string | undefined, params: ParamMap): ListingState;
export function toQueryParams(state: ListingState): Params;      // omits defaults; arrays → repeated params; specs → "spec" entries
export function toProductQuery(state: ListingState, pageSize: number): ProductQuery;
```
**Home page:**
- Hero CTA "Shop all products" → `/search`.
- A category grid built from `categories()`: top-level categories with their photo, name and product count, linking to `/c/:slug`.
- A "Featured" strip of 8 cards (`featured=true&pageSize=8`).
- Skeleton placeholders while loading.

**Listing page:**
- **Title area:** the category name, or `Results for "q"`, plus the result count.
- **Filter panel** (from `facets(category)`): brand checkboxes with counts, min/max price inputs, an in-stock toggle, and one block per spec facet (enum → checkboxes with counts; number → min/max inputs showing the unit and range; boolean → toggle).
- **Above the grid:** active-filter chips (each removable) and "Clear all". A sort select: Featured / Price: low to high / Price: high to low / Newest, plus Relevance when `q` is set.
- **Grid:** 4 / 3 / 2 columns by breakpoint, with a `mat-paginator` (24 per page).
- **Responsive:** under 900px the filter panel moves into a `mat-sidenav` drawer opened by a "Filters" button.
- **States:** a skeleton grid while loading; an empty state ("No products match these filters", with a "Clear filters" button); 404 for an unknown category, rendered as the NotFound component.

**URL state:** state lives **only** in the URL. Every change calls `router.navigate([], { queryParams: toQueryParams(next), queryParamsHandling: '' })`, which resets `page` to 1 except on page changes. Back/forward and reload restore exactly the same view. Number inputs are debounced (400 ms).

- [ ] **Step 1: Write the failing tests:**
  - **`listing-query.ts`:** `round trip: toQueryParams(fromRoute(...)) preserves state`; `defaults omitted from URL`; `spec range and values serialize to "ram_gb:16..", "os:Linux,Windows 11"`; `filter change resets page to 1`.
  - **Components:** filter panel emits a brand toggle; the empty state renders when `totalCount` is 0; the home page renders category cards from a mocked response.
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run tests, lint and build: PASS. **Manual check** against the compose stack (`docker compose up -d --build`): on `http://localhost:8080`, filter laptops by RAM, refresh the page and use back. Fetch the page HTML/API with curl to confirm the API responses the page depends on. Record what you checked.
- [ ] **Step 5: Commit** `feat(web): home with live categories and featured products; filterable product listing`.

---

### Task 11: Product detail page

**Files:**
- Create: `src/app/pages/product-detail/{product-detail.page.ts,html,scss}`, `gallery/*`, `variant-picker/*`, `variant-logic.ts`

**Interfaces:**
```ts
export type Selection = Record<string, string>;
export function initialSelection(detail: ProductDetail): Selection;   // first variant with available > 0, else first variant
export function resolveVariant(variants: Variant[], selection: Selection): Variant | undefined;
export function isValueSelectable(variants: Variant[], selection: Selection, axisKey: string, value: string): boolean;   // a variant exists with this value and the other selected values
export function bestSelectionFor(variants: Variant[], selection: Selection, axisKey: string, value: string): Selection;  // switch value; if combo missing, pick the closest existing variant with that value
```
**Layout:**
- A breadcrumb (Home › Category › Product).
- A gallery: main image plus a thumbnail row. Clicking a thumbnail swaps the main image; arrow keys work when the gallery is focused; there's an `alt` text with the product name.
- Brand, name, and the price of the selected variant.
- A variant picker: one `mat-button-toggle-group` per axis. Values with no existing combination are disabled; values whose combination is out of stock stay selectable, and the badge says "Out of stock".
- The stock badge, and a disabled primary button labelled "Add to cart — coming soon".
- A description, then a spec table (label, value).
- **URL:** the selected variant is reflected in the URL as `?variant=<sku>`, so links can be shared.
- **States:** a skeleton while loading; the NotFound component on 404.

- [ ] **Step 1: Write the failing tests:**
  - **`variant-logic`:** `initialSelection skips out-of-stock first variant`; `resolveVariant exact match`; `isValueSelectable false when combination missing`; `bestSelectionFor falls back to existing combination`.
  - **Components:** the page shows the selected variant's price and stock label, the `?variant=` param preselects, and thumbnail clicks swap the main image.
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run tests, lint and build: PASS. Do a manual check against the compose stack, as in Task 10.
- [ ] **Step 5: Commit** `feat(web): product detail with gallery, variant picker and spec table`.

---

### Task 12: Ship it

**Files:**
- Modify: `README.md`:
  - feature list for the storefront, plus a "Catalog design" paragraph linking ADR-0006
  - Roadmap: tick Plan 2
  - Testing section: mention `Catalog.UnitTests` and the OpenAPI snapshot workflow (`UPDATE_OPENAPI_SNAPSHOT=1`, then `npm run api:types`)
- Modify: `.github/workflows/ci.yml` if needed (new test project included through `--solution`; verify)

- [ ] **Step 1:** Run the full local verification: `dotnet build VoltLane.slnx` (0 warnings), `dotnet test --solution VoltLane.slnx`, and `npm ci && npx ng lint && CI=true npx ng test --no-watch && npx ng build` in `web/shop-web`. Then a compose smoke test: categories, a filtered search and a product detail via curl. Expected: all pass.
- [ ] **Step 2:** Update the README, then commit `docs: storefront features, catalog ADR link, roadmap`.
- [ ] **Step 3:** Push the branch and open a PR to `main` with `gh` (body: summary, test evidence, the attribution line). Wait for CI to go green (`gh pr checks --watch`).
- [ ] **Step 4: STOP. The user merges the PR** (merging deploys to production).
- [ ] **Step 5: After the merge, verify production** at https://204.168.194.99.sslip.io:
  - `/api/catalog/categories` returns the seeded tree, so the seeder ran on the production database
  - `/api/catalog/products?category=laptops&spec=ram_gb:16..` returns results
  - a product page `/p/<slug>` HTML loads
  - `/images/products/<key>-400.webp` returns 200 with `Cache-Control: public, max-age=604800`
  - `/api/system/info` shows the merge SHA
