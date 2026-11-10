# geoDC application UI patterns

## Exemplars

- `geodc/src/geoDC.App/MainWindow.xaml`: shell bands, navigation, content, output dock, notifications, and footer.
- `geodc/src/geoDC.App/Features/Setup/SetupPage.xaml`: dock panes, scrollable sections, property editing, and card-driven navigation.
- `geodc/src/geoDC.App.UI.Sample/MainWindow.xaml`: catalog of available shared controls and states.
- `geodc/docs/ui/`: authoritative shared-control (`controls.md`), resource (`theming.md`), popup (`popups.md`), and foreground (`theming.md`) guidance.

## Composition map

- Shell region: `UiDockPane`
- Informational section: `UiSectionCard`
- Editable metadata: `UiPropertyCard` with `UiPropertyRow`
- Drill-in action: `UiCardAction`
- Shell navigation: `UiNavBar` / `UiNavBarItem`
- In-page presentation choices: `UiTabControl` or `UiViewModeSelector`
- Display text: `UiTextBlock` with `FontTypography` and `Tone`

Prefer the `ui:` spacing panels (`UiStackPanel`, `UiWrapPanel`, `UiAutoGrid`) for normal flow and auto-grid layout. Use built-in `Grid` when exact row/column placement is the requirement, and `ui:FlowPanel` for weighted responsive rows.

Before creating page-local templates, check the sample gallery. If the requirement needs a reusable interaction contract, multiple visible variants, or would otherwise be copied between pages, switch to `geodc-shared-control-builder`.
