# 01 — Vendoring Snt.Lib into geoDC

Verified @ geoDC e9a8766, Snt.Lib d545ca2

Roots: geoDC = `/home/user/geodc`, Snt.Lib = `/home/user/snt.lib`. Paths below are relative to those roots unless absolute.
Counts are from `src/`, `tools/` and `tests/` of geoDC (no `snt:` usage exists under `tests/`), excluding `obj/` and `bin/`.

## 0. Version check: pin matches checkout exactly

| Evidence | Value |
|---|---|
| geoDC pin | `Directory.Packages.props:33-35` → `Snt.Lib.General`, `Snt.Lib.Wpf`, `Snt.Lib.Wpf.Text.Generators` = `0.10.0-preview.8`; feed `nuget.config:6` (`https://nuget.sthornton.co.uk`), source mapping `nuget.config:12-14` (`Snt.*`) |
| Snt.Lib HEAD | `d545ca2` "feat: inline tokens and text messages in generated text (#7)", 2026-10-29; no tags in the checkout |
| NBGV height | `version.json` = `0.10-preview.{height}`; last version bump `edeb5f0`; longest path edeb5f0→HEAD is 8 commits (d545ca2, 189a9ce, 2a9b161, ebdc6e6, 415d35c, c29001f, afd7879, edeb5f0) → `0.10-preview.8` |
| Package provenance | `~/.nuget/packages/snt.lib.wpf/0.10.0-preview.8/snt.lib.wpf.nuspec` `<repository … commit="d545ca277673e7e1b96b22a2fe5e6fc63e196c01">`; `Snt.Lib.Wpf.dll` informational version `0.10.0-preview.8+d545ca2776` |

Conclusion: zero drift. The source in `/home/user/snt.lib` is byte-for-byte what geoDC consumes. Note the feature geoDC depends on (TextMessage, tokens, `…Message` members) is still under `## [Unreleased]` in Snt.Lib `CHANGELOG.md:9-29`; geoDC is consuming an unreleased preview.

## 1. Exact inventory of Snt.Lib surface geoDC touches

### 1a. XAML (elements, markup extensions, attached properties)

Prefix declarations: `xmlns:snt="https://schemas.sthornton.co.uk/snt/wpf"` in 170 files (App 64, App.UI 16, Sample 89, `tools/geoDC.TestDataGenerator.Wpf` 1); `xmlns:text="clr-namespace:Snt.Generated.Text"` in 70 files (all App); `xmlns:sntText="clr-namespace:Snt.Lib.General.Text;assembly=Snt.Lib.General"` in 1 file. Total `snt:` tokens in XAML = 2,042 (incl. 170 declarations and closing tags); the ~1128 StackPanel figure in the brief counts open+close tags, opening tags are given below.

| `snt:` name | Kind | Snt source (path:line) | Uses (opening tags / extension uses) | Files | Example geoDC path:line |
|---|---|---|---|---|---|
| StackPanel | Panel | `src/Snt.Lib.Wpf/Controls/StackPanel.cs:12` | 571 | 156 | `src/geoDC.App.UI.Sample/Themes/Gallery.xaml:40`, `src/geoDC.App.UI.Sample/Gallery/Playground/PropertyPlayground.xaml:156` |
| TrueToVisible | Converter ext. | `src/Snt.Lib.Wpf/Converters/TrueToVisible.cs:8` | 329 | 57 | `src/geoDC.App.UI.Sample/Views/NotificationSampleView.xaml:87`, `src/geoDC.App.UI.Sample/Specimens/Collections/UiListGroupHeaderSpecimens.xaml:43` |
| FalseToVisible | Converter ext. | `Converters/FalseToVisible.cs:8` | 80 | 28 | `src/geoDC.App/Themes/Templates/Startup/ProjectSelectorTemplate.xaml:215` |
| WrapPanel | Panel | `Controls/WrapPanel.cs:14` | 73 | 39 | `src/geoDC.App.UI.Sample/Gallery/Playground/PropertyPlayground.xaml:158` |
| AutoGrid | Panel | `Controls/AutoGrid.cs:14` | 48 | 17 | `src/geoDC.App.UI.Sample/Views/NotificationSampleView.xaml:54`, `tools/geoDC.TestDataGenerator.Wpf` (24 tokens) |
| NotNullToVisible | Converter ext. | `Converters/NotNullToVisible.cs:8` | 48 | 18 | `src/geoDC.App/Themes/Templates/Popups/VrfTransformationPopupTemplates.xaml:147` |
| MatchText | Markup ext. | `src/Snt.Lib.Wpf/Text/MatchTextExtension.cs:9` | 41 | 13 | `src/geoDC.App/Themes/Templates/Startup/NewProjectTemplate.xaml:14`, `…/ProjectSelectorTemplate.xaml:185` |
| EqualityToVisibility | Converter ext. | `Converters/EqualityToVisibility.cs:13` | 34 | 6 | `src/geoDC.App/Themes/Templates/Popups/DataAnalysisPopupTemplate.xaml:427` |
| Grid | Panel | `Controls/Grid.cs:18` | 26 | 15 | `src/geoDC.App/Themes/Templates/Popups/ShoreControlFormPopupTemplates.xaml:170` |
| NotNullOrEmptyToVisible | Converter ext. | `Converters/NotNullOrEmptyToVisible.cs:8` | 25 | 14 | `…/ShoreControlFormPopupTemplates.xaml:161` |
| EnumToBoolean | Converter ext. (two-way) | `Converters/EnumToBoolean.cs:17` | 13 | 4 | `src/geoDC.App/Views/ShoreControl/ShoreControlRegisterView.xaml:254` |
| TextInlines.Message | Attached DP | `Text/TextInlines.cs:32` | 10 | 5 | `src/geoDC.App.UI.Sample/Specimens/Foundations/UiReferenceChipSpecimens.xaml:18`, `src/geoDC.App.UI/Themes/Controls.Badges.xaml:405` |
| NotNullOrWhiteSpaceToVisible | Converter ext. | `Converters/NotNullOrWhiteSpaceToVisible.cs:8` | 10 | 8 | `src/geoDC.App.UI/Themes/Controls.Sections.xaml:854` |
| FormatText | Markup ext. | `Text/FormatTextExtension.cs:9` | 9 | 7 | `…/ProjectSelectorTemplate.xaml:66`, `src/geoDC.App/Views/ShoreControl/ShoreControlJobCopyView.xaml:226` |
| NullToVisible | Converter ext. | `Converters/NullToVisible.cs:8` | 6 | 4 | `src/geoDC.App/Views/Attempts/DatasetSlot.xaml:22` |
| BooleanNegation | Converter ext. | `Converters/BooleanNegation.cs:13` | 6 | 2 | `src/geoDC.App/Themes/Templates/Popups/CreateFeaturePopupTemplate.xaml:122` |
| NotNullToBool | Converter ext. | `Converters/NotNullToBool.cs:13` | 5 | 2 | `src/geoDC.App/Views/Attempts/DatasetSlot.xaml:15` |
| TextInlines.Renderer | Attached DP (inherits) | `Text/TextInlines.cs:36` | 3 | 2 | `…/UiReferenceChipSpecimens.xaml:19` |
| NullOrEmptyEnumerableToVisible | Converter ext. | `Converters/NullOrEmptyEnumerableToVisible.cs:8` | 3 | 2 | `src/geoDC.App/Views/Attempts/AttemptListPane.xaml:62` |
| StringToVisibility | Converter ext. | `Converters/StringToVisibility.cs:13` | 1 | 1 | `src/geoDC.App.UI/Themes/Controls.Cards.xaml:987` |
| FalseToHidden | Converter ext. | `Converters/FalseToHidden.cs:8` | 1 | 1 | `src/geoDC.App/Views/Attempts/AttemptChartSection.xaml:28` |
| EqualityToBoolean | Converter ext. | `Converters/EqualityToBoolean.cs:13` | 1 | 1 | `src/geoDC.App.UI/Themes/Controls.Lists.xaml:467` |
| sntText:TextMessage | `x:Type` in DataTemplate | `src/Snt.Lib.General/Text/TextMessage.cs:15` | 1 | 1 | `src/geoDC.App.UI/Themes/Controls.Badges.xaml:403` |

Not used in XAML: `snt:StringText`, `snt:SwitchText`, `snt:BoolToVisibility`, badges, `NotificationHost`, `PlaceholderControl`, every string/number/count converter.

Panel properties geoDC actually sets (from the 718 opening tags): StackPanel `Spacing` 525, `Orientation` 117, `VerticalContentAlignment` 4; WrapPanel `HorizontalSpacing` 71, `VerticalSpacing` 58, `LineAlignment` 9, `Orientation` 2, `Horizontal/VerticalContentAlignment` 1 each; AutoGrid `Columns` 47, `ColumnSpacing` 40, `Rows` 19, `RowSpacing` 13, `Orientation` 10; Grid `ColumnSpacing` 23, `RowSpacing` 4, `RowDefinitions`/`ColumnDefinitions` string form 1 each. No `AutoGrid.Diagnostics`/`AutoGrid.Parse` use in C#.

### 1b. C# types

| Type | Snt source | geoDC lines / files | Examples |
|---|---|---|---|
| `TextMessage` | `src/Snt.Lib.General/Text/TextMessage.cs:15` | 72 / 20 | `src/geoDC.App.Core/Diagnostics/Diagnostic.cs:30`, `src/geoDC.App.UI/UI/Behaviors/ContextualHelp.cs:271` |
| `TextRun` | `General/Text/TextRun.cs:4` | 21 / 8 | `src/geoDC.App.UI.Sample/Specimens/Foundations/UiReferenceChipSpecimens.xaml.cs:36` |
| `TextToken` | `General/Text/TextToken.cs:8` | 16 / 8 | `src/geoDC.App.UI/UI/Controls/UiTermRenderer.cs:33` |
| `TextArgument` | `General/Text/TextArgument.cs:8` | 14 / 8 | `src/geoDC.App.UI/UI/Controls/UiReferenceChipRenderer.cs:36` |
| `TextPart` | `General/Text/TextPart.cs:11` | 3 / 2 | `src/geoDC.App/Popups/VrfTransformations/VrfTransformationIssueText.cs:178` |
| `TextInlines` (`DefaultRenderer`, `SetMessage`) | `Text/TextInlines.cs:29` | 16 / 9 | `src/geoDC.App/Startup/TextInlinesBootstrap.cs:23`, `ContextualHelp.cs:288`, `src/geoDC.App.UI/UI/Controls/InlineDescription.cs:33` |
| `ITextPartRenderer` | `Text/ITextPartRenderer.cs:13` | 5 / 5 | `TextInlinesBootstrap.cs:27`, `UiReferenceChipRenderer.cs:19` |
| `TextPartRendererCollection` | `Text/TextPartRendererCollection.cs:9` | 1 / 1 | `TextInlinesBootstrap.cs:31` |
| `TextTokenRenderer` | `Text/TextTokenRenderer.cs:8` | 1 / 1 | `UiTermRenderer.cs:17` |
| `TextResourceKey` (`FormatMessage`) | `Text/TextResourceKey.cs:7` | 1 / 1 | `TextInlinesBootstrap.cs:40` (via `GlossaryText.DefinitionMap.TryResolve`, i.e. `StringTextMap.cs:31`) |
| `LambdaConverter.Create` | `Converters/LambdaConverter.cs:11` | 5 / 1 | `src/geoDC.App/Converters/AppConverters.cs:34,38,46,56,64` |
| `using` lines | — | `using Snt.Generated.Text;` 221 files (App 177, App.Tests 44); `using Snt.Lib.General.Text;` 22; `using Snt.Lib.Wpf.Text;` 11; `using Snt.Lib.Wpf.Converters;` 1 | full list of the 24 Snt.Lib-using files in §6 |
| Doc crefs | — | 2 | `src/geoDC.App.UI/UI/Controls/UiHelpBadge.cs:71,82` (`Snt.Lib.General.Text.TextMessage`) |

Generated output consumed: 20 YAML files in `src/geoDC.App/Strings/` (3,740 lines) → 20 classes `Snt.Generated.Text.<Name>Text`; XAML `{x:Static text:…}` 1,580 refs across 18 classes; C# member refs 2,109 in 223 files (incl. tests). Nothing references `GeneratedTextResourceAttribute`, `TextResourceMap`, `ITextMap`, `EnumTextMap`, `NumberTextMap`, `BooleanTextMap`, `TextTemplate` directly; they appear only in generated code.

### 1c. MSBuild / packaging

| Item | geoDC path:line | Note |
|---|---|---|
| `PackageReference Snt.Lib.Wpf` | `src/geoDC.App/geoDC.App.csproj:21`, `src/geoDC.App.UI/geoDC.App.UI.csproj:19` | Sample, Branding, TestDataGenerator.Wpf, App.UI.Tests get it transitively through App.UI (`geoDC.App.UI.Sample.csproj:4`, `tools/geoDC.TestDataGenerator.Wpf/….csproj:5`) |
| `PackageReference Snt.Lib.Wpf.Text.Generators PrivateAssets="all"` | `geoDC.App.csproj:22` | only project running the generator |
| `PackageReference Snt.Lib.General` | `src/geoDC.App.Core/geoDC.App.Core.csproj:11` | App.Core is `net10.0`, no WPF (`:15`) |
| Pins / feed | `Directory.Packages.props:33-35`, `nuget.config:6,12-14` | the only private-feed references in the repo |
| Generator inputs | none in geoDC | supplied by the package's `buildTransitive/Snt.Lib.Wpf.Text.Generators.targets:6-13` (`Strings\**\*.yaml`, `@(Page)`, `@(ApplicationDefinition)` → `AdditionalFiles`) and `:16` (`CompilerVisibleProperty SntTextGeneratedNamespace`) |
| `SntTextGeneratedNamespace`, `SntTextEnableAutomaticInputs` | not set anywhere in geoDC | defaults apply: namespace `Snt.Generated.Text` (`TextResourceGenerator.cs:20`) |
| `EmitCompilerGeneratedFiles` | `geoDC.App.UI.csproj:6-7` only (for geoDC's own token generator) | **not** set on geoDC.App, so Snt output never reaches disk |
| `.editorconfig` severities for `SNTTXT*` | none | analyzer runs at its built-in Error severity |

### 1d. Confirmed unused by geoDC

Snt.Lib.Wpf `Controls/Badge*.cs`, `BadgeAppearance.cs`, `IconPlacement.cs`, `NotificationHost.cs`, `NotificationTemplateSelector.cs`, `PlaceholderControl.cs`; all of `Services/` (notifications); `Themes/Generic.xaml` (only styles the above: `:7,27,261,353,453`); `Snt.Lib.Wpf.WpfUi` and `Snt.Lib.Wpf.Notifications.WpfUi` (not in `Directory.Packages.props`); `Snt.Lib.General/RateLimiting/*`; `Snt.Lib.Wpf.Core` (an empty csproj, `src/Snt.Lib.Wpf.Core/` has no sources); converters `BoolToVisibility`, `TrueToHidden`, `NullOrEmptyToVisible`, `NullOrWhiteSpaceToVisible`, `NotNullOrEmptyEnumerableToVisible`, `EnumerableHasItems`, `EnumerableIsNullOrEmpty`, `EmptyStringToValue`, `NullToValue`, `String*` (Join/Trim/ToUpper/ToLower/IsNullOr*), `NumberComparison*`, `NumericComparison*`, `CountComparison*`; text `StringTextExtension`, `SwitchTextExtension`. CommunityToolkit.Mvvm is used only by the notification types (`Services/NotificationItem.cs`, `ProgressNotification.cs`, `AcknowledgeableNotification.cs`, `Controls/NotificationHost.cs`).

## 2. Transitive closure inside Snt.Lib — the copy list

### 2a. Panels (1,931 lines)

| Snt.Lib file | Lines | Why it comes along |
|---|---|---|
| `src/Snt.Lib.Wpf/Controls/StackPanel.cs` | 250 | used |
| `src/Snt.Lib.Wpf/Controls/WrapPanel.cs` | 620 | used |
| `src/Snt.Lib.Wpf/Controls/AutoGrid.cs` | 453 | used; derives from Snt `Grid` (`AutoGrid.cs:14`) |
| `src/Snt.Lib.Wpf/Controls/Grid.cs` | 475 | used; holds internal `ColumnDefinitionsConverter`/`RowDefinitionsConverter` (`:365,421`) behind `[TypeConverter]` (`:117,126`) |
| `src/Snt.Lib.Wpf/Controls/ChildAlignmentDefaults.cs` | 106 | internal; `StackPanel.cs:14,213`, `WrapPanel.cs:16,455`, `Grid.cs:20,164` |
| `src/Snt.Lib.Wpf/Controls/WrapPanelLineAlignment.cs` | 27 | enum of `WrapPanel.LineAlignment` (`WrapPanel.cs:71,164`); geoDC sets it 9 times |

### 2b. Converters (1,371 lines)

| Snt.Lib file (`src/Snt.Lib.Wpf/Converters/`) | Lines | Role |
|---|---|---|
| `TrueToVisible.cs`, `FalseToVisible.cs`, `FalseToHidden.cs` | 17 each | used; derive from `BooleanToVisibility` |
| `NotNullToVisible.cs`, `NullToVisible.cs` | 17 each | used; derive from `ObjectToVisibility` |
| `NotNullOrEmptyToVisible.cs` 23, `NotNullOrWhiteSpaceToVisible.cs` 17 | 40 | used; derive from `StringToVisibility` (`NotNullOrEmptyToVisible.cs:18-21` overrides `HasValue`) |
| `NullOrEmptyEnumerableToVisible.cs` | 17 | used; derives from `EnumerableToVisibility` |
| `NotNullToBool.cs` 37, `BooleanNegation.cs` 54 | 91 | used; standalone |
| `EqualityToVisibility.cs` 55, `EqualityToBoolean.cs` 43, `EnumToBoolean.cs` 57 | 155 | used; need `ConverterValueHelper` + `EnumConversionMemo` |
| `StringToVisibility.cs` | 75 | used directly (`Controls.Cards.xaml:987`) and as a base |
| `LambdaConverter.cs` | 163 | used from C# (`AppConverters.cs:34-64`) |
| `BooleanToVisibility.cs` 92, `ObjectToVisibility.cs` 62, `EnumerableToVisibility.cs` 63 | 217 | base classes |
| `BoxedValues.cs` 28, `EnumerableConverterHelper.cs` 34 | 62 | internal helpers (`BooleanToVisibility.cs:54`, `EnumerableToVisibility.cs:55`) |
| `ConverterValueHelper.cs` 261, `EnumConversionMemo.cs` 33, `BoundedLruCache.cs` 138 | 432 | internal; equality/enum parsing with an LRU cache (`ConverterValueHelper.cs:10-11`). `TryConvertDouble`/`TryGetCount` (`:132-240`, ~110 lines) serve only unused number/count converters and can be dropped |
| `ConverterCache.cs` | 34 | public cache knob referenced by `ConverterValueHelper.cs:11` |

### 2c. Text runtime, WPF side (759 lines required, 847 with the two optional extensions)

| Snt.Lib file (`src/Snt.Lib.Wpf/Text/`) | Lines | Role |
|---|---|---|
| `TextResourceMap.cs` 66, `TextResourceKey.cs` 75, `GeneratedTextResourceAttribute.cs` 16 | 157 | emitted by the generator (`TextResourceGenerator.cs:486,533-534`) |
| `ITextMap.cs` 8, `BooleanTextMap.cs` 25, `EnumTextMap.cs` 43, `NumberTextMap.cs` 54, `StringTextMap.cs` 57 | 187 | emitted map types (`:562,575,592,600`); `StringTextMap.TryResolve` used by `TextInlinesBootstrap.cs:40` |
| `FormatTextExtension.cs` 53, `TextBindingConverter.cs` 34, `TextBindingOptions.cs` 15 | 102 | `snt:FormatText` |
| `MatchTextExtension.cs` 40, `MatchTextBindingConverter.cs` 42 | 82 | `snt:MatchText` |
| `TextInlines.cs` 144, `ITextPartRenderer.cs` 19, `TextPartRendererCollection.cs` 38, `TextTokenRenderer.cs` 30 | 231 | inline rendering used by App and App.UI |
| `StringTextExtension.cs` 55, `SwitchTextExtension.cs` 33 | 88 | optional: unused by geoDC XAML, but `XamlTextAnalyzer.cs:120-126` and `TextBindingTests`/`TextMarkupExtensionTests` cover them |

### 2d. Text primitives, no WPF (357 lines)

| Snt.Lib file (`src/Snt.Lib.General/Text/`) | Lines | Role |
|---|---|---|
| `TextPart.cs` 21, `TextRun.cs` 11, `TextArgument.cs` 32, `TextToken.cs` 28 | 92 | closed part hierarchy used by geoDC renderers |
| `TextMessage.cs` | 85 | used in App.Core, App.UI, App, Sample, tests |
| `TextTemplate.cs` 55, `TextTemplateSegment.cs` 125 | 180 | emitted by the generator (`TextResourceGenerator.cs:23,497`) and held by `TextResourceKey.cs:11` |

### 2e. Generator (1,360 lines) and assembly attributes

| Snt.Lib file | Lines | Note |
|---|---|---|
| `src/Snt.Lib.Wpf.Text.Generators/TextResourceGenerator.cs` | 968 | the generator |
| `src/Snt.Lib.Wpf.Text.Generators/XamlTextAnalyzer.cs` | 374 | the XAML analyzer |
| `src/Snt.Lib.Wpf.Text.Generators/buildTransitive/Snt.Lib.Wpf.Text.Generators.targets` | 18 | becomes project-local items in `geoDC.App.csproj` (§5) |
| `src/Snt.Lib.Wpf.Text.Generators/Snt.Lib.Wpf.Text.Generators.csproj:14-16` | 3 | `ImplicitUsings` off, `EnforceExtendedAnalyzerRules`, `NoWarn RS2008` |
| `src/Snt.Lib.Wpf/Properties/AssemblyInfo.cs:8-11` | 4 | `XmlnsPrefix snt` + three `XmlnsDefinition`s; `:5-7 ThemeInfo` and `Themes/Generic.xaml` are **not** needed (no vendored type has a style there); `:12 InternalsVisibleTo` is covered by `geoDC.App.UI.csproj:10` |

**Total source: ≈5,780 lines (5,866 with the optional extensions).**

### 2f. Tests worth carrying (3,161 lines, xUnit)

| Snt.Lib test file | Lines | Tests | Note |
|---|---|---|---|
| `tests/Snt.Lib.General.Tests/TextMessageTests.cs` | 128 | 9 | pure .NET |
| `tests/Snt.Lib.Wpf.Core.Tests/ConverterTests.cs` | 635 | 46 | 14 cover converters geoDC does not use |
| `tests/Snt.Lib.Wpf.Core.Tests/LayoutControlsTests.cs` | 719 | 23 | hand-rolled STA thread (`:698-710`) |
| `tests/Snt.Lib.Wpf.Core.Tests/StackPanelTests.cs` | 223 | 8 | |
| `tests/Snt.Lib.Wpf.Core.Tests/BoundedLruCacheTests.cs` | 92 | 7 | |
| `tests/Snt.Lib.Wpf.Core.Tests/TextBindingTests.cs` | 169 | 10 | needs `InternalsVisibleTo` (`CreateBinding`) |
| `tests/Snt.Lib.Wpf.Core.Tests/TextInlinesTests.cs` | 196 | 9 | |
| `tests/Snt.Lib.Wpf.Core.Tests/TextMarkupExtensionTests.cs` | 76 | 1 | |
| `tests/Snt.Lib.Wpf.Core.Tests/TextMessageBindingTests.cs` | 150 | 9 | |
| `tests/Snt.Lib.Wpf.Core.Tests/TextResourceGeneratorTests.cs` | 640 | 32 | drives `CSharpGeneratorDriver` (`:31`) |
| `tests/Snt.Lib.Wpf.Core.Tests/XamlTextAnalyzerTests.cs` | 133 | 5 | `AnalyzerOptions` + fake `AdditionalText` (`:91-100`); fixture URI must change |

Not to copy: `XamlNamespaceTests.cs` (asserts the `snt` prefix and the WpfUi assemblies), `NotificationServiceTests.cs`, `RateLimiterTests.cs`, `samples/`, `benchmarks/`.

## 3. External dependencies and licence handling

| Dependency | Needed by | Snt version | geoDC today | Licence |
|---|---|---|---|---|
| YamlDotNet | generator only (`TextResourceGenerator.cs:12`, `RepresentationModel` API) | 8.1.0 (`Snt.Lib Directory.Packages.props:16`); bundled into the package as `analyzers/dotnet/cs/YamlDotNet.dll` from `lib/netstandard1.3` (`Snt.Lib.Wpf.Text.Generators.csproj:21,28`) | absent | MIT |
| Microsoft.CodeAnalysis.CSharp | generator | 4.14.0 (`:11`) | 5.6.0 (`geoDC Directory.Packages.props:12`), already used by `src/geoDC.App.UI.Generators/geoDC.App.UI.Generators.csproj:12` | MIT |
| CommunityToolkit.Mvvm | **not** needed by the vendored set (only notification types) | 8.4.0 | 8.4.2 present | MIT |
| WPF-UI | not needed | 4.2.0 | absent | — |

geoDC's YAML uses no block scalars, anchors or aliases (0 hits in `src/geoDC.App/Strings/*.yaml`); only mappings, scalars and `match:`/`plural:`/`tokens:` maps (133 lines). A current YamlDotNet (16.x) keeps the `YamlStream`/`YamlMappingNode`/`Mark.Index` API the generator uses, so bump rather than pin 8.1.0.

What `docs/third-party-notices.md` requires: packages are listed from `ReferenceCopyLocalPaths` (`:23-27`, `src/geoDC.App/ThirdPartyNotices/ThirdPartyNotices.targets:48-50`); analyzers and `PrivateAssets="all"` packages never appear (`docs:24-25`), so YamlDotNet as an analyzer dependency needs **no** notice and no `bundled-components.xml` entry. `Snt.Lib.Wpf`/`Snt.Lib.General` currently appear as "Not declared in the package metadata" (`docs:65-66`; neither nuspec carries a licence, and `/home/user/snt.lib` has no LICENSE file). After vendoring they drop out of the notices automatically; delete the `docs:65-66` sentence. Both repositories are authored by the same person (Snt.Lib `git log`: Samuel Thornton / snt0; geoDC commits by snt0), so the vendored code is first-party, not a third-party component. Recommend a one-line provenance header (`// Vendored from snt.lib d545ca2 (src/Snt.Lib.Wpf/Controls/AutoGrid.cs)`) rather than a notice. Test fixtures that use `"Snt.Lib.Wpf"` as sample data (`tests/geoDC.App.Core.Tests/About/ThirdPartyNoticesReaderTests.cs:31,50`, `tests/geoDC.App.Tests/Popups/ThirdPartyNoticesPopupTests.cs:15,31`) keep working; rename for hygiene only.

## 4. How the generator works

**Inputs.**

- `TextResourceGenerator` (`TextResourceGenerator.cs:17-18`, `IIncrementalGenerator`) takes every `AdditionalText` whose path ends `.yaml` and contains a `/Strings/` segment (`:57`, `:836-840`), combined with the whole `Compilation` and the `build_property.SntTextGeneratedNamespace` option (`:21,53-59`). The package targets make `Strings\**\*.yaml`, all `@(Page)` and `@(ApplicationDefinition)` items AdditionalFiles unless `SntTextEnableAutomaticInputs=false` (`targets:3-13`).
- One class per file: `ToPascalCase(filename) + "Text"` (`:68`, so `shoreControl.yaml` → `ShoreControlText`); two files mapping to one class is `SNTTXT008`.
- Keys flatten to PascalCase members (`:146`), scalars become entries, maps are recognised by `plural:` or `match:` (`:158-170`): `match: bool`, `match: string` (with optional `tokens: kind` catalog, `:244-260`), otherwise an enum name resolved against the compilation (`:759-783`, exhaustive unless `default`).
- Placeholders `{name}`, `{name:Type}`, `{name:Type:format}` (`:655-685`); types are the built-ins (`:43-48`) or a unique public non-generic type found by walking the compilation (`:725-757`), which is why `ReferencedEntity`, `Length`, `Angle`, `geoDC.Measurements.Point`, `TextMessage` work in geoDC's YAML.
- Inline tokens `[text](kind:key)` (`:639-652`), `[[`/`]]` escapes (`:637`).
- Diagnostics `SNTTXT001`–`016` (`:26-41`), all Error severity, located at the YAML span (`:863-872`).

**Outputs.**

- One `<Class>.g.cs` per file (`:87`), `namespace <ns>;` + `public static class <Name>Text` (`:483-485`) holding a private `TextResourceMap Resources` with every composite format and, for templated entries, `TextTemplate` segments (`:486-501`);
- per scalar a `[GeneratedTextResourceAttribute(n)] public static TextResourceKey <Member>Key` (`:533-534`), then either `public const string <Member>` (`:538`) plus `<Member>Message` when it has tokens (`:539-544`), or a typed `string <Member>(args)` using `string.Format(CurrentCulture, …)` and a `<Member>Message(args)` (`:548-555`);
- maps emit `EnumTextMap<T>`/`NumberTextMap`/`BooleanTextMap`/`StringTextMap` `<Member>Map` properties and `<Member>(value)` lookups (`:558-619`).
- All emitted type names are hard-coded `global::Snt.Lib.Wpf.Text.*` / `global::Snt.Lib.General.Text.*` (`:22-23,486,497,533-534,562-563,575-576,592,600-601`) — every one must change on rename.
- Generated code therefore depends on the text runtime being referenced by the consuming project (geoDC.App gets it via App.UI).

**`XamlTextAnalyzer`** (`XamlTextAnalyzer.cs:14-15`, a `DiagnosticAnalyzer` with one compilation-end action, `:59`).

- It is **not** a literal-string detector; geoDC's own `tests/geoDC.Infrastructure.Tests/Conventions/XamlLiteralTextConventionTests.cs` does that.
- It parses each `.xaml` AdditionalFile as XML (`:64-76`), finds attribute values that are markup extensions whose prefix resolves to the hard-coded URI `https://schemas.sthornton.co.uk/snt/wpf` (`:17,106-108`) and whose name is `FormatText`/`StringText`/`SwitchText`/`MatchText` (`:115-130`), resolves the `{x:Static prefix:Class.Member}` argument through the attribute's xmlns **only when it is a `clr-namespace:` URI** (`:228-231,271-281`) and the member exists in the compilation (`:235-237`),
- then checks: member type is `Snt.Lib.Wpf.Text.TextResourceKey` (`:19,240-244`), `FormatText` argument count equals the `GeneratedTextResourceAttribute` value (`:151-156,246-253`), `MatchText` targets an `ITextMap` (`:192-196`).
- Diagnostics `SNTTXT100/101/102` (`:23-48`), Error. geoDC enables it implicitly: the package is an analyzer and all Pages are AdditionalFiles; no `.editorconfig` tuning exists.
- Two consequences for vendoring: (1) the three constants `:17-21` must become the geoDC URI and namespaces;
- (2) if the generated classes were ever mapped through an `XmlnsDefinition` URI instead of `clr-namespace`, resolution fails and every `FormatText`/`MatchText` use becomes an `SNTTXT100` build error (`:139-141`) — keep `xmlns:text` as `clr-namespace:`.

**Generated sources on disk.** Today: none (`geoDC.App.csproj` has no `EmitCompilerGeneratedFiles`; `.gitignore:3` ignores `obj/`). Visual Studio's Go To Definition / Find All References already work on generator output (Roslyn hosts it in the compilation); grep, ripgrep and non-Roslyn tooling do not.

- Option (a): add `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles><CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)Generated</CompilerGeneratedFilesOutputPath>` to `geoDC.App.csproj`, exactly as `geoDC.App.UI.csproj:6-7` already does → files at `src/geoDC.App/obj/Generated/<GeneratorAssembly>/<GeneratorType>/*.g.cs`, greppable after any build, no diff noise, may hold stale files until the next build; recommended.
- Option (b): commit the output: point `CompilerGeneratedFilesOutputPath` at a tracked folder, add `<Compile Remove="Generated/**" />` to avoid CS0101 duplicates, add a CI step that fails when a rebuild changes it; cost is a diff several times the YAML size on every copy change (each entry emits 4–8 lines) and merge conflicts; only worth it if reviewers need to read generated code in PRs.
- `StringResourceConventionTests` (`tests/geoDC.Infrastructure.Tests/Conventions/StringResourceConventionTests.cs:25`) greps for *usages* `\b[A-Z]\w*Text\.\w+`, not definitions, so neither option affects it.

**Localisation.**

- The design is single-culture but not hostile to localisation: all text flows through one lookup, `TextResourceMap.Resolve` (`TextResourceMap.cs:65`), and the XAML side binds through `TextResourceKey`/`ITextMap` indirections.
- The one thing that bakes text into callers is the `public const string` for plain entries (`TextResourceGenerator.cs:538`), which the compiler inlines.
- Making text culture-switchable later is a generator change (const → static property reading the current culture's dictionary; `{x:Static}` works on properties) plus per-culture YAML, with every consumer in the same solution recompiling.
- Owning the generator makes that easier, not harder.
- During vendoring, do not flatten `…Key`/`…Map` references into literal strings and keep `Strings/*.yaml` the single source.

## 5. Destination plan

| Group | Proposed project / folder / namespace | Collisions & notes |
|---|---|---|
| General.Text primitives (7 files) | `src/geoDC.App.Core/Text/`, `geoDC.App.Core.Text` | App.Core already consumes `TextMessage` (`Diagnostics/Diagnostic.cs:1,30`), is `net10.0` with no WPF (`geoDC.App.Core.csproj:15`), and is referenced by App.UI (`geoDC.App.UI.csproj:22`). The files use only `System.*`. The `sntText` xmlns in `Controls.Badges.xaml:8` becomes `clr-namespace:geoDC.App.Core.Text;assembly=geoDC.App.Core` (a URI mapping is impossible: App.Core cannot reference `XmlnsDefinitionAttribute` without WPF, and the WPF markup compiler ignores `XmlnsDefinition.AssemblyName`, per Snt.Lib commit d545ca2 message). |
| Panels (6 files) | `src/geoDC.App.UI/UI/Panels/`, `geoDC.App.UI.Panels` (already in the URI, `src/geoDC.App.UI/Properties/AssemblyInfo.cs:11`; `FlowPanel` lives there) | Names `Grid`/`StackPanel` collide with `System.Windows.Controls` in any C# file that imports both (Snt itself aliases, `StackPanel.cs:5`). XAML is unaffected (`<Grid>` vs `<ui:Grid>`). Decision: keep Snt names (zero rename churn, mirrors Snt) or rename to `UiStackPanel`/`UiWrapPanel`/`UiAutoGrid`/`UiGrid` per the toolkit's `Ui*` convention — costs nothing extra if done in the same prefix regex pass (718 opening + 718 closing tags). `FlowPanel` is a different panel (`UI/Panels/FlowPanel.cs:14`, width-constrained rows), not a duplicate of `WrapPanel`; keep both (`AGENTS.md:108` already says so). |
| Converters (24 files) | `src/geoDC.App.UI/UI/Converters/`, `geoDC.App.UI.Converters` (in URI, `AssemblyInfo.cs:9`) | Existing geoDC converters (`SameReferenceToVisible`, `AbsentValue`, `ZeroPad`, `Thousands`, `SignedAngle`, `UiFractionColumn`, `UiSection*Converter`; App-level `AppConverters`, `StringEqualsMultiConverter`, `MetersMultiConverter`, `BindingProxy`, `DecimalPlacesPrecisionConverter`) overlap none of the Snt ones; `SameReferenceToVisible` is reference equality, `EqualityToVisibility` is `Equals` + enum-name parsing (`ConverterValueHelper.cs:29-51`) — keep both. `LambdaConverter` moves with them (consumer `AppConverters.cs:12`). Fix the stale "XAML prefix `conv:`" comment at `AppConverters.cs:18-19` (no `conv` xmlns exists). |
| Text runtime (17–19 files) | `src/geoDC.App.UI/UI/Text/`, `geoDC.App.UI.Text`; add `[assembly: XmlnsDefinition("https://schemas.geodc/ui", "geoDC.App.UI.Text")]` to `AssemblyInfo.cs` | `MatchText`, `FormatText`, `TextInlines.*` then resolve as `ui:`. No existing `geoDC.App.UI.Text` namespace. |
| Generator (2 files + targets content) | Preferred: new `src/geoDC.App.Text.Generators` (netstandard2.0, mirrors `geoDC.App.UI.Generators.csproj:2-13`). Alternative in the brief: fold into `src/geoDC.App.UI.Generators`. | Folding works technically — `TokenCatalogGenerator` filters `Tokens.*.xaml` names (`TokenCatalogGenerator.cs:204-210`, all four such files are in App.UI) and the text generator filters `Strings/*.yaml`, so neither fires in the other's project — but it would attach YamlDotNet to the toolkit generator, make App.UI load `XamlTextAnalyzer` for its Tokens AdditionalFiles, and couple the toolkit (which has no strings, `docs/string-resources.md:171`) to app copy tooling. Separate project keeps the dependency graph honest. |
| Generated namespace | `Snt.Generated.Text` → `geoDC.App.Text` (hard-code the default at `TextResourceGenerator.cs:20`; keep or drop the MSBuild override) | `StringResourceConventionTests.cs:25` regex is namespace-agnostic; `docs/string-resources.md:3-5,46-47,229-230` must change; 221 `using` lines and 70 `xmlns:text` lines change. Avoid `geoDC.App.Core.Text` as the generated namespace: it is a different assembly. |
| xmlns | Fold everything into `https://schemas.geodc/ui`. Then either keep `xmlns:snt` remapped to that URI (170 one-line edits, two prefixes for one URI in 151 files) or delete it and use `ui:` (recommended end state). | 151 of the 170 files already declare `xmlns:ui="https://schemas.geodc/ui"`. The other 19 (3 App menu templates, 16 App.UI theme/template files, listed in §6) bind `ui` to `clr-namespace:geoDC.App.UI.Controls` (`Controls.Badges.xaml:11`) and consistently use clr-namespace mappings inside the App.UI assembly; give them `xmlns:panels=`, `xmlns:converters=`, `xmlns:text="clr-namespace:geoDC.App.UI.…"` prefixes rather than changing their `ui` binding. |
| geoDC.App.Core | uses only `Snt.Lib.General.Text.TextMessage` (`Diagnostic.cs:1,30`); after the move it owns the type and the package reference at `geoDC.App.Core.csproj:11` goes. No WPF enters App.Core. |

Wiring the generator as a project (replaces `buildTransitive` targets) in `geoDC.App.csproj`: `<ProjectReference Include="…Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` (pattern at `geoDC.App.UI.csproj:24-26`), `<AdditionalFiles Include="Strings\**\*.yaml" />`, `<AdditionalFiles Include="@(Page);@(ApplicationDefinition)" />` (or a shared `.targets`), optional `<CompilerVisibleProperty Include="GeoDCTextGeneratedNamespace" />`. A `ProjectReference` analyzer does **not** load its NuGet dependencies into the compiler, so YamlDotNet must be surfaced explicitly: in the generator csproj `<PackageReference Include="YamlDotNet" GeneratePathProperty="true" PrivateAssets="all" />` and in geoDC.App `<Analyzer Include="$(PkgYamlDotNet)\lib\netstandard2.0\YamlDotNet.dll" />` (or a `GetTargetPath`-style target that appends it to the analyzer's `TargetPathWithTargetPlatformMoniker`). This is the one non-obvious build step; verify with a clean `dotnet build src/geoDC.App` before touching XAML.

## 6. Mechanical edit estimate

| Area | Count | Safe search-and-replace? |
|---|---|---|
| XAML `xmlns:snt` declarations | 170 lines / 170 files | yes (delete or remap) |
| XAML `snt:` usage tokens | ≈1,870 (2,042 − 170 declarations), in the same 170 files | yes for `snt:(StackPanel|WrapPanel|AutoGrid|Grid|TrueToVisible|…)\b` → `ui:…` in the 151 URI files; the 19 clr-namespace files need per-namespace prefixes (care) |
| XAML `xmlns:text` | 70 lines / 70 files (all App) | yes (`clr-namespace:Snt.Generated.Text` → `clr-namespace:geoDC.App.Text`) |
| XAML `sntText` | 1 line + 1 `x:Type` (`Controls.Badges.xaml:8,403`) | yes |
| C# `using` lines | 255 (221 generated-namespace + 22 + 11 + 1) in ≈240 files | yes, four literal replacements |
| C# files with `using Snt.Lib.*` (review, usings only) | 24, listed below | yes |
| C# doc crefs / comments | `UiHelpBadge.cs:71,82`, `AppConverters.cs:18` | yes |
| Generated member references | 1,580 XAML + 2,109 C# | **untouched** if class names stay `<Name>Text` |
| csproj/props/config | `geoDC.App.csproj:21-22` (−2, +~8), `geoDC.App.Core.csproj:11` (−1), `geoDC.App.UI.csproj:19` (−1), `AssemblyInfo.cs` (+1), `Directory.Packages.props:33-35` (−3, +1 YamlDotNet), `nuget.config:6,12-14` (−4), new generator csproj (~25 lines) | manual |
| Generator/analyzer source | `TextResourceGenerator.cs:20-23` and the nine `global::Snt.…` emit sites; `XamlTextAnalyzer.cs:17-21`; namespaces of all 5,780 vendored lines | yes (literal), then build |
| Code style | geoDC `.editorconfig:32-34` makes `var` an **error** (186 `var` in the vendored set), plus `csharp_prefer_system_threading_lock` (`BoundedLruCache.cs:13`), brace/using-placement warnings that CI turns into errors (`Directory.Build.props:19-21`, `.github/workflows/ci.yml:199`) | `dotnet format` in a separate commit; do not exempt the folder |
| Docs | 9 files, ≈36 lines: `AGENTS.md:108`, `docs/ui/README.md` 1, `docs/ui/controls.md` 15, `docs/ui/layout.md:210-225` 4, `docs/string-resources.md` 10, `docs/third-party-notices.md:65-66`, `.claude/skills/geodc-wpf-ui-builder/SKILL.md` 2 + `references/ui-patterns.md` 1, `.claude/skills/geodc-popup-workflow-builder/SKILL.md` 1 (and their `.agents/skills` mirrors) | manual |
| Tests to port | 11 files, 3,161 lines, ≈160 relevant `[Fact]`/`[Theory]` → NUnit (`Directory.Build.props:53-61`), STA via `tests/StaTestRunner.cs` instead of hand-rolled threads | semi-mechanical |


The 24 files with `using Snt.Lib.*` (no API changes, usings only):

- `src/geoDC.App.Core/Diagnostics/Diagnostic.cs`
- `src/geoDC.App.UI/UI/Behaviors/ContextualHelp.cs`
- `src/geoDC.App.UI/UI/Controls/InlineDescription.cs`
- `src/geoDC.App.UI/UI/Controls/UiReferenceChip.cs`
- `src/geoDC.App.UI/UI/Controls/UiReferenceChipRenderer.cs`
- `src/geoDC.App.UI/UI/Controls/UiTerm.cs`
- `src/geoDC.App.UI/UI/Controls/UiTermRenderer.cs`
- `src/geoDC.App/Converters/AppConverters.cs`
- `src/geoDC.App/Startup/TextInlinesBootstrap.cs`
- `src/geoDC.App/Popups/VrfTransformations/AlignVrfBaselinesPopupViewModel.cs`
- `src/geoDC.App/Popups/VrfTransformations/VrfTransformationDetailsPopupViewModel.cs`
- `src/geoDC.App/Popups/VrfTransformations/VrfTransformationDisplay.cs`
- `src/geoDC.App/Popups/VrfTransformations/VrfTransformationIssueText.cs`
- `src/geoDC.App/Popups/VrfTransformations/VrfTransformationReviewViewModel.cs`
- `src/geoDC.App.UI.Sample/Specimens/Foundations/UiReferenceChipSpecimens.xaml.cs`
- `src/geoDC.App.UI.Sample/Specimens/Foundations/UiTermSpecimens.xaml.cs`
- `tests/geoDC.App.Tests/Popups/VrfTransformationIssueTextTests.cs`
- `tests/geoDC.App.Tests/Popups/VrfTransformationPopupTests.cs`
- `tests/geoDC.App.Tests/Startup/TextInlinesBootstrapTests.cs`
- `tests/geoDC.App.Tests/ViewModels/GnssCheck/GnssDiagnosticRuleTests.cs`
- `tests/geoDC.App.Tests/ViewModels/Verification/VerificationDiagnosticRuleTests.cs`
- `tests/geoDC.App.UI.Tests/Controls/UiHelpBadgeTests.cs`
- `tests/geoDC.App.UI.Tests/Controls/UiReferenceChipTests.cs`
- `tests/geoDC.App.UI.Tests/Controls/UiTermTests.cs`

The 19 XAML files that declare `xmlns:snt` but bind `ui` to a clr-namespace (need per-namespace prefixes, §5):

- `src/geoDC.App/Menus/SurveyFeature/SurveyFeatureMenuTemplate.xaml`
- `src/geoDC.App/Menus/Calibration/CalibrationMenuTemplate.xaml`
- `src/geoDC.App/Menus/Verification/VerificationMenuTemplate.xaml`
- `src/geoDC.App.UI/Themes/Controls.Badges.xaml` (also the `sntText` DataTemplate, `:403`)
- `src/geoDC.App.UI/Themes/Controls.BuiltIns.xaml`
- `src/geoDC.App.UI/Themes/Controls.Calendar.xaml`
- `src/geoDC.App.UI/Themes/Controls.Cards.xaml`
- `src/geoDC.App.UI/Themes/Controls.ConventionToken.xaml`
- `src/geoDC.App.UI/Themes/Controls.CrsPicker.xaml`
- `src/geoDC.App.UI/Themes/Controls.Custom.xaml`
- `src/geoDC.App.UI/Themes/Controls.Inputs.xaml`
- `src/geoDC.App.UI/Themes/Controls.Lists.xaml`
- `src/geoDC.App.UI/Themes/Controls.Notifications.xaml`
- `src/geoDC.App.UI/Themes/Controls.Popups.xaml`
- `src/geoDC.App.UI/Themes/Controls.Primitives.xaml`
- `src/geoDC.App.UI/Themes/Controls.Sections.xaml`
- `src/geoDC.App.UI/Themes/Controls.Wizard.xaml`
- `src/geoDC.App.UI/Themes/Templates/DialogPopupTemplate.xaml`
- `src/geoDC.App.UI/Themes/Templates/LoadingPopupTemplate.xaml`

## 7. Risks and ordering

1. **Behaviour**: none expected — the vendored source is the exact commit the package was built from (§0), no geoDC converter duplicates an Snt one, and panel/extension code is copied unchanged. The only runtime-affecting edits are namespace strings. `EnumToBoolean.ConvertBack` (`EnumToBoolean.cs:41-56`) backs radio buttons in `ShoreControlRegisterView.xaml:254-260`; cover it with the ported `EnumToBoolean_ConvertsAndConvertsBack` test.
2. **Analyzer resolution**: `XamlTextAnalyzer` hard-codes the URI and type names (`:17-21`) and only resolves `clr-namespace:` xmlns (`:271-281`); get both wrong and the build either validates nothing (URI mismatch → silent) or fails everywhere (`SNTTXT100`). Port `XamlTextAnalyzerTests.cs` first and point its fixture at the geoDC URI.
3. **Analyzer dependency loading** (YamlDotNet with a `ProjectReference` analyzer, §5). Symptom if missing: `CS8032 An instance of analyzer … cannot be created … FileNotFoundException YamlDotNet`, and every `text:` reference then fails. Verify on a clean clone and in CI.
4. **Roslyn version**: building the generator against 5.6.0 (geoDC pin) instead of 4.14.0 is fine for an in-repo analyzer consumed by the same SDK; it is not fine if the project were ever packed for an older consumer. Keep `EnforceExtendedAnalyzerRules` (`Snt csproj:15`).
5. **Two `TextMessage` types during a split migration**: generated code emits `Snt.Lib.General.Text.TextMessage` until the generator is swapped, while geoDC code would reference `geoDC.App.Core.Text.TextMessage`. The text stack (General.Text + Wpf.Text + generator + namespace rename) therefore moves in **one** PR; panels and converters can move independently and earlier.
6. **Code-style diff noise** (§6): do the move as a verbatim copy commit followed by a formatting commit, so reviewers can diff the copy against Snt.Lib.
7. **Coexistence window**: between the panel PR and the text PR, XAML files carry both `ui:` (vendored panels/converters) and `snt:` (Snt text extensions). No ambiguity as long as the panel/converter names are removed from the `snt:` URI only once their package goes.
8. **YamlDotNet upgrade** from 8.1.0: run the ported `TextResourceGeneratorTests` (32 tests, including error-location tests that depend on `Mark.Index`).
9. **Localisation**: unaffected (§4); record the const→property path in `docs/string-resources.md` when rewriting its "generator ships in the Snt packages" paragraph (`:229-230`).

Recommended PR sequence (each builds and passes CI on its own):

| # | Scope | Removes |
|---|---|---|
| 1 | Panels + converters + `LambdaConverter` into `geoDC.App.UI` (verbatim copy commit, then format commit); `XmlnsDefinition` already covers the namespaces; XAML regex pass for those names only; port `ConverterTests` (32 relevant), `LayoutControlsTests`, `StackPanelTests`, `BoundedLruCacheTests` onto `StaTestRunner`; `AGENTS.md:108`, `docs/ui/layout.md` | nothing yet (Snt.Lib.Wpf still referenced for text) |
| 2 | Text stack: `geoDC.App.Core/Text`, `geoDC.App.UI/UI/Text` (+ `XmlnsDefinition`), `src/geoDC.App.Text.Generators` with YamlDotNet, `geoDC.App.csproj` wiring, `Snt.Generated.Text` → `geoDC.App.Text`, 255 `using` edits, 70 `xmlns:text` + 1 `sntText` edits, remaining `snt:` → `ui:`/clr-namespace prefixes, analyzer constants; port the six text/generator test files and `TextMessageTests`; `docs/string-resources.md` | `Snt.Lib.*` from all three csproj files, `Directory.Packages.props:33-35`, the `ST` source and mapping in `nuget.config` |
| 3 | Follow-ups: `EmitCompilerGeneratedFiles` on `geoDC.App` (grep-ability), optional trims (`StringText`/`SwitchText`, unused `ConverterValueHelper` members), `docs/third-party-notices.md:65-66`, stale `conv:` comment, test-fixture names, `docs/ui/controls.md` | — |

After PR 2 geoDC has no private-feed dependency; `nuget.config` can be reduced to nuget.org only.
