# CLAUDE.md

This file defines how Claude Code should work inside the Contractra repository.

## Project Overview

Contractra is an internal financial and operations management system for an agricultural contracting business.

The system is intended to replace fragmented Excel-based workflows used by engineers, administration, and accounting.

The backend is currently in an early implementation stage.

Technology:

* .NET 10
* ASP.NET Core Web API
* Entity Framework Core
* PostgreSQL
* Clean Architecture

Do not infer implementation maturity from the documentation alone. Always inspect the current code before making claims about what is implemented.

---

## Business Context

Contractra manages operational and financial workflows related to agricultural contracting projects.

Core concepts include:

* Operations
* Sites
* Engineers
* Engineer site assignments
* Petty cash / engineer advances
* Expenses
* Workers and daily wages
* Suppliers
* Financial transactions
* Assignment orders
* Payment certificates
* Company participation / subcontracting arrangements
* Monthly financial reporting
* Audit and operational logging

An Operation is a primary business concept.

Sites belong within the operational context.

Engineers may work on multiple sites, but engineers should only access sites assigned to them.

Financial data requires strong traceability and auditability.

---

## Solution Structure

### Contractra.Domain

Innermost layer.

Contains:

* Domain entities
* Value objects
* Enums
* Domain rules
* Domain behavior

Rules:

* Must not depend on Infrastructure.
* Must not depend on API.
* Avoid EF Core-specific behavior in domain models.
* Important business invariants should be protected here when appropriate.

### Contractra.Application

References Domain.

Contains:

* Application use cases
* Commands and queries
* DTOs
* Interfaces
* Validation
* Application-level orchestration

Application logic may coordinate domain behavior but must not depend directly on Infrastructure implementations.

### Contractra.Infrastructure

References Domain and Application.

Contains technical implementation details such as:

* EF Core
* PostgreSQL
* DbContext
* Entity configurations
* Repositories
* Persistence implementations
* External service implementations
* File/storage implementations
* Logging infrastructure

Do not place business rules in Infrastructure.

### Contractra.Api

Composition root and HTTP API.

Contains:

* API endpoints/controllers
* Dependency injection composition
* Authentication/authorization integration
* HTTP-specific concerns

Controllers/endpoints should remain thin.

Do not place core business logic in API endpoints.

---

## Architecture Direction

Dependencies must point inward.

Expected direction:

Api -> Application -> Domain

Infrastructure implements interfaces required by Application and is wired through the API composition root.

Domain must remain independent from outer layers.

Before introducing a new abstraction, verify that it solves an actual requirement.

Do not over-engineer the system.

Prefer clear and maintainable designs over unnecessary patterns.

---

## Source of Truth

The authoritative Contractra documentation lives in this repository under `docs/`.

The primary entry point is:

`docs/documentation-index-ar.md`

Read it first when documentation guidance is needed. It defines which documents exist, their purpose, and the precedence order to use when documents disagree.

Before implementing any business feature:

1. Inspect the existing code.
2. Open `docs/documentation-index-ar.md` and identify only the documents/sections relevant to the feature's domain — do not load the entire documentation set unnecessarily.
3. Identify the confirmed business rules related to the feature.
4. Compare documentation with the current implementation.

### Document authority and precedence

Follow the authority hierarchy defined in `docs/documentation-index-ar.md` exactly:

* Markdown source documents are authoritative. Precedence between them, when they conflict, is the order given in that index — never inferred from file modification dates.
* Generated HTML presentation files (the `*.html` counterparts under `docs/`) are presentation only. They must never be treated as an independent source of business rules — if an HTML file appears to say something its source Markdown does not, the Markdown governs and the HTML is out of date.
* Superseded documents must never override current documents, even if they contain relevant-looking detail. Treat a document as superseded only as recorded in `docs/documentation-index-ar.md`, not by guessing from filenames or dates.
* When a derived/output document conflicts with a source document it was derived from, the source document governs; the conflict is a defect in the derived document, not a change to the rule.

Documentation containing confirmed business rules should be treated as authoritative unless a newer confirmed decision supersedes it, per that same index.

Never invent missing business rules.

If:

* documentation is ambiguous,
* two documents conflict,
* documentation conflicts with implementation,
* or an important financial rule is missing,

report the issue before changing system behavior.

Everything under `docs/` **except** `docs/implementation/**` and `docs/decisions/**` is authoritative source material and is read-only: do not modify it.

`docs/implementation/**` (plans, sprints, API contracts) and `docs/decisions/**` (ADRs) are working documents. Claude Code may edit them when a task asks for it.

---

## Important Business Rules

These rules are particularly important.

### Operations and Sites

* Operation is a main business entity.
* Sites exist within an operational context.
* Engineer access to sites must be controlled through assignments.
* Engineers should only see/select sites assigned to them.

### Assignment Orders

Each relevant operation must have an Assignment Order.

An Assignment Order must not be silently omitted when it is required by the confirmed business rules.

When a site is selected in workflows where the Assignment Order is required, the system should derive or expose the relevant Assignment Order according to the documented model.

### Engineer Expense Entry

Engineer-facing expense entry should remain simple.

Do not add unnecessary fields to engineer workflows without a confirmed requirement.

Invoice/document images may be optional where defined by business rules.

### Petty Cash

Petty cash is associated with engineers according to the confirmed financial model.

Financial transactions involving petty cash must remain traceable.

Do not calculate or modify financial balances using assumptions that are not explicitly supported by the domain rules.

### Workers and Daily Wages

Daily worker wages are important operational expenses and must be captured according to the documented workflow.

### Company Participation

Some operations may belong fully to internally owned companies.

Other operations may involve external/subcontracting arrangements where revenue/profit participation rules are agreed before work begins.

Do not hard-code a percentage globally unless the business rules explicitly define it as universal.

Participation terms should be modeled as business data when appropriate.

### Monthly Reports

Monthly financial reporting is an important system requirement.

Reports may include information such as:

* Expenses by category
* Worker daily wages
* Engineer-related financial information
* Salaries where authorized
* Site totals
* Operation totals
* Overall monthly spending

Sensitive payroll information must respect authorization rules.

Generated financial reports should be traceable and protected against silent modification according to the documented reporting rules.

### Auditability

Financial operations require strong auditability.

Do not rely only on application logs for financial audit history.

Important financial actions should be traceable through appropriate business/audit records according to the approved system design.

---

## Financial System Safety

Contractra handles financial information.

For financial features:

1. Identify the relevant business rules.
2. Identify financial invariants.
3. Inspect related entities and workflows.
4. Explain the proposed design before making major architectural changes.
5. Implement the smallest coherent change.
6. Add appropriate validation.
7. Add tests for important financial rules.
8. Verify that auditability is preserved.

Do not silently:

* delete financial records,
* rewrite financial history,
* recalculate historical values using new assumptions,
* modify posted/approved financial records,
* or bypass audit requirements.

Any destructive financial behavior requires explicit approval.

---

## Database

Database:

* PostgreSQL

ORM:

* Entity Framework Core

Current DbContext:

`Contractra.Infrastructure/Persistence/ContractraDbContext.cs`

Connection string key:

`ConnectionStrings:DefaultConnection`

Local secrets should be stored outside source control using appropriate secret management such as .NET User Secrets.

Never commit:

* database passwords
* API keys
* authentication tokens
* secrets
* private credentials

### Database Design Rules

Before creating a new table/entity:

1. Verify the business requirement.
2. Check whether an existing concept already represents it.
3. Determine relationships and ownership.
4. Determine required constraints.
5. Consider audit requirements.
6. Consider financial integrity.
7. Consider authorization implications.

Do not create duplicate business concepts with different names.

Do not introduce polymorphic financial references without checking the approved financial model.

Do not create destructive migrations without explicit approval.

Do not run database updates against production-like environments unless explicitly instructed.

---

## EF Core Commands

From the repository root:

```bash
dotnet ef migrations add <MigrationName> --project Contractra.Infrastructure --startup-project Contractra.Api
```

```bash
dotnet ef database update --project Contractra.Infrastructure --startup-project Contractra.Api
```

Creating a migration and applying a migration are separate actions.

Do not automatically apply migrations just because a migration was created.

---

## Development Workflow

For every non-trivial feature:

### 1. Investigate

Read:

* relevant code
* relevant documentation
* related entities
* related persistence configuration
* existing tests

Do not guess about code that can be inspected.

### 2. Plan

Before implementation, explain:

* what currently exists
* relevant business rules
* proposed design
* files to create
* files to modify
* database impact
* tests required
* risks or unresolved questions

Do not modify files when explicitly asked for planning only.

### 3. Implement

After the plan is approved:

* implement only the agreed scope
* avoid unrelated refactoring
* follow existing conventions
* preserve architecture boundaries

### 4. Validate

Run:

```bash
dotnet build
```

Run relevant tests when they exist.

If no tests exist for important new business logic, consider adding them as part of the feature.

### 5. Review

Inspect the resulting diff.

Report:

* files created
* files modified
* design decisions
* business rules implemented
* build result
* test result
* database/migration changes
* deviations from the approved plan
* remaining risks

---

## Git Rules

Claude must not automatically:

* push
* force push
* reset --hard
* rewrite Git history
* delete branches
* delete tags

Do not commit changes unless explicitly requested.

Before a commit, the implementation should be reviewed.

Keep commits focused on one coherent change whenever practical.

---

## Scope Control

Do not turn a small task into a repository-wide refactor.

Do not:

* introduce unnecessary frameworks
* create abstractions for hypothetical future requirements
* rename unrelated code
* change architecture without a clear reason
* implement undocumented features

Prefer the smallest implementation that correctly satisfies the confirmed requirement.

---

## Verification Principle

Never guess about something that can be verified from:

* code
* configuration
* Git history
* documentation
* database configuration
* tests

Inspect first.

If uncertainty remains, explicitly state what is uncertain.

---

## Current Repository State

At the time this guidance was established:

* The solution targets .NET 10.
* Clean Architecture projects exist.
* PostgreSQL persistence has been configured.
* EF Core is configured in Infrastructure.
* `ContractraDbContext` exists.
* The domain model covers Parties and Identity (users, roles, permissions, user role/permission grants, user–party links); there is no Application or API logic yet.
* EF Core migrations exist under `Contractra.Infrastructure/Migrations`, including the Slice 1 authorization catalog seed (`SeedAuthorizationCatalog`).
* Tests exist: `Contractra.Domain.Tests` (unit) and `Contractra.Infrastructure.IntegrationTests` (PostgreSQL, ephemeral databases via `CONTRACTRA_TEST_POSTGRES`).

Always verify this section against the repository because the project will evolve.

---

## Standard Commands

Restore:

```bash
dotnet restore
```

Build:

```bash
dotnet build
```

Run API:

```bash
dotnet run --project Contractra.Api
```

Before reporting that implementation is complete, build the solution unless there is a documented reason it cannot be built.

---

## Primary Principle

Contractra is a financial business system.

Correctness, traceability, maintainability, and preservation of confirmed business rules are more important than producing code quickly.

Understand first.

Plan second.

Implement third.

Verify last.
