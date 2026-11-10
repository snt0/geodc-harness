---
name: geodc-project-session-workflow-builder
description: "Build or refine geoDC `.gdcproj` session and package lifecycle workflows: open, create, save, save-as, recent/last project state, package compatibility, ordered workspace load/save pipelines, and identity-safe reopen behavior. Use geodc-persistence-builder for general EF mappings, queries, audit, and persisted aggregate work that does not change the project lifecycle."
---

# geoDC Project Session Workflow Builder

Keep project lifecycle orchestration coherent across the shell, workspace handlers, package service, and runtime project context.

## Workflow

1. Read `geodc/docs/project-packages.md` and [`references/project-session-patterns.md`](./references/project-session-patterns.md).
2. Classify the change as session orchestration, file workflow, package format/service, or ordered load/save-handler behavior.
3. Define the action boundary: open, create blank/seeded, save current, save as, inspect recent, or recover/reopen.
4. Normalize and validate paths at the entry point, then reuse the package service and ordered handlers.
5. Keep runtime context, workspace reset/load, preferences, recent-project state, notifications, and logging synchronized with successful transitions.

## Rules

- Callers request a workflow action; they do not assemble package, workspace, and preference steps themselves.
- Use scoped `IUnitOfWorkFactory` access within workflow boundaries. Do not retain a DbContext across project transitions.
- Clear workspace/navigation state only when the active package changes.
- Keep dialogs and operator-facing outcomes in outer orchestration; keep package/container mechanics in `ProjectPackageService`.
- Preserve zstd packages, legacy ZIP reads, manifest provenance/migration, entry integrity, encryption, and host-supplied `IApplicationMetadata` as applicable.
- Read `Workspace/Handlers/IDENTITY_CONVENTIONS.md` before changing save semantics. Use `PersistenceIdentity.ShouldInsert(...)`; preassigned ULIDs may still be new.
- Log project session and package outcomes as `geodc/docs/logging.md` describes, following the existing calls in `ProjectFileWorkflowService` and using the documented property names (`ProjectFilePath`, `ProjectId`, ...). The workflow boundary logs the outcome once.
- Treat identity as a graph: remapped IDs must reach later handlers in the same save pass and remain stable through save, reopen, and save-as.

## Verification

- Prefer workflow-level host tests for open/create/save/save-as/reopen, active-path changes, preferences, recent projects, dialogs, and notifications.
- For identity changes, cover repeat save, `save -> reopen -> mutate -> save`, save-as, and missing-row repair without duplicate rows or changed canonical identity.
- If the change is only an EF mapping, query, audit, or fingerprint concern, use `geodc-persistence-builder` instead.
