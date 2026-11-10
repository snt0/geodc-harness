---
name: geodc-shared-control-builder
description: Create or refine reusable WPF toolkit controls in geoDC.App.UI. Use when a UI pattern needs a stable public interaction contract, control template, theme registration, Sample-gallery variants, and behavior tests. Do not use for one-page XAML composition or theme-token catalog changes without a control.
---

# geoDC Shared Control Builder

Deliver reusable controls as a coherent toolkit feature rather than app-local XAML fragments.

## Workflow

1. Confirm recurrence or a stable interaction contract justifies promotion from page-local XAML.
2. Read `geodc/docs/ui/controls.md` (and `geodc/docs/ui/sample-gallery.md` for gallery coverage), the nearest controls and templates, and the sample gallery.
3. Define the public surface: base class, dependency properties, commands, routed events, and small helper types.
4. Implement the control and its template, register its dictionary in `Themes/ThemeResources.xaml`, and add meaningful sample states.
5. Add template smoke and public-behavior tests in `geodc/tests/geoDC.App.UI.Tests/Controls`.

## Rules

- Keep `geoDC.App.UI` domain-free. Business validation and workflow orchestration remain in the app layer.
- Keep the public surface narrow and semantic; do not create a control family without demonstrated variants.
- Use existing typed resources and shared styles. Route changes to the token catalog or dictionary wrappers through `geodc-theme-layout-resource-builder`.
- Give a control derived directly from `Control`, `ContentControl`, `HeaderedContentControl` or `ButtonBase` an `OnCreateAutomationPeer` override: `UiStructuralAutomationPeer` named from its caption, or `UiInvokableAutomationPeer` / `UiToggleAutomationPeer` when it is clickable. Those bases create no peer, so the control is absent from the UI Automation tree and an AutomationId on it does nothing. A control that stays out on purpose goes in `DeliberatelyPeerless` in `ToolkitAutomationPeerCoverageTests` with its reason.
- Exercise selection, grouping, commands, routed events, property synchronization, and custom rendering only when they are part of the public contract.
- Behavior tests generally must not pin incidental theme choices such as exact sizes, spacing, or brushes. Assert them only when the value is itself functional behavior.
- Use `geodc-test-infrastructure-builder` only if the work requires a new or changed reusable STA/Application/render harness; ordinary control tests belong to this skill.

## Required bundle

- Control and helper types under `geodc/src/geoDC.App.UI/UI`
- Owning template/theme dictionary and registration in `ThemeResources.xaml`
- Sample-gallery coverage showing important states
- Focused control tests

Call out any remaining page-local variant that the shared control does not yet replace.
