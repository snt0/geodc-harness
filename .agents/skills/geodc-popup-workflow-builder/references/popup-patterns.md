# geoDC Popup Workflow Patterns

Use this reference for concrete entry points and contracts. The public architecture is also documented in `geodc/docs/ui/popups.md`.

## Primary exemplars

- `geodc/src/geoDC.App.UI/UI/Popups/PopupDefinition.cs`: definition-owned presentation, viewmodel creation, actions, dismissal result, session scope, and disposal.
- `geodc/src/geoDC.App.UI/UI/Popups/PopupPresentation.cs`: default interaction, modality, frame identity, sizing, owner, close affordances, and whether the frame or the content scrolls the body.
- `geodc/src/geoDC.App.UI/UI/Popups/PopupService.cs`: typed definition resolution, host-strategy routing, cancellation, and active overlay stack.
- `geodc/src/geoDC.App.UI/UI/Popups/PopupAction.cs`: shared footer roles and label binding.
- `geodc/src/geoDC.App.UI/UI/Popups/IPopupChannel.cs`: session completion and guarded close boundary exposed to viewmodels.
- `geodc/src/geoDC.App.UI/UI/Popups/IPopupCloseGuard.cs`: dirty-editor close veto contract.
- `geodc/src/geoDC.App.UI/UI/Popups/OverlayPopupHostStrategy.cs` and `WindowPopupHostStrategy.cs`: Blocking hosting, and Detached/Immersive hosting (one window strategy registered per interaction with `PopupWindowFactory` or `FullWindowFactory`).
- `geodc/src/geoDC.App.UI/UI/Controls/PopupHost.cs` and `PopupFrame.cs`: overlay stack host and common chrome.
- `geodc/src/geoDC.App.UI/UI/Popups/DialogPopupDefinition.cs` and `geodc/src/geoDC.App.UI/UI/Dialogs/DialogService.cs`: dialogs on the same pipeline.
- `geodc/src/geoDC.App/Features/Launch/ProjectSelectorPopupDefinition.cs`: startup definition returning a typed project-session decision.
- `geodc/src/geoDC.App/Features/Setup/Popups/ProjectMetadataEditorPopupDefinition.cs`: app form definition with scoped construction and footer actions.
- `geodc/src/geoDC.App.UI.Sample/Popups/SamplePopupDefinitions.cs`: one body exercised under multiple interactions.
- `geodc/tests/geoDC.App.UI.Tests/Popups/PopupDefinitionTests.cs`, `PopupServiceTests.cs`, and `WindowPopupHostStrategyTests.cs`: definition, routing/stack, and window behavior.
- `geodc/tests/geoDC.App.UI.Tests/Loading/LoadingServiceTests.cs`: async popup lifetime held by an `await using` scope.

## Workflow shape

1. Define a request record and structured result where appropriate.
2. Implement the workflow viewmodel; inject `IPopupChannel<TResult>` if it completes itself.
3. Implement a `PopupDefinition` that declares `PopupPresentation`, constructs the viewmodel, and declares footer actions.
4. Register the definition and a data template keyed by the viewmodel type.
5. Call `IPopupService.ShowAsync<TRequest, TResult>(request, cancellationToken: cancellationToken)`; pass `PopupOptions` only for a genuine per-call override.

The definition chooses the default interaction and modality. The call site may override them without changing the content, but must not restate them. Do not encode interaction choice into the request or duplicate one definition per host.

## Action and close contracts

- `PopupAction.Primary` is the affirmative default action; `Secondary` is a non-terminal alternative role; `Cancel` routes through guarded close; `Result` completes with a fixed result.
- Saving/completing does not pass through `IPopupCloseGuard`. Esc, backdrop, frame close, cancel, and programmatic close do.
- `PopupAction.Dismiss` bypasses the guard; use it only when bypass is deliberate.
- Keep wizard Back/Next navigation in `WizardFrame`; it is not a popup terminal-action row.

## Behavior contracts worth testing

- The matching typed definition is selected and its default interaction reaches the expected strategy.
- `PopupOptions` overrides interaction/modality without changing definition content.
- Nested Blocking sessions stack LIFO; only the top session owns close/focus gestures.
- Cancellation dismisses the intended session and unwinds resources.
- Modal and modeless windows converge on the same typed completion/dismissal semantics.
- Close guards run once per gesture and can veto close.
- Viewmodels and optional service scopes are disposed when the session completes, and released again if
  session construction fails after the viewmodel is built — nothing owns them until `Completed` is wired.
- Action `CanExecute`, default Enter behavior, and per-action AutomationIds match the declared contract.
