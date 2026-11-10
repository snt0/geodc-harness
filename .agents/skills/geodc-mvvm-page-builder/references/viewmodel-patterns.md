# geoDC page viewmodel patterns

## Current exemplars

- `geodc/src/geoDC.App/Shell/MainShellViewModel.cs`: shell navigation, filtered views, summary counts, and commands.
- `geodc/src/geoDC.App/Features/Calibration/CalibrationPageViewModel.cs`: large operational page orchestration.
- `geodc/src/geoDC.App/Features/Landing/ProjectLandingPageViewModel.cs`: workspace listeners, selection-driven details, popup-result mutation, and summary refresh.
- `geodc/src/geoDC.App/Features/Landing/ProjectLandingJobStageBuilder.cs`: extracted workflow-stage aggregation.
- `geodc/src/geoDC.App/Features/Survey/SurveyPageViewModel.cs`: named-handler subscription tracking and `IAsyncDisposable` teardown of workspace, per-item, and chart-host resources.
- `geodc/tests/geoDC.App.Tests/Conventions/PageViewModelLifetimeTests.cs`: churns the workspace after disposal and asserts a disposed page observes nothing.

## Shape

1. Choose the workspace collection or service result that owns each piece of durable state.
2. Expose editable inputs and primary selections as observable properties.
3. Derive detail lists, labels, visibility, counts, and command availability from those anchors.
4. Route all mutations through a command or typed workflow result.
5. Refresh affected projections together and restore selection by stable entity ID.

Use `ICollectionView` only for user-visible filtering, sorting, or grouping. Store it, give it a named filter, and refresh it explicitly when inputs change.

For async commands, validate state, enter the page's existing busy/error boundary, build a request, call one service action, apply the result, and publish a concrete status outcome.
