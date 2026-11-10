# geoDC persistence patterns

## Primary surfaces

- `geodc/src/geoDC.Infrastructure/Persistence/GeoDcDbContext.cs`: project DbSets, conventions, mappings, indexes, relationships, and delete behavior.
- `geodc/src/geoDC.Infrastructure/Persistence/GeoDcDbContextFactory.cs`, `UnitOfWork.cs`, and `UnitOfWorkFactory.cs`: scoped project-database lifetime.
- `geodc/src/geoDC.Infrastructure/Persistence/DatabaseBootstrapper.cs`: current `EnsureCreatedAsync` bootstrap behavior.
- `geodc/src/geoDC.Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs`: deterministic audit ownership.
- `geodc/src/geoDC.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`: application-to-infrastructure registrations.
- `geodc/src/geoDC.Infrastructure/Diagnostics/ProjectFingerprint.cs`: hand-curated, ID-agnostic persisted projection.
- `geodc/src/geoDC.App/Workspace/Handlers/IDENTITY_CONVENTIONS.md`: insert/update and ID-remapping rules for workspace adapters.
- `geodc/tests/geoDC.App.Tests/Infrastructure/PersistenceTests.cs`: SQLite mapping and persistence coverage.

## Change map

For a new aggregate or persisted field, inspect each applicable item:

1. Domain type, invariant, status workflow, and NUlid identity.
2. `DbSet` and complete EF mapping.
3. Application abstraction and infrastructure implementation if access is not already owned by a broader service.
4. `AddInfrastructure(...)` registration and correct lifetime.
5. Audit behavior and logical-operation correlation.
6. `ProjectFingerprint` projection, stable ordering, and coverage.
7. Workspace load/save handler ordering and identity propagation.
8. Existing-project/package schema compatibility.
9. SQLite mapping, round-trip, query, audit, fingerprint, and handler tests.

Project packages contain the project database, but container format/lifecycle and relational persistence are separate concerns. Cross into `geodc-project-session-workflow-builder` only when the change affects opening, saving, compatibility checks, or active project transitions.
