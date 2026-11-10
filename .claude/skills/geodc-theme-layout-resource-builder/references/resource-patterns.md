# geoDC Theme and Layout Resource Patterns

Use this reference when the task needs the concrete geoDC shared-resource system rather than generic WPF resource-dictionary advice.

## Primary exemplars

- `geodc/src/geoDC.App/App.xaml`: app-level merge point for `LayoutDictionary`, `ThemeDictionary`, and app-owned resource dictionaries.
- `geodc/src/geoDC.App.UI/Themes/ThemeResources.xaml`: shared theme entry point for colors, typography, base styles, and control dictionaries.
- `geodc/src/geoDC.App.UI/Themes/Tokens.Layout.xaml`: canonical source for spacing, radius, thickness, and margin tokens.
- `geodc/src/geoDC.App.UI/Markup/ThemeDictionary.cs`: typed shared-theme dictionary wrapper and fallback loading path.
- `geodc/src/geoDC.App.UI/Markup/LayoutDictionary.cs`: typed shared-layout dictionary wrapper and fallback loading path.
- `geodc/src/geoDC.App.UI/Markup/ThemeResourceExtension.cs`: typed dynamic-resource access for theme keys.
- `geodc/src/geoDC.App.UI/Markup/LayoutResourceExtension.cs`: typed dynamic-resource access for layout keys.
- `geodc/src/geoDC.App.UI.Generators/TokenCatalogGenerator.cs`: generates `ThemeResourceKey`, `LayoutResourceKey`, and their catalogs from the token XAML supplied as `AdditionalFiles`; never edit generated `.g.cs` output.
- `geodc/tests/geoDC.App.UI.Tests/Markup/ResourceDictionaryTokenTests.cs`: smoke tests that ensure all known keys exist in the source dictionaries.

## Edit checklist

When adding or renaming a shared token:

1. Update the canonical XAML dictionary.
2. Let `TokenCatalogGenerator` derive the typed enum, catalog mapping, parser, and fallback from the XAML; do not edit generated output.
3. Update usage sites that should consume the token through typed markup extensions.
4. Update or add tests that confirm the key is generated and remains present in the source dictionary.

## Usage rules

- Prefer typed markup usage in app XAML: `{theme:ThemeResource Resource=...}` and `{layout:LayoutResource Resource=...}`.
- Keep recurring shell or popup spacing token-backed.
- Keep one-off page-local alignment nudges local unless they become a repeated pattern.
- If a resource request is really a control-style concern, check whether it belongs in `ThemeResources.xaml` composition or in a control dictionary rather than in `Tokens.Layout.xaml`.

## Fallback intent

- Theme fallback resources intentionally use loud diagnostic colors and fonts.
- Layout fallback resources intentionally use conspicuous values.
- Do not soften these fallback values unless the debugging contract itself is being redesigned.
