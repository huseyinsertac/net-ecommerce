# ADR-0005: Own dispatcher instead of MediatR

Status: Accepted. Date: 2026-11-13

## Context

Application layers use CQRS: commands and queries return `Result` and run through a pipeline (logging, validation). MediatR is the usual choice, but recent versions moved to a commercial license, which is a poor fit for an open portfolio project that others may fork. The needed feature set is small: route a request to one handler, wrap it in ordered behaviors, and resolve everything from DI.

| | Option | Trade-off |
|---|---|---|
| A | MediatR | Familiar and feature-rich; commercial licensing, and most of its surface (notifications, streaming, pre/post processors) goes unused |
| B | Other third-party mediator library | Avoids the licence question; still an external dependency with its own abstractions to learn and track |
| **C (chosen)** | Own dispatcher | Roughly 150 lines, fully understood and tailored to `Result`; we maintain it |

## Decision

Build `IDispatcher` in `Shop.Application.Abstractions`. It resolves `ICommandHandler`/`IQueryHandler` from `IServiceProvider` and wraps the call in `IPipelineBehavior` instances. Order is Logging (outermost), Validation, then the handler. Dispatch uses a cached per-request-type wrapper instead of `dynamic`. FluentValidation validators feed `ValidationBehavior`, which returns a `ValidationError` failure rather than throwing. `AddApplicationHandlers(assembly)` scans internal and public types so module handlers can stay internal.

## Consequences

- No licensing risk and no dependency on a mediator library's roadmap.
- A dispatcher this small is easy to read and to explain in an interview, including the pipeline order and why `dynamic` is avoided.
- Trade-off: we own the code and its bugs, and we do not get MediatR features (notifications, streaming) unless we build them. Domain and integration events use separate, purpose-built handlers and the outbox instead.
