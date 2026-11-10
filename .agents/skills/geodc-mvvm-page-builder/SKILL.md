---
name: geodc-mvvm-page-builder
description: Build or refine geoDC application page and shell viewmodels with CommunityToolkit.Mvvm. Use for workspace-backed page state, selections, projections, computed summaries, command enablement, refresh fanout, and service orchestration in geoDC.App. Use geodc-popup-workflow-builder for popup sessions and geodc-project-session-workflow-builder for project open/save lifecycle orchestration.
---

# geoDC MVVM Page Builder

Build page and shell viewmodels that expose deliberate bindable state and keep the workspace authoritative.

## Workflow

1. Read the target XAML, existing viewmodel, and one nearby exemplar.
2. Consult [`references/viewmodel-patterns.md`](./references/viewmodel-patterns.md) for current repo examples.
3. Identify the authoritative workspace collections, selection anchors, derived projections, and user commands before adding properties.
4. Implement observable inputs and selections, computed UI state, and focused relay commands.
5. Centralize refresh and property-change fanout; preserve selection by entity ID across collection rebuilds.
6. Add focused tests when command availability, listener lifetime, refresh ordering, or selection restoration is easy to regress.

## Rules

- Prefer `[ObservableProperty]` for editable state, selections, filters, busy state, and status text; prefer computed properties for summaries, counts, visibility, and affordances.
- Keep dependency fanout explicit with partial change handlers, `OnPropertyChanged(...)`, and `NotifyCanExecuteChanged()`.
- Update `ObservableCollection<T>` instances in place. Use page-local item viewmodels only for genuine UI projections.
- Attach and detach workspace and nested collection listeners deliberately; newly added children must participate and removed children must not leak subscriptions.
- A page viewmodel that subscribes to the workspace or the messenger, owns a cancellation source, or creates a chart host must implement `IDisposable` (or `IAsyncDisposable` when it owns something asynchronous) and undo exactly what its constructor did. `NavigationService` builds each page in its own DI scope and disposes that scope on eviction, so disposal is the page's real end of life; make it idempotent and use named handlers rather than lambdas wherever a subscription must be removable.
- Keep commands centered on one user action. Build requests in helpers, call injected services, then apply results and perform one deliberate refresh pass.
- Treat popup results as a mutation boundary: launch through the popup workflow, apply the typed result to workspace state, and refresh dependants once.
- Extract narrow summary or stage builders when cross-collection aggregation would dominate the viewmodel.
- Keep navigation, package lifecycle, and reusable control behavior with their owning services or skills.
- A command that catches a failure and shows it as status text or a notification also logs it (`geodc/docs/logging.md`), unless a service below already logged and translated it.

## Verification

- Test observable outcomes rather than generated members.
- Cover command requery, selection continuity, refresh fanout, and subscription cleanup when changed.
- If the change also alters XAML, use `geodc-wpf-ui-builder`; if it changes popup definitions or sessions, use `geodc-popup-workflow-builder`.
