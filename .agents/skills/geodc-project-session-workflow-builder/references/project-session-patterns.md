# geoDC Project Session Workflow Patterns

Use this reference when the task needs the concrete geoDC project package lifecycle rather than generic WPF file-open or persistence advice.

## Primary exemplars

- `geodc/src/geoDC.App/Features/Launch/ProjectSessionService.cs`: project open and create orchestration, package lifecycle reset, workspace reload, and preference persistence.
- `geodc/src/geoDC.App/Features/Launch/ProjectFileWorkflowService.cs`: shell open, save, and save-as flows with confirmation, metadata validation, notifications, and logging.
- `geodc/src/geoDC.App/Features/Launch/ProjectSelectorModels.cs`: typed startup action and result shapes that feed the session service.
- `geodc/src/geoDC.App/Features/Launch/ProjectSelector.cs`: startup entry point that sequences popup-driven project selection.
- `geodc/src/geoDC.App/Workspace/AppProjectWorkspace.cs`: in-memory workspace state that is cleared and repopulated during project switches.
- `geodc/src/geoDC.App.Core/Workspace/IModuleLoadHandler.cs` and `geodc/src/geoDC.App.Core/Workspace/IModuleSaveHandler.cs`: ordered handler contracts for workspace hydration and persistence. Handlers take their dependencies through the constructor, and `WorkspaceModuleOrder` names their order.
- `geodc/src/geoDC.App/Workspace/WorkspaceModuleCoordinator.cs`: the one place that runs the load and save handlers, including the save's single unit of work, its commit and `OnSaveCompleted`.
- `geodc/src/geoDC.Infrastructure/ProjectPackages/ProjectPackageService.cs`: zstd-tar package open, save, save-as, legacy ZIP reads, integrity checks, encryption, and working-directory lifecycle.
- `geodc/src/geoDC.Application/Abstractions/ProjectPackages/IProjectPackageService.cs`: package lifecycle boundary used by orchestration services.
- `geodc/docs/project-packages.md`: package format, manifest compatibility, integrity, encryption, and host metadata contract. Read it before changing the package implementation.

## Preferred workflow shape

1. Resolve the project action and normalize the project file path.
2. Guard against missing files, duplicate targets, or absent required metadata.
3. Open or create the project package through `IProjectPackageService`.
4. Ensure supporting infrastructure such as the project database exists.
5. Clear workspace and cached navigation state when the active package changes.
6. Run the ordered load or save handlers through `IWorkspaceModuleCoordinator`; never repeat its loop.
7. Persist user preferences and recent-project tracking when the project path changes or becomes active.
8. Surface confirmation, notifications, and logs from the outer workflow service.

## Split responsibilities deliberately

- Keep popup or shell callers thin; they should request a workflow action, not rebuild the workflow.
- Keep package persistence in the package service.
- Keep module-specific serialization and hydration inside ordered load and save handlers.
- Keep preference and recent-project persistence in the orchestration layer so it stays coupled to successful package transitions.

## Behavior contracts worth testing

- Open rejects missing package paths and avoids reopening the already-active project.
- Create rejects existing target files and applies seed or metadata details after workspace load.
- Save validates required metadata before persisting handlers and package output.
- Save-as updates the package path, last-project preference, and recent-project list together.
- Handler execution honors ordering and runs inside the expected unit-of-work boundary.
