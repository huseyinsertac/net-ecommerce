# Plan 1 — Foundation & Walking Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A deployed, empty-but-real VoltLane: the full solution structure with all five modules, the building blocks (result types, dispatcher, outbox/inbox), architecture tests, an Angular shell that shows the API version, Docker Compose, and CI/CD to a public HTTPS URL.

**Architecture:** One ASP.NET Core host (`Shop.Api`) composes five modules. Each module is split into `Domain / Application / Infrastructure / Contracts` projects, and each owns one PostgreSQL schema with its own `DbContext`, `outbox_messages` and `inbox_messages`. Cross-module communication goes through integration events: they are written to the outbox in the same transaction as the state change, then delivered in-process by a polling processor, with an inbox making delivery idempotent.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, EFCore.NamingConventions, FluentValidation, Serilog, Scalar, xUnit v3, Shouldly, Testcontainers, NetArchTest.Rules, Angular (latest stable) + Angular Material, Docker Compose, Caddy, GitHub Actions, GHCR.

**Spec:** `docs/superpowers/specs/2026-11-13-ecommerce-portfolio-design.md`

**This plan is 1 of 5 for Milestone 1:**
1. **Foundation & walking skeleton** (this plan)
2. Catalog: categories, products, variants, specs, search/filters, stock reservation, storefront browse and product page, seed data
3. Identity & Basket: JWT, throwaway demo customer, shared demo admin, cart UI
4. Ordering & Payments: checkout, the outbox choreography, Stripe test mode, checkout-status polling, order history
5. Admin, nightly reset, rate limiting, README/screenshots polish → Milestone 1 done

## Global Constraints

- Target framework `net10.0`; `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, all via `Directory.Build.props`.
- Package versions only in `Directory.Packages.props` (central package management).
- PostgreSQL 18 (`postgres:18-alpine`). One database `voltlane`. Schemas: `catalog`, `basket`, `ordering`, `payments`, `identity`. Snake_case table and column names. EF migrations history table lives in each module's schema.
- No MediatR, no message broker, no Redis, no Elasticsearch (spec §2 anti-goals).
- Project and namespace naming: `{Module}.{Layer}` (e.g. `Catalog.Domain`); building blocks `Shop.*`; host `Shop.Api`.
- Modules reference each other **only** through `{Module}.Contracts`.
- Store brand name: **VoltLane** (assistant's placeholder). Defined once: `App:Name` in `appsettings.json` and `web/shop-web/src/app/brand.ts`.
- Routes: API under `/api/*`; `/health` (liveness), `/health/ready` (checks the DB); API docs at `/scalar`, exposed in every environment.
- Angular: latest stable, standalone components, signals, Angular Material, no NgRx.
- Tests: xUnit v3 + Shouldly; integration tests use Testcontainers `postgres:18-alpine`.
- **Prerequisite:** Node.js 24 LTS is **not installed** on the dev machine; install it before Task 7. Docker 29 and .NET SDK 10.0.300 are present.

## Review Focus

1. **Two outbox processors poll at the same time** (overlapping polls, or two API instances): each message is delivered exactly once → test in Task 4.
2. **One of two handlers fails for the same event:** on retry, the handler that already succeeded is not run again → test in Task 4.
3. **An outbox row has an unknown type** (event class renamed or removed): it is parked with an error and doesn't block newer messages → test in Task 4.
4. **Unhandled exception in Production:** the client gets a generic 500 ProblemDetails with no stack trace or exception message → test in Task 5.
5. **Fresh empty database / API starting before Postgres is ready:** all five schemas get migrated on startup, and Compose waits on the Postgres healthcheck → test in Task 5, check in Task 8.

---

## File Structure

```
Directory.Build.props, Directory.Packages.props, .editorconfig, .gitignore, VoltLane.slnx
.config/dotnet-tools.json                      # dotnet-ef
src/BuildingBlocks/
  Shop.SharedKernel/            Entity.cs, AggregateRoot.cs, DomainEvent.cs, Error.cs, Result.cs, Money.cs, IntegrationEvent.cs
  Shop.Application.Abstractions/  Messaging/(ICommand.cs, IQuery.cs, Handlers.cs, IDispatcher.cs, Dispatcher.cs, IPipelineBehavior.cs)
                                Behaviors/(ValidationBehavior.cs, LoggingBehavior.cs), Events/(IDomainEventHandler.cs, IIntegrationEventHandler.cs, IEventPublisher.cs)
                                ValidationError.cs, DependencyInjection.cs
  Shop.Infrastructure.Common/   Persistence/(ModuleDbContext.cs, ModuleDbContextRegistration.cs, ModuleDatabaseRegistry.cs)
                                Outbox/(OutboxMessage.cs, OutboxOptions.cs, OutboxProcessor.cs, OutboxEventPublisher.cs)
                                Inbox/(InboxMessage.cs, InboxHandler.cs)
                                Events/(IEventBus.cs, InProcessEventBus.cs, IntegrationEventTypeRegistry.cs, DomainEventDispatcher.cs)
                                Modules/IModule.cs
src/Modules/{Catalog,Basket,Ordering,Payments,Identity}/
  {M}.Domain/AssemblyReference.cs
  {M}.Application/AssemblyReference.cs
  {M}.Contracts/AssemblyReference.cs
  {M}.Infrastructure/{M}Module.cs, Persistence/{M}DbContext.cs, Persistence/Migrations/
src/Bootstrapper/Shop.Api/      Program.cs, ModuleCatalog.cs, ErrorHandling/(GlobalExceptionHandler.cs, ErrorResults.cs),
                                SystemEndpoints.cs, appsettings*.json, Dockerfile
tests/
  Shop.SharedKernel.UnitTests/
  Shop.Application.Abstractions.UnitTests/
  Shop.Infrastructure.Common.IntegrationTests/   # TestModuleDbContext (schema "test"), fixtures
  Shop.ArchitectureTests/
  Shop.IntegrationTests/                         # ShopApiFactory (WebApplicationFactory + Testcontainers)
web/shop-web/                   # Angular app, Dockerfile, Caddyfile.web, proxy.conf.json
deploy/                         docker-compose.yml (local), docker-compose.prod.yml, Caddyfile, .env.example, SERVER_SETUP.md
.github/workflows/              ci.yml, deploy.yml
docs/adr/                       0001…0005
README.md
```

---

### Task 1: Repository and solution foundation

**Files:**
- Create: `.gitignore` (dotnet + node + `deploy/.env`), `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`, `VoltLane.slnx`, `.config/dotnet-tools.json`
- Create: all module projects from the File Structure (empty, each with `public static class AssemblyReference {}`), the three BuildingBlocks projects, `Shop.Api` (empty web project), and the test projects
- Create: `docs/adr/0001-modular-monolith.md`, `docs/adr/0002-layer-projects-per-module.md`

**Project references (the dependency rule, enforced by the compiler):**
- `{M}.Domain` → `Shop.SharedKernel`
- `{M}.Contracts` → `Shop.SharedKernel`
- `{M}.Application` → `{M}.Domain`, `{M}.Contracts`, `Shop.Application.Abstractions`
- `{M}.Infrastructure` → `{M}.Application`, `Shop.Infrastructure.Common`
- `Shop.Application.Abstractions` → `Shop.SharedKernel`
- `Shop.Infrastructure.Common` → `Shop.Application.Abstractions` (+ `FrameworkReference Microsoft.AspNetCore.App`)
- `Shop.Api` → every `{M}.Infrastructure`

- [ ] **Step 1:** `git init -b main`, add `.gitignore`, commit the spec and this plan: `git commit -m "docs: add design spec and plan 1"`.
- [ ] **Step 2:** Create the solution and the projects with the references above. Add `dotnet-ef` 10.x to the tool manifest.
- [ ] **Step 3:** Write ADR-0001 (modular monolith over microservices: one deployable, module boundaries enforced in code, the path to extracting a module later) and ADR-0002 (Approach A from spec §4, with its trade-off table). Each is ≤ 1 page: Context / Decision / Consequences.
- [ ] **Step 4: Verify.** Run `dotnet build VoltLane.slnx`. Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.
- [ ] **Step 5: Commit** `chore: scaffold modular monolith solution structure`.

---

### Task 2: Shared kernel

**Files:**
- Create: `src/BuildingBlocks/Shop.SharedKernel/{Entity,AggregateRoot,DomainEvent,Error,Result,Money,IntegrationEvent}.cs`
- Test: `tests/Shop.SharedKernel.UnitTests/{ResultTests,MoneyTests,AggregateRootTests}.cs`

**Interfaces (produced):**
```csharp
public abstract class Entity<TId> where TId : notnull { public TId Id { get; protected init; } }   // equality by type + Id
public interface IDomainEvent { Guid EventId { get; } DateTime OccurredOnUtc { get; } }
public abstract record DomainEvent : IDomainEvent;               // EventId = Guid.CreateVersion7(), OccurredOnUtc = UtcNow
public interface IHasDomainEvents { IReadOnlyList<IDomainEvent> DomainEvents { get; } void ClearDomainEvents(); }
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents { protected void Raise(IDomainEvent e); }
public enum ErrorType { Validation, NotFound, Conflict, Unauthorized, Forbidden, Failure }
public record Error(string Code, string Message, ErrorType Type)
{ static Error Validation(string code, string msg); NotFound(..); Conflict(..); Unauthorized(..); Forbidden(..); Failure(..); static readonly Error None; }
public class Result { bool IsSuccess; bool IsFailure; Error Error; static Result Success(); static Result Failure(Error e);
                      static Result<T> Success<T>(T v); static Result<T> Failure<T>(Error e); }
public class Result<T> : Result { T Value { get; } /* throws InvalidOperationException when failed */
                                  implicit operator Result<T>(T v); implicit operator Result<T>(Error e); }
public sealed record Money(decimal Amount, string Currency)
{ static Money Usd(decimal amount); static Money Zero; operator +(Money, Money); operator *(Money, int quantity); }
public interface IIntegrationEvent { Guid Id { get; } DateTime OccurredOnUtc { get; } }
public abstract record IntegrationEvent : IIntegrationEvent;    // Id = Guid.CreateVersion7(), OccurredOnUtc = UtcNow
```
Value objects are `sealed record`s, with no `ValueObject` base class. This is my call: records already give value equality. `Money` is single-currency (`"USD"`, since multi-currency is out of scope). It rounds to 2 decimals with `MidpointRounding.AwayFromZero` and throws `ArgumentOutOfRangeException` on a negative amount. Adding two different currencies throws `InvalidOperationException`.

- [ ] **Step 1: Write the failing tests**
```csharp
[Fact] void Success_HasNoError()          => Result.Success().Error.ShouldBe(Error.None);
[Fact] void Failure_ValueAccess_Throws()  => Should.Throw<InvalidOperationException>(() => Result.Failure<int>(Error.NotFound("x","y")).Value);
[Fact] void ImplicitFromError_IsFailure() { Result<int> r = Error.Conflict("c","m"); r.IsFailure.ShouldBeTrue(); r.Error.Type.ShouldBe(ErrorType.Conflict); }
[Fact] void Money_RoundsToTwoDecimals()   => Money.Usd(10.005m).Amount.ShouldBe(10.01m);
[Fact] void Money_Negative_Throws()       => Should.Throw<ArgumentOutOfRangeException>(() => Money.Usd(-0.01m));
[Fact] void Money_Multiply()              => (Money.Usd(19.99m) * 3).ShouldBe(Money.Usd(59.97m));
[Fact] void Money_EqualByValue()          => Money.Usd(5m).ShouldBe(Money.Usd(5.00m));
[Fact] void Aggregate_RaiseAndClear()     { /* test aggregate raises 2 events → DomainEvents.Count == 2; ClearDomainEvents → 0 */ }
[Fact] void Entities_SameIdSameType_AreEqual() { /* two TestEntity with Id 1 → Equals true; different type same Id → false */ }
```
- [ ] **Step 2:** `dotnet test tests/Shop.SharedKernel.UnitTests`. Expected: build FAILS (types not defined).
- [ ] **Step 3:** Implement the types listed in Interfaces.
- [ ] **Step 4:** `dotnet test tests/Shop.SharedKernel.UnitTests`. Expected: all PASS.
- [ ] **Step 5: Commit** `feat(shared-kernel): add entity, aggregate, result, money, events`.

---

### Task 3: Command/query dispatcher and pipeline

**Files:**
- Create: `src/BuildingBlocks/Shop.Application.Abstractions/**` (see File Structure)
- Create: `docs/adr/0005-own-dispatcher-instead-of-mediatr.md`
- Test: `tests/Shop.Application.Abstractions.UnitTests/{DispatcherTests,ValidationBehaviorTests}.cs`

**Interfaces:**
- Consumes: `Result`, `Result<T>`, `Error`, `IDomainEvent`, `IIntegrationEvent` (Task 2)
- Produces:
```csharp
public interface ICommand; public interface ICommand<TResponse>; public interface IQuery<TResponse>;
public interface ICommandHandler<in TCommand> where TCommand : ICommand { Task<Result> Handle(TCommand command, CancellationToken ct); }
public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse> { Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct); }
public interface IQueryHandler<in TQuery, TResponse> where TQuery : IQuery<TResponse> { Task<Result<TResponse>> Handle(TQuery query, CancellationToken ct); }
public interface IDispatcher {
  Task<Result> Send(ICommand command, CancellationToken ct = default);
  Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default);
  Task<Result<TResponse>> Query<TResponse>(IQuery<TResponse> query, CancellationToken ct = default); }
public delegate Task<TResult> HandlerDelegate<TResult>();
public interface IPipelineBehavior<in TRequest, TResult> where TResult : Result { Task<TResult> Handle(TRequest request, HandlerDelegate<TResult> next, CancellationToken ct); }
public sealed record ValidationError(IReadOnlyDictionary<string, string[]> Errors)
    : Error("validation.failed", "One or more validation errors occurred.", ErrorType.Validation);
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent { Task Handle(TEvent e, CancellationToken ct); }
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent { Task Handle(TEvent e, CancellationToken ct); }
public interface IEventPublisher { void Publish(IIntegrationEvent e); }   // enlists in the current module's outbox; saved on SaveChanges
public static class DependencyInjection {
  IServiceCollection AddDispatcher(this IServiceCollection s);                         // dispatcher + Logging + Validation behaviors
  IServiceCollection AddApplicationHandlers(this IServiceCollection s, Assembly a); }  // scans command/query/domain-event handlers + FluentValidation validators
```
`Dispatcher` resolves the handler from `IServiceProvider` and wraps it in the registered behaviors. The order is Logging (outermost), then Validation, then the handler. Generic dispatch goes through a cached per-request-type wrapper, not `dynamic`. `ValidationBehavior` needs to build a failed `TResult`, so add `internal static TResult ResultFactory.Failure<TResult>(Error e)` (it handles `Result` and `Result<T>`). `LoggingBehavior` logs the request type name, elapsed ms and, on failure, `Error.Code`.

- [ ] **Step 1: Write the failing tests**
```csharp
[Fact] async Task Send_RoutesCommandToItsHandler()           // PingCommand → handler returns Result.Success(42) → result.Value == 42
[Fact] async Task Query_RoutesQueryToItsHandler()
[Fact] async Task Send_NoHandlerRegistered_Throws()          // InvalidOperationException mentioning the command type name
[Fact] async Task Validation_InvalidCommand_ReturnsValidationError_HandlerNotCalled()
    // CreateThing(Name: "") with rule NotEmpty → IsFailure, Error is ValidationError, Errors["Name"] non-empty, handler call count 0
[Fact] async Task Validation_ValidCommand_ReachesHandler()
[Fact] async Task Behaviors_RunInOrder_LoggingThenValidationThenHandler()   // record order via a test behavior
```
- [ ] **Step 2:** `dotnet test tests/Shop.Application.Abstractions.UnitTests`. Expected: build FAILS.
- [ ] **Step 3:** Implement the Interfaces above.
- [ ] **Step 4:** Re-run. Expected: all PASS.
- [ ] **Step 5:** Write ADR-0005 (MediatR's commercial license; a dispatcher under 150 lines is easier to explain in an interview; trade-off: we maintain it).
- [ ] **Step 6: Commit** `feat(app-abstractions): add CQRS dispatcher with validation and logging pipeline`.

---

### Task 4: Module persistence, outbox and inbox

**Files:**
- Create: `src/BuildingBlocks/Shop.Infrastructure.Common/{Persistence,Outbox,Inbox,Events}/**`
- Create: `docs/adr/0004-outbox-inbox-choreography.md`
- Test: `tests/Shop.Infrastructure.Common.IntegrationTests/{PostgresFixture,TestModuleDbContext,OutboxProcessorTests,InboxHandlerTests,DomainEventDispatchTests}.cs`

**Interfaces:**
- Consumes: Task 2 events/aggregates; Task 3 `IDomainEventHandler<>`, `IIntegrationEventHandler<>`, `IEventPublisher`
- Produces:
```csharp
public abstract class ModuleDbContext(DbContextOptions options, IDomainEventDispatcher dispatcher) : DbContext(options)
{ public abstract string Schema { get; }
  DbSet<OutboxMessage> OutboxMessages; DbSet<InboxMessage> InboxMessages;
  // OnModelCreating: HasDefaultSchema(Schema), maps outbox_messages / inbox_messages
  // SaveChangesAsync override: dispatch domain events from tracked IHasDomainEvents (loop until none new, clearing each), then base.SaveChangesAsync
}
public sealed class OutboxMessage { Guid Id; string Type; string Content /*jsonb*/; DateTime OccurredOnUtc; DateTime? ProcessedOnUtc; int Attempts; string? Error; }
public sealed class InboxMessage  { Guid MessageId; string Consumer; DateTime ProcessedOnUtc; }   // PK (MessageId, Consumer)
public sealed class OutboxOptions { TimeSpan PollingInterval = 250 ms; int BatchSize = 20; int MaxAttempts = 5; }
public sealed class OutboxProcessor<TContext> : BackgroundService where TContext : ModuleDbContext
{ public Task<int> ProcessBatchAsync(CancellationToken ct); }   // returns # messages attempted
public interface IEventBus { Task PublishAsync(IIntegrationEvent e, CancellationToken ct); }
public sealed class IntegrationEventTypeRegistry { void Register(Assembly contracts); Type? Resolve(string typeName); }   // key = Type.FullName
public sealed class InboxHandler<TEvent, THandler, TContext> : IIntegrationEventHandler<TEvent> ...   // consumer = typeof(THandler).FullName
public static class ModuleDbContextRegistration {
  IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection s, IConfiguration c) where TContext : ModuleDbContext;
     // Npgsql(ConnectionStrings:Shop), UseSnakeCaseNamingConvention, MigrationsHistoryTable("__ef_migrations_history", Schema),
     // scoped IEventPublisher → OutboxEventPublisher<TContext>, hosted OutboxProcessor<TContext>, adds TContext to ModuleDatabaseRegistry
  IServiceCollection AddInboxHandler<TEvent, THandler, TContext>(this IServiceCollection s);
  IServiceCollection AddEventing(this IServiceCollection s, params Assembly[] contractsAssemblies); }  // bus, registry, domain-event dispatcher
public sealed class ModuleDatabaseRegistry { IReadOnlyList<Type> ContextTypes; }
```

**Processor algorithm** (the tests don't fully pin it down):
```
BEGIN;
SELECT * FROM {schema}.outbox_messages
 WHERE processed_on_utc IS NULL AND attempts < @maxAttempts
 ORDER BY occurred_on_utc LIMIT @batchSize FOR UPDATE SKIP LOCKED;
for each row:
  type = registry.Resolve(row.Type)
  if type is null → error = "Unknown event type '{Type}'", attempts = maxAttempts   (parked)
  else try { await bus.PublishAsync(deserialize(row.Content, type)); processed_on_utc = now }
       catch (ex) { attempts++, error = ex.Message }
COMMIT;
```
`InProcessEventBus.PublishAsync` creates a new DI scope and runs **every** `IIntegrationEventHandler<T>` for the event's runtime type. If any of them throw, it rethrows an `AggregateException` after all have run. `InboxHandler` begins a transaction on `TContext`. If a row with `(MessageId = e.Id, Consumer)` already exists, it returns. Otherwise it calls the inner handler, inserts the inbox row, saves and commits.

- [ ] **Step 1: Write the failing tests.** These use a Testcontainers Postgres fixture and a `TestModuleDbContext` (schema `test`) with a `TestAggregate` and `TestEvent : IntegrationEvent`. The schema is created with `EnsureCreatedAsync`.
```csharp
// OutboxProcessorTests
[Fact] ProcessBatch_PublishesPendingMessage_AndMarksProcessed()          // handler received event with same Id; ProcessedOnUtc != null
[Fact] ProcessBatch_WhenHandlerThrows_IncrementsAttemptsAndRecordsError() // Attempts == 1, Error contains handler message, ProcessedOnUtc null
[Fact] ProcessBatch_AfterMaxAttempts_StopsRetrying()                      // 6 batches, handler invoked exactly 5 times
[Fact] ProcessBatch_UnknownType_IsParkedWithoutBlockingOthers()           // [Review Focus 3] row Type="Gone.Event" + 1 valid row →
                                                                          // valid processed; unknown: Attempts == MaxAttempts, Error starts "Unknown event type"
[Fact] ConcurrentProcessors_DeliverEachMessageExactlyOnce()               // [Review Focus 1] 50 messages, 2 processors via Task.WhenAll in loops
                                                                          // until none pending → handler saw 50 distinct Ids, each once
// InboxHandlerTests
[Fact] SameMessageDeliveredTwice_InvokesInnerHandlerOnce()
[Fact] OneOfTwoHandlersFails_RetryDoesNotReinvokeSucceededHandler()       // [Review Focus 2] handler A ok, B throws once → after 2 batches:
                                                                          // A count 1, B count 2, message processed
// DomainEventDispatchTests
[Fact] SaveChanges_DomainEventHandlerPublishes_OutboxRowSavedWithAggregate()  // both rows exist after one SaveChangesAsync
[Fact] SaveChanges_DomainEventHandlerThrows_NothingIsSaved()                   // aggregate row count 0, outbox row count 0
```
- [ ] **Step 2:** `dotnet test tests/Shop.Infrastructure.Common.IntegrationTests`. Expected: build FAILS.
- [ ] **Step 3:** Implement the Interfaces using the processor algorithm. Serialize with `System.Text.Json`, default web options.
- [ ] **Step 4:** Re-run. Expected: all 9 PASS (Docker must be running).
- [ ] **Step 5:** Write ADR-0004. Cover why an outbox (a DB write and event publish that can't drift apart), why an inbox (at-least-once delivery means handlers must be idempotent), why choreography over an orchestrator, polling with `SKIP LOCKED`, and when you'd move to a broker.
- [ ] **Step 6: Commit** `feat(infra-common): add module DbContext, transactional outbox and idempotent inbox`.

---

### Task 5: Module wiring and API host

**Files:**
- Create: `src/BuildingBlocks/Shop.Infrastructure.Common/Modules/IModule.cs`
- Create: `src/Modules/{M}/{M}.Infrastructure/{M}Module.cs`, `Persistence/{M}DbContext.cs`, and an initial migration `InitialOutboxInbox` per module (`dotnet ef migrations add InitialOutboxInbox -p src/Modules/{M}/{M}.Infrastructure -s src/Bootstrapper/Shop.Api -c {M}DbContext -o Persistence/Migrations`)
- Create: `src/Bootstrapper/Shop.Api/{Program.cs, ModuleCatalog.cs, SystemEndpoints.cs, ErrorHandling/GlobalExceptionHandler.cs, ErrorHandling/ErrorResults.cs, appsettings.json, appsettings.Development.json}`
- Create: `docs/adr/0003-schema-per-module.md`
- Test: `tests/Shop.IntegrationTests/{ShopApiFactory.cs, HostTests.cs}`; `tests/Shop.Api.UnitTests/ErrorResultsTests.cs` (or put these in IntegrationTests; builder's choice)

**Interfaces:**
- Consumes: `AddModuleDbContext<T>`, `AddEventing`, `ModuleDatabaseRegistry` (Task 4); `AddDispatcher`, `AddApplicationHandlers`, `ValidationError` (Task 3)
- Produces:
```csharp
public interface IModule { string Name { get; }   // "catalog" | "basket" | "ordering" | "payments" | "identity" (= schema name)
  void Register(IServiceCollection services, IConfiguration configuration);
  void MapEndpoints(IEndpointRouteBuilder app); }   // module routes under /api/{Name}
public static class ModuleCatalog { public static IReadOnlyList<IModule> All { get; } }   // explicit list, no assembly scanning
public static class ErrorResults { public static IResult ToProblem(this Error error); }
   // Validation→400 ValidationProblem (ValidationError.Errors), NotFound→404, Conflict→409, Unauthorized→401, Forbidden→403, Failure→500
   // ProblemDetails.Type = error.Code, Title = error.Message
GET /api/system/info → 200 { "name": "VoltLane", "version": "<APP_VERSION env or assembly informational version>", "environment": "<env>" }
GET /health → 200 "Healthy";  GET /health/ready → checks Postgres (AspNetCore.HealthChecks.NpgSql)
```
`Program.cs` wires everything in one place: Serilog (console, compact JSON outside Development), `AddProblemDetails` + `GlobalExceptionHandler`, OpenAPI + Scalar at `/scalar`, `AddDispatcher`, `AddEventing(contracts assemblies of all modules)`, `module.Register` for each, health checks, then `MapEndpoints` for each. When `Database:MigrateOnStartup` is `true` (the default in appsettings), it migrates every type in `ModuleDatabaseRegistry` before `app.Run()`, retrying the connection up to 10 times with a 2 s delay. Add `public partial class Program;` for `WebApplicationFactory`.
`GlobalExceptionHandler` logs the exception and returns 500 ProblemDetails with Title `"An unexpected error occurred."`. In Development only, it adds `detail` = exception message.

- [ ] **Step 1: Write the failing tests** (`ShopApiFactory` starts a Postgres container, sets `ConnectionStrings:Shop`, environment `Production`):
```csharp
[Fact] Health_ReturnsHealthy()                 // GET /health/ready → 200, body "Healthy"
[Fact] SystemInfo_ReturnsNameAndVersion()      // name == "VoltLane", version non-empty
[Fact] FreshDatabase_AllModuleSchemasMigrated()  // [Review Focus 5] information_schema.tables contains outbox_messages AND inbox_messages
                                                 // for each of catalog, basket, ordering, payments, identity
[Fact] UnhandledException_InProduction_ReturnsGenericProblem()  // [Review Focus 4] test-only endpoint (registered via factory's
                                                 // ConfigureTestServices / IStartupFilter) throws "secret detail" → 500, content-type
                                                 // application/problem+json, title "An unexpected error occurred.", body does NOT contain "secret detail" or "   at "
[Fact] Scalar_IsServed()                        // GET /scalar → 200
// ErrorResultsTests
[Theory] ToProblem_MapsErrorTypeToStatus(ErrorType type, int status)   // the six mappings above
[Fact]   ToProblem_ValidationError_IncludesFieldErrors()               // errors["Name"] present in ValidationProblemDetails
```
- [ ] **Step 2:** `dotnet test tests/Shop.IntegrationTests`. Expected: FAIL.
- [ ] **Step 3:** Implement `IModule`, the five `{M}Module`/`{M}DbContext` (each `Schema` = module name, `Register` calls `AddModuleDbContext<{M}DbContext>` and `AddApplicationHandlers({M}.Application.AssemblyReference assembly)`, `MapEndpoints` maps an empty group `/api/{Name}` for now), add the five migrations, then `Shop.Api`.
- [ ] **Step 4:** Re-run. Expected: all PASS. Also run `dotnet run --project src/Bootstrapper/Shop.Api` against the local Compose Postgres (`docker compose -f deploy/docker-compose.yml up -d postgres` once Task 8 exists; until then a `docker run postgres:18-alpine` on 5432). Then `curl http://localhost:5080/api/system/info`. Expected: JSON with `"name":"VoltLane"`. Dev URL `http://localhost:5080` is set in `launchSettings.json`.
- [ ] **Step 5:** Write ADR-0003: one database and one schema per module; no cross-schema FKs or joins; data owned by its module; extraction path.
- [ ] **Step 6: Commit** `feat(api): compose five modules with per-schema DbContexts, health, ProblemDetails, Scalar`.

---

### Task 6: Architecture tests

**Files:**
- Test: `tests/Shop.ArchitectureTests/{Modules.cs, LayerRulesTests.cs, ModuleBoundaryTests.cs}`

**Interfaces:**
- Consumes: every `AssemblyReference` and `{M}Module` type (Tasks 1, 5)
- Produces: `Modules.All` — for each module: `(string Name, Assembly Domain, Assembly Application, Assembly Infrastructure, Assembly Contracts)`

- [ ] **Step 1: Write the tests** (NetArchTest.Rules, `[Theory]` over `Modules.All`):
```csharp
Domain_DoesNotDependOn_Application_Infrastructure_EfCore_AspNetCore(module)
Application_DoesNotDependOn_Infrastructure_EfCore_AspNetCore(module)
Contracts_DependOnlyOnSharedKernel(module)          // no {M}.Domain/Application/Infrastructure, no EF/AspNetCore
Module_DoesNotReferenceOtherModulesInternals(module) // for every other N: no dependency on N.Domain, N.Application, N.Infrastructure
                                                     // namespaces (N.Contracts allowed)
DomainEventHandlers_AndCommandHandlers_AreNotPublic(module)   // handlers in {M}.Application are internal / sealed
```
Failure messages must list the offending types (`result.FailingTypeNames`).
- [ ] **Step 2: Prove the tests bite.** Temporarily add `Basket.Application` → `Catalog.Domain` project reference with a type using a Catalog.Domain type. Run `dotnet test tests/Shop.ArchitectureTests`. Expected: `Module_DoesNotReferenceOtherModulesInternals(basket)` FAILS and names the type. Revert.
- [ ] **Step 3:** `dotnet test tests/Shop.ArchitectureTests`. Expected: all PASS. (Domain projects currently hold only `AssemblyReference`; the tests need at least one type per assembly, which that provides.)
- [ ] **Step 4: Commit** `test(arch): enforce layer and module boundary rules`.

---

### Task 7: Angular shell

**Prerequisite:** install Node.js 24 LTS; verify `node --version` → `v24.x`.

**Files:**
- Create: `web/shop-web/` via `npx @angular/cli@latest new shop-web --directory web/shop-web --style=scss --routing --ssr=false --skip-git`
- `ng add @angular/material` (custom M3 theme in `src/styles.scss`: neutral surfaces, one electric-blue primary); `ng add @angular-eslint/schematics`
- Create: `src/app/brand.ts`, `src/app/core/system-info.service.ts`, `src/app/layout/{header,footer}/*`, `src/app/pages/home/*`, `proxy.conf.json` (`/api`, `/health` → `http://localhost:5080`), wire proxy in `angular.json` serve options
- Create: `web/shop-web/Dockerfile`, `web/shop-web/Caddyfile.web`
- Test: `src/app/layout/footer/footer.component.spec.ts`, `src/app/layout/header/header.component.spec.ts`

**Interfaces:**
- Consumes: `GET /api/system/info` (Task 5)
- Produces:
```ts
export const BRAND = { name: 'VoltLane', tagline: 'Tech that keeps up with you.' } as const;
export interface SystemInfo { name: string; version: string; environment: string; }
@Injectable({ providedIn: 'root' }) export class SystemInfoService { readonly info: Signal<SystemInfo | undefined>; }   // via httpResource or HttpClient + toSignal
```
Shell layout: a header with the brand wordmark, search field (disabled placeholder), "Shop" nav link and cart icon button (badge 0); a home page with a hero (tagline + "Browse products" button, routing to `/` for now) and a "Featured categories" placeholder grid of 6 cards; a footer with "© {year} VoltLane · portfolio project" and `API {version}`. Make it responsive (header collapses to a menu button under 600px).

- [ ] **Step 1: Write the failing tests**
```ts
it('footer shows API version from /api/system/info')   // HttpTestingController flushes {version:'1.2.3'} → text contains 'API 1.2.3'
it('footer shows "API offline" when the request fails')
it('header shows brand name from BRAND')
```
- [ ] **Step 2:** `npm test -- --watch=false` (in `web/shop-web`). Expected: FAIL.
- [ ] **Step 3:** Implement the shell.
- [ ] **Step 4:** `npm test -- --watch=false`, `npx ng lint`, `npx ng build`. Expected: all pass, build output in `dist/shop-web/browser`.
- [ ] **Step 5: Manual check.** Run the API (Task 5) and `npx ng serve`, open `http://localhost:4200`. Expected: the footer shows `API <version>`.
- [ ] **Step 6:** `Dockerfile`: `node:24-alpine` build stage, then a `caddy:2-alpine` serving `/srv` with `Caddyfile.web` (`:80 { root * /srv; try_files {path} /index.html; file_server; encode zstd gzip }`).
- [ ] **Step 7: Commit** `feat(web): add Angular shell with Material theme and API version footer`.

---

### Task 8: Containers and local Compose

**Files:**
- Create: `src/Bootstrapper/Shop.Api/Dockerfile` (multi-stage `mcr.microsoft.com/dotnet/sdk:10.0` → `aspnet:10.0`, non-root `app` user, `ASPNETCORE_HTTP_PORTS=8080`, `ARG APP_VERSION` → `ENV APP_VERSION`), `.dockerignore` at the repo root
- Create: `deploy/docker-compose.yml`, `deploy/docker-compose.prod.yml`, `deploy/Caddyfile`, `deploy/.env.example`

**Compose topology** (the same shape locally and in prod):
- `postgres` (`postgres:18-alpine`, volume `pgdata`, healthcheck `pg_isready -U $POSTGRES_USER -d voltlane`, interval 5s, retries 10)
- `api` (`depends_on: postgres: condition: service_healthy`; env `ConnectionStrings__Shop`, `ASPNETCORE_ENVIRONMENT`)
- `web`
- `caddy` (`caddy:2-alpine`, ports 80/443 in prod, `8080:80` locally; volumes `caddy_data`, `caddy_config`)

`deploy/Caddyfile`:
```
{$SITE_ADDRESS} {
  encode zstd gzip
  @api path /api/* /health* /scalar* /openapi/*
  reverse_proxy @api api:8080
  reverse_proxy web:80
}
```
Local `.env`: `SITE_ADDRESS=:80`. The local file uses `build:`; prod uses `image: ghcr.io/${GHCR_OWNER}/voltlane-{api,web}:${IMAGE_TAG:-latest}`.

- [ ] **Step 1:** Write the Dockerfiles, Compose files, Caddyfile and `.env.example`. The example contains `POSTGRES_USER`, `POSTGRES_PASSWORD`, `SITE_ADDRESS`, `GHCR_OWNER`, `IMAGE_TAG`.
- [ ] **Step 2: Verify** ([Review Focus 5]): from `deploy/`, run `cp .env.example .env && docker compose up -d --build`. Then:
  - `docker compose ps`: `api` started only after `postgres` reports `healthy`
  - `curl -s http://localhost:8080/health/ready` → `Healthy`
  - `curl -s http://localhost:8080/api/system/info` → JSON with `"name":"VoltLane"`
  - `http://localhost:8080` in a browser: the shell renders and the footer shows the API version
  - `docker compose down -v && docker compose up -d`: on a fresh volume, still healthy (migrations ran)
- [ ] **Step 3: Commit** `build: add Dockerfiles, Compose topology and Caddy reverse proxy`.

---

### Task 9: CI/CD and first public deploy

**Files:**
- Create: `.github/workflows/ci.yml`, `.github/workflows/deploy.yml`, `deploy/SERVER_SETUP.md`, `README.md`

**`ci.yml`:** runs on `pull_request` and on `push` to `main`.
- Job `backend` (ubuntu-latest): `actions/checkout@v4`, `actions/setup-dotnet@v4` (`10.0.x`), `dotnet restore`, `dotnet build --no-restore -c Release`, `dotnet test --no-build -c Release`. Testcontainers works on the hosted runner's Docker.
- Job `frontend`: `actions/setup-node@v4` (node 24, npm cache), then `npm ci`, `npx ng lint`, `npm test -- --watch=false`, `npx ng build`. Working directory `web/shop-web`.

**`deploy.yml`:** runs on `workflow_run` of CI `completed` on `main` with `conclusion == 'success'`, and on `workflow_dispatch`. `permissions: packages: write, contents: read`.
- Steps: log in to GHCR (`docker/login-action@v3`). Build and push `voltlane-api` and `voltlane-web` (`docker/build-push-action@v6`), tagged `${{ github.sha }}` and `latest`, with `APP_VERSION=${{ github.sha }}` for the api. Copy `deploy/docker-compose.prod.yml` and `deploy/Caddyfile` to `/opt/voltlane` (`appleboy/scp-action@v0.1.7`). Then `appleboy/ssh-action@v1.2.0` runs: `cd /opt/voltlane && IMAGE_TAG=${{ github.sha }} docker compose -f docker-compose.prod.yml pull && IMAGE_TAG=${{ github.sha }} docker compose -f docker-compose.prod.yml up -d && docker image prune -f`.
- Secrets used: `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_SSH_KEY`.
- A final step curls `https://$SITE/health/ready` (`vars.SITE_URL`), retrying for 60 s, and fails the job unless it gets `Healthy`.

**Steps only the user can do** (written into `deploy/SERVER_SETUP.md`; the executor stops and hands these over):
1. Create the GitHub repository and push `main`.
2. Create a Hetzner Cloud server (CX22, Ubuntu 24.04) with your SSH key.
3. Domain: point an A record at the server IP. With no domain, use `<ip-with-dashes>.sslip.io`, which works with Let's Encrypt.
4. On the server: install Docker Engine + Compose plugin; create a `deploy` user in the `docker` group; `mkdir /opt/voltlane`; create `/opt/voltlane/.env` from `.env.example` with a strong `POSTGRES_PASSWORD` and `SITE_ADDRESS=<your domain>`; open ports 22/80/443 (ufw).
5. Generate a deploy SSH key pair and put the public key in `~deploy/.ssh/authorized_keys`. Add GitHub secrets `DEPLOY_HOST`, `DEPLOY_USER=deploy`, `DEPLOY_SSH_KEY` (private key), and variable `SITE_URL`.
6. After the first image push, make both GHCR packages **public** (or `docker login ghcr.io` on the server with a read-only PAT).

- [ ] **Step 1:** Write `ci.yml`. Push a branch and open a PR. Expected: both jobs green.
- [ ] **Step 2:** Write `SERVER_SETUP.md` and `deploy.yml`. **Hand the user-only steps to the user** and wait for confirmation that they're done.
- [ ] **Step 3:** Merge to `main`. Expected: CI green → Deploy green, with its health check passing.
- [ ] **Step 4: Verify publicly.** Open `https://<domain>`: valid TLS, the shell renders, and the footer shows `API <commit sha>`. `https://<domain>/scalar` loads.
- [ ] **Step 5:** Write the README skeleton:
  - one-line pitch
  - **Live demo** link
  - CI badge
  - tech stack
  - a Mermaid diagram of the 5 modules, the host and Postgres schemas, with arrows for "Contracts only"
  - "Run locally" (`cd deploy && cp .env.example .env && docker compose up -d --build` → `http://localhost:8080`)
  - links to the ADRs
  - "Roadmap" listing Plans 2–5

  Demo credentials and screenshots are marked "coming in Plan 3/5".
- [ ] **Step 6: Commit** `ci: add CI, GHCR image publishing and SSH deploy; add README`.

---

## Definition of done for Plan 1

- `dotnet test` and the Angular tests are green locally and in CI.
- `https://<domain>` serves the VoltLane shell over HTTPS, the footer shows the deployed commit, and `/scalar` and `/health/ready` work.
- Every merge to `main` redeploys automatically.
- ADRs 0001–0005 and the README skeleton are committed.
