---
name: geodc-persistence-builder
description: Add or refine geoDC persisted aggregates, EF Core SQLite mappings, application persistence abstractions, infrastructure queries or services, DI registration, audit behavior, ProjectFingerprint coverage, and workspace persistence handlers. Use for schema and persisted-field changes or data-access behavior. Use geodc-project-session-workflow-builder when the primary change is `.gdcproj` open/save/package lifecycle or compatibility.
---

# geoDC Persistence Builder

Carry persistence changes through the domain, application boundary, EF mapping, audit/fingerprint surfaces, workspace adapters, and focused round-trip tests.

## Workflow

1. Read `geodc/docs/data-model.md` and `geodc/docs/coding-style.md`; read domain/package docs too when semantics or package compatibility change.
2. Inspect the target aggregate, `GeoDcDbContext`, the nearest application abstraction and infrastructure implementation, DI registration, and neighboring persistence tests.
3. Use [`references/persistence-patterns.md`](./references/persistence-patterns.md) to identify every surface the change may affect.
4. Decide explicitly whether the change affects audit output, `ProjectFingerprint`, workspace load/save handlers, and project schema compatibility.
5. Add focused mapping/query tests plus a persist-and-read-back test at the owning layer.

## Rules

- Keep domain types free of EF, DI, logging, and UI dependencies. Put use-case contracts in `geoDC.Application` and implementations in `geoDC.Infrastructure`.
- Use NUlid and existing validated value objects. Configure value conversions, requiredness, precision, indexes, relationships, and delete behavior deliberately in `GeoDcDbContext`.
- The repository currently bootstraps databases with `EnsureCreatedAsync`; do not invent a migrations workflow. Treat a persisted schema change as a compatibility decision and coordinate package schema handling when existing projects are affected.
- Let `AuditSaveChangesInterceptor` create audit records. Preserve correlation IDs for one logical multi-entity operation; do not add per-row audit entries for bulk `DataSeriesRow` samples.
- For every persisted field or aggregate, explicitly include it in the hand-curated `ProjectFingerprint` projection or document why it is excluded. Keep ordering based on stable business keys, not minted ULIDs.
- Use `IUnitOfWorkFactory` for scoped project data access and register application abstractions in `AddInfrastructure(...)`; do not retain project DbContexts across workflow boundaries.
- Log store and service failures that are handled or translated at this layer, per `geodc/docs/logging.md`; do not also log an exception that is rethrown for the caller to handle.
- Read `Workspace/Handlers/IDENTITY_CONVENTIONS.md` before changing workspace persistence. Use `PersistenceIdentity.ShouldInsert(...)`, propagate ID remaps within the save pass, and preserve preassigned canonical ULIDs.

## Verification

- Assert relational shape where it matters: required fields, conversions, precision, uniqueness, foreign keys, and delete behavior.
- Test write/read round trips and query semantics against SQLite rather than relying only on in-memory object assertions.
- Add audit, fingerprint, workspace-handler, and package compatibility coverage in proportion to the affected surfaces.
- Use `geodc-project-session-workflow-builder` for save/reopen/save-as behavior that spans the package lifecycle.
