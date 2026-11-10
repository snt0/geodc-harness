---
name: geodc-theme-layout-resource-builder
description: Build or refine geoDC shared WPF theme and layout infrastructure. Use for token catalogs, typed ThemeResource or LayoutResource access, dictionary wrappers and merge order, diagnostic fallbacks, or shared dictionary registration. Use geodc-shared-control-builder for control templates and behavior, and geodc-wpf-ui-builder for ordinary page-level resource consumption.
---

# geoDC Theme and Layout Resource Builder

Keep shared styling in the typed theme and layout catalogs rather than in raw resource keys, ad hoc values, or page-local dictionaries. This skill owns resource infrastructure, not page styling or shared-control behavior.

## Workflow

1. Read the target XAML, `geodc/docs/ui/theming.md` > Design Tokens, and [`references/resource-patterns.md`](./references/resource-patterns.md).
2. Inspect the target dictionary and `geodc/src/geoDC.App.UI/Themes/ThemeResources.xaml` so merge order and ownership stay explicit.
3. Decide whether the need is a new token, a renamed token, a fallback adjustment, a new control dictionary, or a usage-only cleanup. If only one page needs it and it is unlikely to recur, fix the page usage instead of adding a token.
4. Edit the token XAML source. `TokenCatalogGenerator` regenerates the typed enums, catalogs, parsing, and diagnostic fallbacks; never edit generated `.g.cs` files.
5. Update usage sites in the same change as a rename or addition.
6. Add or update tests when catalog coverage, fallback behavior, or dictionary composition changes.

## Rules

- `ThemeResources.xaml` and `Tokens.Layout.xaml` are the canonical sources; catalogs and enums are the typed access layer, not a second source of truth.
- In XAML, use `{theme:ThemeResource ...}` and `{layout:LayoutResource ...}` rather than string resource keys.
- Prefer shared tokens for recurring spacing, padding, margins, radii, and brushes. An occasional ad hoc value for an isolated alignment fix is acceptable; do not build common patterns around one.
- Preserve diagnostic fallback resources so a missing key stays obvious in tests rather than blending into production colours or sizes.
- Prefer `DynamicResource` for cross-dictionary references that depend on merge order, especially shared focus visuals and base styles that tests may load in isolation.
- Shared dictionaries, catalogs, and markup extensions belong in `geoDC.App.UI`. App merge order belongs in `geodc/src/geoDC.App/App.xaml`. The Sample app (`geodc/src/geoDC.App.UI.Sample/App.xaml`) resolves through the same `ThemeDictionary` and `LayoutDictionary` entry points.
- Give a reusable control family its own dictionary when its templates, triggers, or visual states would make a broader dictionary hard to scan. Register it in `ThemeResources.xaml` in the same change, and check that the Sample app reaches it without extra merges.

## Verification

- Update catalog smoke tests when the set of known keys changes.
- Check that a new token exists in its source dictionary and resolves through the typed markup extension.
- When merge order changes, check that popup, shell, Sample, and shared-control resources still resolve through the expected entry points.
- In resource smoke tests, load the real XAML dictionaries under a real WPF `Application` rather than constructing wrapper dictionaries in isolation.
