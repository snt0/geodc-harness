---
name: geodc-popup-workflow-builder
description: Build or refine geoDC WPF popup workflows using typed PopupDefinition requests/results, session-scoped viewmodels, shared frame actions, Blocking/Detached/Immersive hosting, close guards, dialogs, templates, DI registration, and behavior tests in geoDC.App.UI, geoDC.App, or the Sample gallery.
---

# geoDC Popup Workflow Builder

Use the shared popup pipeline; do not introduce an ad hoc `Window`, `MessageBox`, or page-local overlay for a workflow the pipeline can express.

## Start here

1. Read `geodc/docs/ui/popups.md`.
2. Read [references/popup-patterns.md](references/popup-patterns.md) and the nearest current definition/template/test.
3. Define the typed request, result, and dismissal semantics before writing XAML.
4. Decide independently what the popup contains and how the call site presents it.

## Architecture rules

- Implement `PopupDefinition<TRequest, TResult, TViewModel>`. Override `Define`, `CreateActions`, and `GetDismissResult` as needed; override `CreateViewModel` only when DI plus the request/channel cannot express construction.
- Put title, size class, close behavior, modality, and optional frame `AutomationId` in `PopupPresentation`. Declare a `SizeClass` (`Dialog`, `Tool` or `Workbench`; `geodc/docs/ui/popups.md` > Size classes) rather than a width: it sets the width, minimum width and default `PopupInteraction`. Declare a `Width` only for another width inside the class's range, and an `Interaction` only when the popup must open elsewhere.
- Leave `PopupPresentation.ScrollsBody` alone unless the body holds a region that must stay put while the rest scrolls (a section rail, a pinned summary); declaring `false` moves scrolling into the content, which must then supply its own `ScrollViewer` and a `Height` for the viewport.
- Let the caller override only `PopupOptions.Interaction` or `IsModal` when that invocation genuinely differs from the definition default; never pass `PopupOptions` that restate it.
- Use `Blocking` for an in-window LIFO overlay, `Detached` for a chromeless card window, and `Immersive` for a full workflow window.
- Take `IPopupChannel<TResult>` in a workflow viewmodel when it completes or dismisses its session. Do not make callers scrape mutable fields after close.
- Put terminal actions in `PopupDefinition.CreateActions` using `PopupAction.Primary`, `Secondary`, `Cancel`, or `Result`. The command's `CanExecute` owns enabled state.
- Put dirty-editor decisions in `IPopupCloseGuard`; cancel actions terminate and must not ask a second confirmation.
- Supply `IServiceScopeFactory` when the definition relies on scoped services or default `ActivatorUtilities` construction. The definition owns session-scope and disposable-viewmodel cleanup.
- Keep shared frame/chrome in `geoDC.App.UI`; keep domain-specific bodies and templates in `geoDC.App`.
- Register definitions as `IPopupDefinition`, host strategies as `IPopupHostStrategy`, and body templates as typed `DataTemplate`s merged into `Application.Resources`: in the app through the owning feature's `Features/<Area>/<Area>Templates.xaml` (or `Shell/ShellTemplates.xaml`) that `App.xaml` merges, never a page's `Resources`, which `Detached` and `Immersive` windows cannot see; in the Sample gallery through its own popup template dictionary.

## UI and automation

- Use typed theme/layout resources and the `ui:` spacing panels (`UiStackPanel`, `UiAutoGrid` and the rest) in popup bodies.
- Give each footer action a page-scoped `{Noun}Button` AutomationId.
- Frames are `PopupPanel` by default and dialogs are `DialogPanel`; the footer is `PopupFooterPanel`.
- Give a popup its own `PopupPresentation.AutomationId` when it can stack over another popup.
- Add reusable popup states to the Sample gallery. Ask the user before invoking the UI driver, then use the `wpf-ui-driver` skill for live verification.

## Verification

Test request-to-viewmodel construction, presentation defaults and caller overrides, action enablement/results, dismissal/cancellation, close guards, LIFO blocking behavior, owner/modality behavior, and scope/disposal lifetime as applicable. Compilation alone does not prove focus, keyboard, stacking, or live layout behavior.
