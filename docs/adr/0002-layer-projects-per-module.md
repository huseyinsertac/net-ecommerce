# ADR-0002: Layer projects per module (Approach A)

Status: Accepted. Date: 2026-11-13

## Context

Within each module we need a clean-architecture structure that reviewers recognise and that stays honest over time. Three options were considered:

| | Approach | Trade-off |
|---|---|---|
| **A (chosen)** | Each module split into `Domain`, `Application`, `Infrastructure`, `Contracts` projects | Compiler enforces the dependency rule; recognisable to reviewers; ~20 projects and some wiring boilerplate |
| B | One project per module, layers as folders | Fewer projects, faster; layering only by convention and architecture tests, which some reviewers read as nominal |
| C | Hybrid: full layers only in logic-heavy modules | Shows pragmatism; inconsistency needs explaining and can read as unfinished |

## Decision

Use Approach A. Every module has four projects named `{Module}.Domain`, `{Module}.Application`, `{Module}.Infrastructure` and `{Module}.Contracts`, with these references:

- Domain references `Shop.SharedKernel` only.
- Contracts references `Shop.SharedKernel` only.
- Application references its own Domain and Contracts, plus `Shop.Application.Abstractions`.
- Infrastructure references its own Application plus `Shop.Infrastructure.Common`.
- `Shop.Api` references every module's Infrastructure and composes them.

Architecture tests are added on top to enforce module-to-module boundaries, which project references alone cannot express.

## Consequences

- The dependency rule is verified by the compiler: Domain code cannot accidentally use EF Core or ASP.NET types.
- Visible, verifiable structure is worth more than leanness for a portfolio; the boilerplate is a one-time cost.
- More projects mean slower solution load and more csproj files, mitigated by central package management and shared `Directory.Build.props`.
