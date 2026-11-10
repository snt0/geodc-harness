---
name: geodc-wpf-ui-builder
description: Build or refine app-level geoDC WPF views and page composition in geoDC.App from viewmodels, requirements, or wireframes. Use for XAML layout, bindings, information hierarchy, app copy, accessibility, and composition from existing geoDC.App.UI controls. Use geodc-shared-control-builder to create reusable toolkit controls and geodc-theme-layout-resource-builder to change shared tokens or dictionary infrastructure.
---

# geoDC WPF UI Builder

Compose operational application views from existing shared controls, typed resources, and viewmodel state.

## Workflow

1. Read the target viewmodel and nearby view; use `MainWindow.xaml` for shell composition and `Features/Setup/SetupPage.xaml` for page composition.
2. Consult [`references/ui-patterns.md`](./references/ui-patterns.md) for the current control map.
3. Map information hierarchy, selection, empty states, commands, and validation to explicit bindings before laying out the view.
4. Compose with existing `ui:` controls and panels, typed theme/layout resources, and generated app string resources.
5. Keep a new pattern page-local unless reuse is established; route an intentional promotion to `geodc-shared-control-builder`.

## Rules

- Prefer `UiTextBlock`, cards, dock panes, navigation controls, and other existing `geoDC.App.UI` components over styled stock controls.
- Prefer `ui:UiGrid`, `ui:UiStackPanel`, `ui:UiWrapPanel`, and `ui:UiAutoGrid`; use `ui:FlowPanel` for its responsive weighted-row behavior. Let panel `Spacing` replace repeated child margins.
- Use typed `ThemeResource` and `LayoutResource` access. Do not add a shared token as part of ordinary page composition; use the theme-resource skill when the catalog must change.
- Keep behavior in bindings, commands, and viewmodels. Code-behind is for genuinely view-owned mechanics.
- Put user-facing app copy in `geodc/src/geoDC.App/Strings/*.yaml` and use the generated access path. Write it to `geodc/docs/copy-style.md`.
- Preserve keyboard access, focus order, labels, AutomationIds needed by established automation, selection cues, and meaningful empty states.
- A root that does not use `UiWindow` styling must establish foreground and retain `ForegroundRootTests` coverage.

## Verification

- Run the narrowest relevant app/UI tests and `git diff --check`.
- Compilation does not prove layout, focus, keyboard, popup, or rendering behavior. Ask before using `wpf-ui-driver`; use it only when live UI verification is warranted.
