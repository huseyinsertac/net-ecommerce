# ADR-0004: Transactional outbox, idempotent inbox and choreography

Status: Accepted. Date: 2026-11-13

## Context

Checkout spans modules: Ordering places an order, Catalog reserves stock, Payments charges, Ordering marks the order paid. Each module owns its own schema and `DbContext` (ADR-0001), so there is no single transaction across them. Two failure modes must be impossible:

- **Drift between state and events.** If a module commits its change and then publishes an event in a separate step, a crash in between loses the event (the order exists but stock is never reserved). Publishing first and committing second is worse: consumers act on a change that may roll back.
- **Duplicate effects.** Any delivery mechanism that survives crashes must retry, and retrying means a consumer can see the same event more than once (at-least-once delivery). A handler that reserves stock twice or charges twice is a real bug.

We also had to choose how the multi-step flow is coordinated: a central orchestrator (a saga that tells each module what to do next) or choreography (each module reacts to events and emits its own).

## Decision

**Transactional outbox.** Application code never publishes directly. `IEventPublisher.Publish` adds an `outbox_messages` row to the module's own `DbContext`, so the event is written by the same `SaveChangesAsync`, in the same transaction, as the state change. Domain events are dispatched inside `SaveChangesAsync` before the write, in the same scope, so a domain-event handler that publishes an integration event lands its outbox row in that same transaction. If any handler throws, nothing is saved.

**Polling processor with `SKIP LOCKED`.** One `OutboxProcessor<TContext>` per module polls (250 ms, batches of 20). It opens a transaction, claims pending rows with `SELECT ... FOR UPDATE SKIP LOCKED` ordered by `occurred_on_utc`, delivers each through the in-process `IEventBus`, records `processed_on_utc` or increments `attempts` with the error, and commits. Row locks mean any number of processors (threads, or several app instances) can poll the same table without delivering a row twice at the same time and without blocking each other. After 5 failed attempts, or when the stored type name no longer resolves to a CLR type, the row is parked (it stays in the table with its error and is never retried automatically) so one poison message cannot block the rest.

**Idempotent inbox.** Every integration-event handler is registered wrapped in `InboxHandler<TEvent, THandler, TContext>`. It begins a transaction on the consuming module's `DbContext`, returns early if an `inbox_messages` row with `(message_id, consumer)` exists, otherwise runs the handler, inserts the inbox row and commits. The handler's writes, any outbox rows it publishes, and the inbox row are atomic. The consumer is the handler's type name, so each handler of an event is deduplicated independently: if one of two handlers fails, the retry re-runs only the failed one. A concurrent duplicate loses on the primary key and rolls back. `AddApplicationHandlers` does not register integration-event handlers, so a handler cannot also be invoked without its inbox.

**Choreography over an orchestrator.** Modules react to each other's public integration events (`{Module}.Contracts`). No module holds the whole checkout flow. With five modules and one main flow, an orchestrator would be one more stateful component that knows every module's internals, and it would become the coupling point ADR-0001 is trying to avoid. Compensation (for example, releasing stock when payment fails) is expressed as events too.

## Consequences

- An event is published if and only if its state change commits. Delivery is at-least-once, and the inbox makes the effect exactly-once per handler.
- Delivery is asynchronous. Checkout is eventually consistent, typically within one polling interval per hop. The UI must show intermediate states ("payment pending") rather than assume a synchronous result.
- Polling adds a small constant database load and up to about 250 ms of latency per hop. That is acceptable at this scale. Event ordering is best-effort (by `occurred_on_utc`), not guaranteed across processors or retries, so handlers must tolerate out-of-order events (check the current state, do not assume a sequence).
- Parked messages need operational attention: they are visible as rows with `processed_on_utc IS NULL AND attempts >= 5` and can be replayed by resetting `attempts`.
- Choreography makes the end-to-end flow harder to see in one place. Tracing and logging on event IDs, plus tests that run the whole checkout, make up for that. If flows grow long or need timeouts and human steps, introduce an orchestrated saga for that flow only.
- **When to move to a broker:** when a module is extracted into its own process, when several independent consumers outside this app need the events, or when polling latency or load becomes a measurable problem. The outbox stays (it is still how the write and the publish stay consistent); only `IEventBus` changes, from the in-process bus to a relay that publishes outbox rows to RabbitMQ or Kafka, while the inbox keeps deduplicating on the consumer side.
